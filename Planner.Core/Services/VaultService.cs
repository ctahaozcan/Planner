using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Security;

namespace Planner.Core.Services;

public sealed class VaultService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private byte[]? _key;

    public VaultService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public bool IsUnlocked => _key is not null;

    public async Task<bool> HasPasswordAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vault.AnyAsync(ct);
    }

    public async Task SetupPasswordAsync(string password, CancellationToken ct = default)
    {
        ValidatePassword(password);
        if (await HasPasswordAsync(ct))
        {
            throw new InvalidOperationException("Kasa şifresi zaten ayarlanmış.");
        }

        var salt = EncryptionService.GenerateSalt();
        var key = EncryptionService.DeriveKey(password, salt);
        var verifier = EncryptionService.CreateVerifier(key);

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Vault.Add(new VaultMeta
        {
            Id = 1,
            PasswordSalt = salt,
            KeyVerifier = verifier,
            Iterations = EncryptionService.DefaultIterations,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
        _key = key;
    }

    public async Task<bool> UnlockAsync(string password, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var meta = await db.Vault.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null)
        {
            return false;
        }

        var key = EncryptionService.DeriveKey(password, meta.PasswordSalt, meta.Iterations);
        if (!EncryptionService.Verify(key, meta.KeyVerifier))
        {
            EncryptionService.Zero(key);
            return false;
        }

        Lock();
        _key = key;
        return true;
    }

    public void Lock()
    {
        EncryptionService.Zero(_key);
        _key = null;
    }

    public async Task<IReadOnlyList<ContactRecord>> GetContactsAsync(CancellationToken ct = default)
    {
        var key = RequireKey();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Contacts.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var list = new List<ContactRecord>(rows.Count);
        foreach (var row in rows)
        {
            var json = Encoding.UTF8.GetString(EncryptionService.Decrypt(row.Payload, key));
            var record = JsonSerializer.Deserialize<ContactRecord>(json, JsonOptions);
            if (record is not null)
            {
                record.Id = row.Id;
                record.SocialAccounts ??= [];
                list.Add(record);
            }
        }

        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task AddAsync(ContactRecord contact, CancellationToken ct = default)
    {
        var key = RequireKey();
        contact.Id = contact.Id == Guid.Empty ? Guid.NewGuid() : contact.Id;
        if (contact.CreatedAt == default)
        {
            contact.CreatedAt = DateTime.Now;
        }

        var payload = EncryptionService.Encrypt(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contact, JsonOptions)),
            key);

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Contacts.Add(new EncryptedContact
        {
            Id = contact.Id,
            Payload = payload,
            CreatedAt = contact.CreatedAt,
            UpdatedAt = DateTime.Now
        });
        WriteEncryptedSocials(db, contact, key);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ContactRecord contact, CancellationToken ct = default)
    {
        var key = RequireKey();
        var payload = EncryptionService.Encrypt(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contact, JsonOptions)),
            key);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contact.Id, ct)
                  ?? throw new InvalidOperationException("Kişi bulunamadı.");
        row.Payload = payload;
        row.UpdatedAt = DateTime.Now;
        WriteEncryptedSocials(db, contact, key);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        RequireKey();
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        if (row is null)
        {
            return;
        }

        db.Contacts.Remove(row);
        db.SocialAccounts.RemoveRange(await db.SocialAccounts.Where(s => s.ContactId == id).ToListAsync());
        await db.SaveChangesAsync();
        PortraitStore.DeleteForPerson(id);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        if (!await UnlockAsync(currentPassword, ct))
        {
            throw new InvalidOperationException("Mevcut şifre yanlış.");
        }

        var contacts = await GetContactsAsync(ct);
        var newSalt = EncryptionService.GenerateSalt();
        var newKey = EncryptionService.DeriveKey(newPassword, newSalt);
        var verifier = EncryptionService.CreateVerifier(newKey);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var meta = await db.Vault.FirstAsync(ct);
        meta.PasswordSalt = newSalt;
        meta.KeyVerifier = verifier;
        meta.Iterations = EncryptionService.DefaultIterations;

        var rows = await db.Contacts.ToListAsync(ct);
        foreach (var row in rows)
        {
            var contact = contacts.First(c => c.Id == row.Id);
            row.Payload = EncryptionService.Encrypt(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contact, JsonOptions)),
                newKey);
            row.UpdatedAt = DateTime.Now;
            WriteEncryptedSocials(db, contact, newKey);
        }

        await db.SaveChangesAsync(ct);
        if (_key is not null)
        {
            PortraitStore.ReencryptAll(_key, newKey);
        }

        Lock();
        _key = newKey;
    }

    public async Task ResetVaultAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SocialAccounts.RemoveRange(await db.SocialAccounts.ToListAsync(ct));
        db.Contacts.RemoveRange(await db.Contacts.ToListAsync(ct));
        db.Vault.RemoveRange(await db.Vault.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        PortraitStore.DeleteAll();
        Lock();
    }

    public void SavePortrait(Guid personId, byte[] original, byte[] thumbnail)
    {
        var key = RequireKey();
        if (original.Length > PortraitStore.MaxBytes)
        {
            throw new InvalidOperationException("Fotoğraf 5 MB sınırını aşıyor.");
        }

        PortraitStore.WriteEncrypted(PortraitStore.OriginalName(personId), original, key);
        PortraitStore.WriteEncrypted(PortraitStore.ThumbName(personId), thumbnail, key);
    }

    public byte[]? TryLoadThumbnail(Guid personId)
    {
        if (_key is null)
        {
            return null;
        }

        try
        {
            return PortraitStore.ReadDecrypted(PortraitStore.ThumbName(personId), _key);
        }
        catch
        {
            return null;
        }
    }

    public byte[]? TryLoadOriginal(Guid personId)
    {
        if (_key is null)
        {
            return null;
        }

        try
        {
            return PortraitStore.ReadDecrypted(PortraitStore.OriginalName(personId), _key)
                   ?? PortraitStore.ReadDecrypted(PortraitStore.ThumbName(personId), _key);
        }
        catch
        {
            return null;
        }
    }

    public void DeletePortrait(Guid personId) => PortraitStore.DeleteForPerson(personId);

    private static void WriteEncryptedSocials(PlannerDbContext db, ContactRecord contact, byte[] key)
    {
        var existing = db.SocialAccounts.Where(s => s.ContactId == contact.Id).ToList();
        db.SocialAccounts.RemoveRange(existing);
        foreach (var account in contact.SocialAccounts ?? [])
        {
            if (string.IsNullOrWhiteSpace(account.Value))
            {
                continue;
            }

            if (account.Id == Guid.Empty)
            {
                account.Id = Guid.NewGuid();
            }

            db.SocialAccounts.Add(new EncryptedSocialAccount
            {
                Id = Guid.NewGuid(),
                ContactId = contact.Id,
                Payload = EncryptionService.Encrypt(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(account, JsonOptions)),
                    key)
            });
        }
    }

    private byte[] RequireKey()
        => _key ?? throw new InvalidOperationException("Kişiler kasası kilitli.");

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException("Şifre en az 8 karakter olmalıdır.");
        }
    }
}
