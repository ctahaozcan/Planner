namespace Planner.App.Services;

public enum ChatPeerKind
{
    Local,
    Lan,
    Server
}

public sealed class ChatPeer
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Username { get; init; } = "";
    public string? Endpoint { get; init; }
    public ChatPeerKind Kind { get; init; }
    public bool IsOnline { get; set; }
    public bool IsFriend { get; set; }
    public DateTime LastSeen { get; set; }

    public string ListSub => string.IsNullOrWhiteSpace(Username)
        ? StatusText
        : "@" + Username + " · " + StatusText;

    public string StatusText => Kind switch
    {
        ChatPeerKind.Local => IsFriend ? "Bu bilgisayar · arkadaş" : "Bu bilgisayar · arkadaş değil",
        ChatPeerKind.Lan => IsFriend
            ? (IsOnline ? "Yerel ağ · arkadaş" : "Yerel ağ · arkadaş (kapalı)")
            : (IsOnline ? "Yerel ağ · arkadaş değil" : "Yerel ağ (kapalı)"),
        ChatPeerKind.Server => IsFriend
            ? (IsOnline ? "Sunucu · arkadaş" : "Sunucu · arkadaş")
            : (IsOnline ? "Sunucu · arkadaş değil" : "Sunucu"),
        _ => ""
    };
}
