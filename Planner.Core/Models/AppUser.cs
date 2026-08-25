namespace Planner.Core.Models;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordVerifier { get; set; } = [];
    public bool HasPassword { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string UsageKind { get; set; } = "personal";
    public Guid? CompanyId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? PositionId { get; set; }
    public string? CompanyName { get; set; }
    public string? UnitName { get; set; }
    public string? PositionTitle { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class LocalOrgMembership
{
    public string UsageKind { get; init; } = "personal";
    public Guid? CompanyId { get; init; }
    public Guid? UnitId { get; init; }
    public Guid? PositionId { get; init; }
    public string? CompanyName { get; init; }
    public string? UnitName { get; init; }
    public string? PositionTitle { get; init; }
}

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public string ConversationKey { get; set; } = "";
    public string FromKey { get; set; } = "";
    public string ToKey { get; set; } = "";
    public string FromName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; }
    public bool IsOutgoing { get; set; }
    public DateTime? EditedAt { get; set; }
    public string Thumbs { get; set; } = "";
}
