namespace Planner.Core;

public static class AppPaths
{
    public const string AppFolderName = "Yaver";
    public const string LegacyAppFolderName = "Planlayici";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string LegacyRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyAppFolderName);

    public static string DataDirectory { get; } = Path.Combine(Root, "data");

    public static string DatabaseFile { get; } = Path.Combine(DataDirectory, "planner.db");

    public static string AttachmentsDirectory { get; } = Path.Combine(Root, "attachments");

    public static string PortraitsDirectory { get; } = Path.Combine(Root, "portraits");

    public static string DocumentsDirectory { get; } = Path.Combine(Root, "documents");

    public static string BackupsDirectory { get; } = Path.Combine(Root, "backups");

    public static string ChatMediaDirectory { get; } = Path.Combine(Root, "chat-media");

    public static string ConnectionString => $"Data Source={DatabaseFile}";

    public static void EnsureCreated()
    {
        TryMigrateFromLegacy();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(PortraitsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(DocumentsDirectory);
        Directory.CreateDirectory(ChatMediaDirectory);
    }

    /// <summary>
    /// İlk çalıştırmada Yaver klasörü boş/yok ama eski Planlayıcı verisi varsa
    /// planner.db, ekler ve yedekleri kopyalar. Eski klasör silinmez.
    /// </summary>
    internal static void TryMigrateFromLegacy()
    {
        if (!Directory.Exists(LegacyRoot) || HasUserData(Root))
        {
            return;
        }

        if (!HasUserData(LegacyRoot))
        {
            return;
        }

        CopyNamedFolder("data");
        CopyNamedFolder("attachments");
        CopyNamedFolder("backups");
    }

    private static bool HasUserData(string root)
    {
        if (File.Exists(Path.Combine(root, "data", "planner.db")))
        {
            return true;
        }

        return HasAnyFiles(Path.Combine(root, "attachments"))
            || HasAnyFiles(Path.Combine(root, "backups"));
    }

    private static bool HasAnyFiles(string directory)
        => Directory.Exists(directory)
           && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();

    private static void CopyNamedFolder(string relative)
    {
        var source = Path.Combine(LegacyRoot, relative);
        if (!Directory.Exists(source))
        {
            return;
        }

        CopyDirectory(source, Path.Combine(Root, relative));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
