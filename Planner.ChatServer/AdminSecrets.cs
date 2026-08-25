using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public static class AdminSecrets
{
    public const string WellKnownPassword = "YaverAdmin";
    public const int MinPasswordLength = 12;
    private const string SaltKey = "AdminPasswordSalt";
    private const string VerifierKey = "AdminPasswordVerifier";

    public static bool IsWellKnown(string? password)
        => string.Equals((password ?? "").Trim(), WellKnownPassword, StringComparison.Ordinal);

    public static async Task EnsureAsync(ChatServerDb db, IConfiguration config)
    {
        if (await db.Settings.AnyAsync(s => s.Key == SaltKey))
        {
            return;
        }

        var fromConfig = config["Admin:Password"] ?? "";
        if (string.IsNullOrWhiteSpace(fromConfig))
        {
            fromConfig = WellKnownPassword;
        }

        await StoreAsync(db, fromConfig);
    }

    public static async Task<bool> VerifyAsync(ChatServerDb db, string password)
    {
        var salt = await GetBytesAsync(db, SaltKey);
        var verifier = await GetBytesAsync(db, VerifierKey);
        if (salt.Length == 0 || verifier.Length == 0)
        {
            return false;
        }

        return ChatPassword.Verify(password ?? "", salt, verifier);
    }

    public static async Task<bool> IsStoredWellKnownAsync(ChatServerDb db)
    {
        var salt = await GetBytesAsync(db, SaltKey);
        var verifier = await GetBytesAsync(db, VerifierKey);
        return salt.Length > 0 && ChatPassword.Verify(WellKnownPassword, salt, verifier);
    }

    public static async Task StoreAsync(ChatServerDb db, string password)
    {
        var (salt, verifier) = ChatPassword.Hash(password);
        await UpsertAsync(db, SaltKey, Convert.ToBase64String(salt));
        await UpsertAsync(db, VerifierKey, Convert.ToBase64String(verifier));
        await db.SaveChangesAsync();
    }

    private static async Task UpsertAsync(ChatServerDb db, string key, string value)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            db.Settings.Add(new OrgSetting { Key = key, Value = value });
            return;
        }

        row.Value = value;
    }

    private static async Task<byte[]> GetBytesAsync(ChatServerDb db, string key)
    {
        var value = await db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    public static void ValidateNew(string next)
    {
        next = (next ?? "").Trim();
        if (next.Length < MinPasswordLength)
        {
            throw new InvalidOperationException($"Yeni şifre en az {MinPasswordLength} karakter olmalı.");
        }

        if (IsWellKnown(next))
        {
            throw new InvalidOperationException("Varsayılan şifre kullanılamaz. Daha güçlü bir şifre seçin.");
        }

        if (next.Distinct().Count() < 4)
        {
            throw new InvalidOperationException("Şifre yeterince çeşitli değil.");
        }
    }
}
