using Planner.Core.Security;

namespace Planner.Core.Services;

public static class PortraitStore
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static string OriginalName(Guid personId) => $"{personId:N}.bin";

    public static string ThumbName(Guid personId) => $"{personId:N}.thumb.bin";

    public static void EnsureDirectory()
        => Directory.CreateDirectory(AppPaths.PortraitsDirectory);

    public static void WriteEncrypted(string relativeName, byte[] plain, byte[] key)
    {
        EnsureDirectory();
        var path = FullPath(relativeName);
        var blob = EncryptionService.Encrypt(plain, key);
        File.WriteAllBytes(path, blob);
    }

    public static byte[]? ReadDecrypted(string relativeName, byte[] key)
    {
        var path = FullPath(relativeName);
        if (!File.Exists(path))
        {
            return null;
        }

        var blob = File.ReadAllBytes(path);
        return EncryptionService.Decrypt(blob, key);
    }

    public static void Delete(string relativeName)
    {
        var path = FullPath(relativeName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void DeleteForPerson(Guid personId)
    {
        Delete(OriginalName(personId));
        Delete(ThumbName(personId));
    }

    public static void DeleteAll()
    {
        if (!Directory.Exists(AppPaths.PortraitsDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(AppPaths.PortraitsDirectory))
        {
            File.Delete(file);
        }
    }

    public static IReadOnlyList<(string Name, byte[] Blob)> ReadAllEncrypted()
    {
        if (!Directory.Exists(AppPaths.PortraitsDirectory))
        {
            return [];
        }

        return Directory.GetFiles(AppPaths.PortraitsDirectory)
            .Select(f => (Path.GetFileName(f), File.ReadAllBytes(f)))
            .ToList();
    }

    public static void WriteEncryptedRaw(string fileName, byte[] blob)
    {
        EnsureDirectory();
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            return;
        }

        File.WriteAllBytes(FullPath(safe), blob);
    }

    public static void ReencryptAll(byte[] oldKey, byte[] newKey)
    {
        if (!Directory.Exists(AppPaths.PortraitsDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(AppPaths.PortraitsDirectory, "*.bin"))
        {
            var blob = File.ReadAllBytes(file);
            byte[] plain;
            try
            {
                plain = EncryptionService.Decrypt(blob, oldKey);
            }
            catch
            {
                continue;
            }

            File.WriteAllBytes(file, EncryptionService.Encrypt(plain, newKey));
            EncryptionService.Zero(plain);
        }
    }

    private static string FullPath(string relativeName)
        => Path.Combine(AppPaths.PortraitsDirectory, Path.GetFileName(relativeName));
}
