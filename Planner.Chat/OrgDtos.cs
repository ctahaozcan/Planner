namespace Planner.Chat;

public static class AccountUsageKinds
{
    public const string Personal = "personal";
    public const string Work = "work";
    public const string Both = "both";

    public static bool IncludesWork(string? usage)
    {
        var n = Normalize(usage);
        return n is Work or Both;
    }

    public static bool IncludesPersonal(string? usage)
    {
        var n = Normalize(usage);
        return n is Personal or Both;
    }

    public static string Normalize(string? usage)
        => (usage ?? "").Trim().ToLowerInvariant() switch
        {
            "work" or "iş" or "is" or "job" => Work,
            "both" or "ikisi" or "ikisi birlikte" or "all" => Both,
            _ => Personal
        };

    public static string ToLabel(string? usage)
        => Normalize(usage) switch
        {
            Work => "İş",
            Both => "İş + özel",
            _ => "Özel"
        };
}

public sealed class CompanyListResponse
{
    public List<CompanyOptionDto> Items { get; set; } = [];
}

public sealed class CompanyOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
}

public sealed class OrgCatalogResponse
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public string Domain { get; set; } = "";
    public List<OrgUnitDto> Units { get; set; } = [];
    public List<OrgPositionDto> Positions { get; set; } = [];
}

public sealed class OrgUnitDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "unit";
    public int SortOrder { get; set; }
    public string Path { get; set; } = "";
}

public sealed class OrgPositionDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid? ReportsToPositionId { get; set; }
    public string? ReportsToTitle { get; set; }
    public int SortOrder { get; set; }
    public bool Occupied { get; set; }
    public string? OccupantName { get; set; }
    public bool CanApproveLeaves { get; set; }
}

public sealed class OrgPersonDto
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionTitle { get; set; }
    public bool DirectReport { get; set; }
}

public sealed class OrgTeamResponse
{
    public OrgPersonDto? Me { get; set; }
    public List<OrgPersonDto> DirectReports { get; set; } = [];
    public List<OrgPersonDto> Subtree { get; set; } = [];
    public List<WorkTaskDto> Tasks { get; set; } = [];
}

public sealed class WorkTaskDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public string Date { get; set; } = "";
    public string? Time { get; set; }
    public string AssignedByUserId { get; set; } = "";
    public string AssignedByName { get; set; } = "";
    public string AssignedToUserId { get; set; } = "";
    public string AssignedToName { get; set; } = "";
    public Guid? ParentTaskId { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<WorkFileDto> Files { get; set; } = [];
}

public sealed class WorkFileDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
}

public sealed class WorkTaskCreateRequest
{
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public string Date { get; set; } = "";
    public string? Time { get; set; }
    public Guid? ToUserId { get; set; }
    public Guid? ToUnitId { get; set; }
}

public sealed class WorkTaskDistributeRequest
{
    public Guid TaskId { get; set; }
    public Guid ToUserId { get; set; }
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
}

public sealed class AdminLoginRequest
{
    public string Password { get; set; } = "";
}

public sealed class AdminLoginResponse
{
    public string Token { get; set; } = "";
    public bool WeakPassword { get; set; }
    public bool UsingHttps { get; set; }
    public int HttpsPort { get; set; } = ChatRoutes.DefaultHttpsPort;
}

public sealed class AdminCompanySaveRequest
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class AdminUnitSaveRequest
{
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "unit";
    public int SortOrder { get; set; }
}

public sealed class AdminPositionSaveRequest
{
    public Guid CompanyId { get; set; }
    public Guid UnitId { get; set; }
    public string Title { get; set; } = "";
    public Guid? ReportsToPositionId { get; set; }
    public int SortOrder { get; set; }
    public bool CanApproveLeaves { get; set; }
}

public sealed class AdminMemberSaveRequest
{
    public Guid? UnitId { get; set; }
    public Guid? PositionId { get; set; }
}

public sealed class InvitePreviewDto
{
    public string Code { get; set; } = "";
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public string Domain { get; set; } = "";
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public Guid PositionId { get; set; }
    public string PositionTitle { get; set; } = "";
    public string? Email { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public sealed class AdminInviteCreateRequest
{
    public Guid CompanyId { get; set; }
    public Guid PositionId { get; set; }
    public string? Email { get; set; }
    public int Days { get; set; } = 14;
}

public sealed class AdminInviteDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public Guid PositionId { get; set; }
    public string PositionTitle { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string? Email { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedByName { get; set; }
}

public sealed class OrgLeaveDto
{
    public Guid Id { get; set; }
    public Guid? ClientId { get; set; }
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public int EntryKind { get; set; }
    public int DurationKind { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public int StartHalf { get; set; }
    public int EndHalf { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "pending";
    public string? DecidedByName { get; set; }
    public int DurationMinutes { get; set; }
}

public sealed class OrgLeaveCreateRequest
{
    public Guid? ClientId { get; set; }
    public string TypeName { get; set; } = "";
    public int EntryKind { get; set; }
    public int DurationKind { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public int StartHalf { get; set; }
    public int EndHalf { get; set; }
    public string? Note { get; set; }
    public int DurationMinutes { get; set; }
}

public sealed class OrgLeaveDecideRequest
{
    public Guid Id { get; set; }
    public bool Approve { get; set; }
    public string? Note { get; set; }
}

public sealed class OrgLeavePersonRow
{
    public OrgPersonDto Person { get; set; } = new();
    public string TodayStatus { get; set; } = "İş yerinde";
    public int PendingCount { get; set; }
    public string NextLeave { get; set; } = "—";
    public List<OrgLeaveDto> Leaves { get; set; } = [];
}

public sealed class OrgLeaveBoardResponse
{
    public bool CanManage { get; set; }
    public List<OrgLeaveDto> Inbox { get; set; } = [];
    public List<OrgLeavePersonRow> People { get; set; } = [];
    public List<OrgLeaveDto> Mine { get; set; } = [];
}

public sealed class AuditEventDto
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? TargetName { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class AdminPasswordChangeRequest
{
    public string Current { get; set; } = "";
    public string Next { get; set; } = "";
}
