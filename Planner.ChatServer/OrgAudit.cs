using Planner.Chat;

namespace Planner.ChatServer;

public static class OrgAudit
{
    public const string TaskAssign = "task.assign";
    public const string TaskDistribute = "task.distribute";
    public const string LeaveSubmit = "leave.submit";
    public const string LeaveApprove = "leave.approve";
    public const string LeaveReject = "leave.reject";
    public const string InviteUse = "invite.use";
    public const string FileUpload = "file.upload";

    public static void Add(
        ChatServerDb db,
        Guid companyId,
        ServerUser actor,
        string action,
        ServerUser? target,
        string detail)
    {
        db.AuditLog.Add(new OrgAuditEvent
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            At = DateTime.UtcNow,
            ActorUserId = actor.Id,
            ActorName = actor.DisplayName,
            Action = action,
            TargetUserId = target?.Id,
            TargetName = target?.DisplayName,
            Detail = (detail ?? "").Trim()
        });
    }

    public static AuditEventDto ToDto(OrgAuditEvent row) => new()
    {
        Id = row.Id,
        At = row.At,
        ActorName = row.ActorName,
        Action = ActionLabel(row.Action),
        TargetName = row.TargetName,
        Detail = row.Detail
    };

    public static string ActionLabel(string action) => action switch
    {
        TaskAssign => "Görev verdi",
        TaskDistribute => "Görevi dağıttı",
        LeaveSubmit => "İzin talep etti",
        LeaveApprove => "İzni onayladı",
        LeaveReject => "İzni reddetti",
        InviteUse => "Daveti kullandı",
        FileUpload => "Kurum dosyası yükledi",
        _ => action
    };
}
