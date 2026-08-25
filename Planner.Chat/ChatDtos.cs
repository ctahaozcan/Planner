namespace Planner.Chat;

public sealed class HealthResponse
{
    public string Status { get; set; } = "ok";
    public int Protocol { get; set; } = ChatRoutes.ProtocolVersion;
    public string Name { get; set; } = "Yaver Chat";
}

public sealed class AuthRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Usage { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? PositionId { get; set; }
    public string? InviteCode { get; set; }
}

public sealed class AuthResponse
{
    public string Token { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Usage { get; set; } = AccountUsageKinds.Personal;
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionTitle { get; set; }
    public bool CanAssignWork { get; set; }
}

public sealed class DirectoryUser
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Online { get; set; }
}

public sealed class PresenceSnapshot
{
    public List<DirectoryUser> Users { get; set; } = [];
}

public sealed class ChatMessageDto
{
    public Guid Id { get; set; }
    public string FromUserId { get; set; } = "";
    public string ToUserId { get; set; } = "";
    public string FromName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; }
}

public sealed class MessageListResponse
{
    public List<ChatMessageDto> Items { get; set; } = [];
}

public sealed class ErrorResponse
{
    public string Error { get; set; } = "";
}

public sealed class AckPayload
{
    public Guid MessageId { get; set; }
}

public sealed class HelloPayload
{
    public int Protocol { get; set; } = ChatRoutes.ProtocolVersion;
    public string UserId { get; set; } = "";
}
