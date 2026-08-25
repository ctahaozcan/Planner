namespace Planner.Chat;

/// <summary>
/// Sürüm 1 sözleşmesi. İstemci ve sunucu aynı yolları kullanır.
/// İleride HTTPS reverse proxy arkasında da path'ler değişmez.
/// </summary>
public static class ChatRoutes
{
    public const int ProtocolVersion = 1;
    public const int DefaultPort = 47880;
    public const int DefaultHttpsPort = 47883;
    public const string DefaultListen = "http://0.0.0.0:47880";
    public const string DefaultClientUrl = "http://127.0.0.1:47880";

    public const string Health = "/v1/health";
    public const string Register = "/v1/auth/register";
    public const string Login = "/v1/auth/login";
    public const string Users = "/v1/users";
    public const string UsersSearch = "/v1/users/search";
    public const string Messages = "/v1/messages";
    public const string WebSocket = "/v1/ws";

    public const string OrgCompanies = "/v1/org/companies";
    public const string OrgCatalog = "/v1/org/catalog";
    public const string OrgMe = "/v1/org/me";
    public const string OrgReports = "/v1/org/reports";
    public const string OrgTeam = "/v1/org/team";
    public const string OrgWorkTasks = "/v1/org/work-tasks";
    public const string OrgDistribute = "/v1/org/work-tasks/distribute";
    public const string OrgInvite = "/v1/org/invite";
    public const string OrgLeaves = "/v1/org/leaves";
    public const string OrgLeaveDecide = "/v1/org/leaves/decide";
    public const string OrgAudit = "/v1/org/audit";
    public const string OrgWorkFiles = "/v1/org/work-tasks/files";
    public const string OrgFile = "/v1/org/files";

    public const string AdminLogin = "/v1/admin/login";
    public const string AdminCompanies = "/v1/admin/companies";
    public const string AdminUnits = "/v1/admin/units";
    public const string AdminPositions = "/v1/admin/positions";
    public const string AdminMembers = "/v1/admin/members";
    public const string AdminInvites = "/v1/admin/invites";
    public const string AdminPassword = "/v1/admin/password";
    public const string AdminAudit = "/v1/admin/audit";

    public const int MaxBodyChars = 700000;
    public const int MaxFrameBytes = 900 * 1024;
    public const int MaxFileBytes = 400 * 1024;
    public const int MaxOrgFileBytes = 20 * 1024 * 1024;
}

public static class ChatTypes
{
    public const string Hello = "hello";
    public const string Presence = "presence";
    public const string Message = "msg";
    public const string Ack = "ack";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
    public const string WorkTask = "worktask";
    public const string Leave = "leave";
}
