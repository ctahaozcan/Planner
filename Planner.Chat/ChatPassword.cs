using System.Security.Cryptography;

namespace Planner.Chat;

public static class ChatPassword
{
    public const int Iterations = 210_000;
    public const int SaltSize = 16;
    public const int KeySize = 32;

    public static (byte[] Salt, byte[] Verifier) Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (salt, SHA256.HashData(key));
    }

    public static bool Verify(string password, byte[] salt, byte[] verifier)
    {
        if (string.IsNullOrEmpty(password) || salt.Length == 0 || verifier.Length == 0)
        {
            return false;
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(key), verifier);
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string NormalizeUsername(string username)
        => (username ?? "").Trim().ToLowerInvariant();
}
