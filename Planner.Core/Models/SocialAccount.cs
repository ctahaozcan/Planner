namespace Planner.Core.Models;

public static class SocialPlatforms
{
    public static readonly string[] All =
    [
        "Instagram",
        "X / Twitter",
        "LinkedIn",
        "Facebook",
        "WhatsApp",
        "Telegram",
        "Diğer"
    ];
}

public sealed class SocialAccount
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = "Diğer";
    public string Value { get; set; } = "";
}

public sealed class EncryptedSocialAccount
{
    public Guid Id { get; set; }
    public Guid ContactId { get; set; }
    public byte[] Payload { get; set; } = [];
}
