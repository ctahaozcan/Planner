namespace Planner.ChatServer;

public sealed class ChatServerPaths
{
    public ChatServerPaths(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(FilesDir);
    }

    public string DataDir { get; }
    public string FilesDir => Path.Combine(DataDir, "files");
    public string HttpsPfx => Path.Combine(DataDir, "https.pfx");
}
