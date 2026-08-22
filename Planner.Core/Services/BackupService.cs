using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Security;
using Planner.Core;

namespace Planner.Core.Services;

public sealed class BackupService
{
    private static readonly byte[] MagicDb = "PLNDB1"u8.ToArray();
    private static readonly byte[] MagicVault = "PLNVLT1"u8.ToArray();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly VaultService _vault;
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly HabitService _habits;
    private readonly DailyNoteService _notes;
    private readonly LeaveService _leaves;

    public BackupService(
        IDbContextFactory<PlannerDbContext> factory,
        VaultService vault,
        TaskService tasks,
        CategoryService categories,
        HabitService habits,
        DailyNoteService notes,
        LeaveService leaves)
    {
        _factory = factory;
        _vault = vault;
        _tasks = tasks;
        _categories = categories;
        _habits = habits;
        _notes = notes;
        _leaves = leaves;
    }

    public async Task ExportVaultAsync(string destPath, string password, CancellationToken ct = default)
    {
        if (!await VerifyVaultPasswordAsync(password, ct))
        {
            throw new InvalidOperationException("Kasa şifresi yanlış.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var meta = await db.Vault.AsNoTracking().FirstOrDefaultAsync(ct)
                   ?? throw new InvalidOperationException("Kasa henüz oluşturulmamış.");
        var contacts = await db.Contacts.AsNoTracking().ToListAsync(ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new VaultBackupDto
        {
            Salt = Convert.ToBase64String(meta.PasswordSalt),
            Verifier = Convert.ToBase64String(meta.KeyVerifier),
            Iterations = meta.Iterations,
            Contacts = contacts.Select(c => new VaultContactDto
            {
                Id = c.Id,
                Payload = Convert.ToBase64String(c.Payload),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            }).ToList()
        }, Json);

        await File.WriteAllBytesAsync(destPath, Wrap(MagicVault, password, payload), ct);
    }

    public async Task ImportVaultAsync(string sourcePath, string password, CancellationToken ct = default)
    {
        var json = Unwrap(MagicVault, password, await File.ReadAllBytesAsync(sourcePath, ct));
        var dto = JsonSerializer.Deserialize<VaultBackupDto>(json, Json)
                  ?? throw new InvalidOperationException("Yedek dosyası okunamadı.");

        var salt = Convert.FromBase64String(dto.Salt);
        var key = EncryptionService.DeriveKey(password, salt, dto.Iterations);
        if (!EncryptionService.Verify(key, Convert.FromBase64String(dto.Verifier)))
        {
            EncryptionService.Zero(key);
            throw new InvalidOperationException("Şifre bu yedekle eşleşmiyor.");
        }

        EncryptionService.Zero(key);

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Contacts.RemoveRange(await db.Contacts.ToListAsync(ct));
        db.Vault.RemoveRange(await db.Vault.ToListAsync(ct));
        db.Vault.Add(new VaultMeta
        {
            Id = 1,
            PasswordSalt = salt,
            KeyVerifier = Convert.FromBase64String(dto.Verifier),
            Iterations = dto.Iterations,
            CreatedAt = DateTime.Now
        });
        foreach (var c in dto.Contacts)
        {
            db.Contacts.Add(new EncryptedContact
            {
                Id = c.Id,
                Payload = Convert.FromBase64String(c.Payload),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            });
        }

        await db.SaveChangesAsync(ct);
        _vault.Lock();
    }

    public async Task ExportDatabaseAsync(string destPath, string password, CancellationToken ct = default)
    {
        ValidateBackupPassword(password);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(FULL);");
        await db.Database.CloseConnectionAsync();

        var bytes = await File.ReadAllBytesAsync(AppPaths.DatabaseFile, ct);
        await File.WriteAllBytesAsync(destPath, Wrap(MagicDb, password, bytes), ct);
    }

    public async Task RestoreDatabaseAsync(string sourcePath, string password, CancellationToken ct = default)
    {
        var decrypted = Unwrap(MagicDb, password, await File.ReadAllBytesAsync(sourcePath, ct));
        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(FULL);");
            await db.Database.CloseConnectionAsync();
        }

        SqliteConnection.ClearAllPools();
        var temp = AppPaths.DatabaseFile + ".restore";
        await File.WriteAllBytesAsync(temp, decrypted, ct);
        File.Copy(temp, AppPaths.DatabaseFile, overwrite: true);
        File.Delete(temp);
        var wal = AppPaths.DatabaseFile + "-wal";
        var shm = AppPaths.DatabaseFile + "-shm";
        if (File.Exists(wal)) File.Delete(wal);
        if (File.Exists(shm)) File.Delete(shm);
    }

    public async Task ExportPublicJsonAsync(string destPath, CancellationToken ct = default)
    {
        var tasks = await _tasks.GetAllAsync(ct);
        var categories = await _categories.GetAllAsync(ct);
        var habits = await _habits.GetAllAsync(ct);
        var notes = await _notes.GetAllAsync(ct);
        var leaves = await _leaves.GetAllAsync(ct);
        var dto = new
        {
            exportedAt = DateTime.Now,
            contactsIncluded = false,
            note = "Kişi verileri dahil edilmedi (kasa kilitli veya PII korunuyor).",
            categories = categories.Select(c => new { c.Id, c.Name, c.ColorHex }),
            tasks = tasks.Select(t => new
            {
                t.Id,
                t.Title,
                t.Notes,
                t.Date,
                t.Time,
                t.ReminderAt,
                status = t.Status.ToDisplay(),
                category = t.Category.Name,
                recurrence = t.RecurrenceKind.ToDisplay()
            }),
            habits = habits.Select(h => new { h.Id, h.Name, schedule = h.ScheduleKind.ToString(), h.ReminderTime }),
            dailyNotes = notes.Select(n => new { n.Date, n.Content }),
            leaves = leaves.Select(l => new
            {
                l.Id,
                type = l.Type.Name,
                durationKind = l.DurationKind.ToDisplay(),
                l.StartDate,
                l.EndDate,
                startTime = l.StartTime?.ToString("HH\\:mm"),
                endTime = l.EndTime?.ToString("HH\\:mm"),
                startHalf = l.StartHalf.ToDisplay(),
                endHalf = l.EndHalf.ToDisplay(),
                l.Note,
                status = l.Status.ToDisplay()
            })
        };
        await File.WriteAllTextAsync(destPath, JsonSerializer.Serialize(dto, Json), Encoding.UTF8, ct);
    }

    private async Task<bool> VerifyVaultPasswordAsync(string password, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var meta = await db.Vault.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null)
        {
            return false;
        }

        var key = EncryptionService.DeriveKey(password, meta.PasswordSalt, meta.Iterations);
        var ok = EncryptionService.Verify(key, meta.KeyVerifier);
        EncryptionService.Zero(key);
        return ok;
    }

    private static void ValidateBackupPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException("Yedek şifresi en az 8 karakter olmalıdır.");
        }
    }

    private static byte[] Wrap(byte[] magic, string password, byte[] payload)
    {
        ValidateBackupPassword(password);
        var salt = EncryptionService.GenerateSalt();
        var key = EncryptionService.DeriveKey(password, salt);
        var blob = EncryptionService.Encrypt(payload, key);
        EncryptionService.Zero(key);
        var iter = BitConverter.GetBytes(EncryptionService.DefaultIterations);
        var result = new byte[magic.Length + salt.Length + iter.Length + blob.Length];
        Buffer.BlockCopy(magic, 0, result, 0, magic.Length);
        Buffer.BlockCopy(salt, 0, result, magic.Length, salt.Length);
        Buffer.BlockCopy(iter, 0, result, magic.Length + salt.Length, iter.Length);
        Buffer.BlockCopy(blob, 0, result, magic.Length + salt.Length + iter.Length, blob.Length);
        return result;
    }

    private static byte[] Unwrap(byte[] magic, string password, byte[] file)
    {
        if (file.Length < magic.Length + EncryptionService.SaltSize + 4 + EncryptionService.NonceSize + EncryptionService.TagSize)
        {
            throw new InvalidOperationException("Yedek dosyası bozuk veya eksik.");
        }

        if (!file.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            throw new InvalidOperationException("Yedek dosyası bu işlem için uygun değil.");
        }

        var salt = file.AsSpan(magic.Length, EncryptionService.SaltSize).ToArray();
        var iter = BitConverter.ToInt32(file, magic.Length + EncryptionService.SaltSize);
        if (iter is < 10_000 or > 2_000_000)
        {
            throw new InvalidOperationException("Yedek dosyası bozuk.");
        }

        var blob = file[(magic.Length + EncryptionService.SaltSize + 4)..];
        var key = EncryptionService.DeriveKey(password, salt, iter);
        try
        {
            return EncryptionService.Decrypt(blob, key);
        }
        catch
        {
            throw new InvalidOperationException("Şifre yanlış veya yedek bozulmuş.");
        }
        finally
        {
            EncryptionService.Zero(key);
        }
    }

    private sealed class VaultBackupDto
    {
        public string Salt { get; set; } = "";
        public string Verifier { get; set; } = "";
        public int Iterations { get; set; }
        public List<VaultContactDto> Contacts { get; set; } = [];
    }

    private sealed class VaultContactDto
    {
        public Guid Id { get; set; }
        public string Payload { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
