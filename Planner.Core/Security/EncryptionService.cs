using System.Security.Cryptography;

namespace Planner.Core.Security;

public static class EncryptionService
{
    public const int DefaultIterations = 210_000;
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] DeriveKey(string password, byte[] salt, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    public static byte[] CreateVerifier(byte[] key) => SHA256.HashData(key);

    public static bool Verify(byte[] key, byte[] verifier)
        => CryptographicOperations.FixedTimeEquals(CreateVerifier(key), verifier);

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceSize + TagSize, ciphertext.Length);
        return blob;
    }

    public static byte[] Decrypt(byte[] blob, byte[] key)
    {
        if (blob.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Şifreli veri bozuk.");
        }

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var ciphertext = blob.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static void Zero(byte[]? data)
    {
        if (data is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
