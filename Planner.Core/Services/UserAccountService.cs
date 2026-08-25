using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Security;

namespace Planner.Core.Services;

public sealed class UserAccountService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly SettingsService _settings;

    public UserAccountService(IDbContextFactory<PlannerDbContext> factory, SettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public AppUser? Current { get; private set; }

    public string CurrentKey => Current?.Id.ToString("N") ?? "";
    public string CurrentName => Current?.DisplayName ?? "Ben";
    public bool IsSignedIn => Current is not null;

    public async Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AppUsers.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct);
    }

    public async Task<bool> HasAnyAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AppUsers.AnyAsync(ct);
    }

    public async Task EnsureDefaultAsync(string displayName, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.AppUsers.AnyAsync(ct))
        {
            if (await _settings.GetBoolAsync(SettingKeys.RememberLogin, false))
            {
                await RestoreSessionAsync(db, ct);
            }

            return;
        }

        await RestoreSessionAsync(db, ct);
    }

    public async Task RestoreSessionAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await RestoreSessionAsync(db, ct);
    }

    private async Task RestoreSessionAsync(PlannerDbContext db, CancellationToken ct)
    {
        var idText = await _settings.GetAsync(SettingKeys.CurrentUserId, "");
        if (!Guid.TryParse(idText, out var id))
        {
            Current = null;
            return;
        }

        Current = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<AppUser> RegisterAsync(
        string firstName,
        string lastName,
        string email,
        string username,
        string password,
        LocalOrgMembership? membership = null,
        CancellationToken ct = default)
    {
        username = NormalizeUsername(username);
        ValidateRegister(firstName, lastName, email, username, password);

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.AppUsers.AnyAsync(u => u.Username == username, ct))
        {
            throw new InvalidOperationException("Bu kullanıcı adı alınmış.");
        }

        var user = CreateUser(username, firstName, lastName, email, password);
        ApplyMembership(user, membership);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync(ct);
        Current = user;
        await _settings.SetAsync(SettingKeys.CurrentUserId, user.Id.ToString("N"));
        return user;
    }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        username = NormalizeUsername(username);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null)
        {
            return false;
        }

        if (user.HasPassword)
        {
            if (string.IsNullOrEmpty(password) || !Verify(user, password))
            {
                return false;
            }
        }

        Current = user;
        await _settings.SetAsync(SettingKeys.CurrentUserId, user.Id.ToString("N"));
        return true;
    }

    public bool UsesWork
    {
        get
        {
            var kind = Current?.UsageKind ?? "";
            return kind.Equals("work", StringComparison.OrdinalIgnoreCase)
                   || kind.Equals("both", StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task ApplyMembershipAsync(LocalOrgMembership membership, CancellationToken ct = default)
    {
        if (Current is null)
        {
            return;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == Current.Id, ct);
        if (row is null)
        {
            return;
        }

        ApplyMembership(row, membership);
        await db.SaveChangesAsync(ct);
        Current = await db.AppUsers.AsNoTracking().FirstAsync(u => u.Id == row.Id, ct);
    }

    public async Task<AppUser> AddAsync(string username, string displayName, string? password, CancellationToken ct = default)
    {
        username = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Kullanıcı adı gerekli.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.AppUsers.AnyAsync(u => u.Username == username, ct))
        {
            throw new InvalidOperationException("Bu kullanıcı adı alınmış.");
        }

        var parts = (displayName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.ElementAtOrDefault(0) ?? username;
        var last = parts.ElementAtOrDefault(1) ?? "";
        var user = CreateUser(username, first, last, "", password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<bool> SwitchAsync(Guid id, string? password, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return false;
        }

        if (user.HasPassword)
        {
            if (string.IsNullOrEmpty(password) || !Verify(user, password))
            {
                return false;
            }
        }

        Current = user;
        await _settings.SetAsync(SettingKeys.CurrentUserId, user.Id.ToString("N"));
        return true;
    }

    public async Task SignInUserAsync(AppUser user, CancellationToken ct = default)
    {
        Current = user;
        await _settings.SetAsync(SettingKeys.CurrentUserId, user.Id.ToString("N"));
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        Current = null;
        await _settings.SetBoolAsync(SettingKeys.RememberLogin, false);
        await _settings.SetAsync(SettingKeys.CurrentUserId, "");
    }

    public async Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        username = NormalizeUsername(username);
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public static string NormalizeUsername(string username)
        => (username ?? "").Trim().ToLowerInvariant();

    public static void ValidateRegister(string firstName, string lastName, string email, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new InvalidOperationException("Ad ve soyad gerekli.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length < 5)
        {
            throw new InvalidOperationException("Geçerli bir e-posta girin.");
        }

        if (username.Length < 3 || username.Length > 32 || username.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
        {
            throw new InvalidOperationException("Kullanıcı adı 3–32 karakter, harf / rakam / alt çizgi.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            throw new InvalidOperationException("Şifre en az 4 karakter olmalı.");
        }
    }

    private static AppUser CreateUser(string username, string firstName, string lastName, string email, string? password)
    {
        var salt = EncryptionService.GenerateSalt();
        var hasPassword = !string.IsNullOrWhiteSpace(password);
        var verifier = hasPassword
            ? EncryptionService.CreateVerifier(EncryptionService.DeriveKey(password!, salt))
            : [];
        firstName = (firstName ?? "").Trim();
        lastName = (lastName ?? "").Trim();
        var display = string.Join(" ", new[] { firstName, lastName }.Where(s => s.Length > 0));
        if (display.Length == 0)
        {
            display = username;
        }

        return new AppUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = display,
            FirstName = firstName,
            LastName = lastName,
            Email = (email ?? "").Trim(),
            PasswordSalt = salt,
            PasswordVerifier = verifier,
            HasPassword = hasPassword,
            CreatedAt = DateTime.Now
        };
    }

    private static void ApplyMembership(AppUser user, LocalOrgMembership? membership)
    {
        if (membership is null)
        {
            return;
        }

        user.UsageKind = string.IsNullOrWhiteSpace(membership.UsageKind) ? "personal" : membership.UsageKind;
        user.CompanyId = membership.CompanyId;
        user.UnitId = membership.UnitId;
        user.PositionId = membership.PositionId;
        user.CompanyName = membership.CompanyName;
        user.UnitName = membership.UnitName;
        user.PositionTitle = membership.PositionTitle;
    }

    private static bool Verify(AppUser user, string password)
    {
        var key = EncryptionService.DeriveKey(password, user.PasswordSalt);
        return EncryptionService.Verify(key, user.PasswordVerifier);
    }
}
