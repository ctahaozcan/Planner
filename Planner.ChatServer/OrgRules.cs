using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public static class OrgRules
{
    public static bool EmailMatchesDomain(string email, string domainField)
    {
        var at = (email ?? "").LastIndexOf('@');
        if (at < 0 || at >= (email ?? "").Length - 1)
        {
            return false;
        }

        var host = (email ?? "")[(at + 1)..].Trim().ToLowerInvariant();
        return SplitDomains(domainField).Contains(host);
    }

    public static IReadOnlyList<string> SplitDomains(string? domainField)
        => (domainField ?? "")
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Contains('.'))
            .Distinct()
            .ToList();

    public static string NormalizeKind(string? kind)
        => (kind ?? "").Trim().ToLowerInvariant() is "team" or "takım" or "takim" ? "team" : "unit";

    public static async Task<bool> IsDirectReportAsync(ChatServerDb db, ServerUser manager, Guid reportUserId, CancellationToken ct = default)
    {
        if (manager.PositionId is null || manager.CompanyId is null)
        {
            return false;
        }

        var report = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == reportUserId, ct);
        if (report is null || report.CompanyId != manager.CompanyId || report.PositionId is null)
        {
            return false;
        }

        var position = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == report.PositionId, ct);
        return position is not null && position.ReportsToPositionId == manager.PositionId;
    }

    public static async Task<List<Guid>> DirectReportIdsAsync(ChatServerDb db, ServerUser manager, CancellationToken ct = default)
    {
        if (manager.PositionId is null || manager.CompanyId is null)
        {
            return [];
        }

        var childPositions = await db.Positions.AsNoTracking()
            .Where(p => p.CompanyId == manager.CompanyId && p.ReportsToPositionId == manager.PositionId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (childPositions.Count == 0)
        {
            return [];
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.CompanyId == manager.CompanyId && u.PositionId != null && childPositions.Contains(u.PositionId.Value))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    public static async Task<HashSet<Guid>> DescendantPositionIdsAsync(ChatServerDb db, Guid companyId, Guid rootPositionId, CancellationToken ct = default)
    {
        var all = await db.Positions.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.Id, p.ReportsToPositionId })
            .ToListAsync(ct);
        var children = all.ToLookup(p => p.ReportsToPositionId);
        var set = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootPositionId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in children[current])
            {
                if (set.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return set;
    }

    public static async Task<bool> WouldCreatePositionCycleAsync(ChatServerDb db, Guid positionId, Guid? reportsTo, CancellationToken ct = default)
    {
        if (reportsTo is null)
        {
            return false;
        }

        if (reportsTo == positionId)
        {
            return true;
        }

        var guard = 0;
        var cursor = reportsTo;
        while (cursor is Guid id && guard++ < 64)
        {
            if (id == positionId)
            {
                return true;
            }

            var row = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            cursor = row?.ReportsToPositionId;
        }

        return false;
    }

    public static async Task<bool> WouldCreateUnitCycleAsync(ChatServerDb db, Guid unitId, Guid? parentId, CancellationToken ct = default)
    {
        if (parentId is null)
        {
            return false;
        }

        if (parentId == unitId)
        {
            return true;
        }

        var guard = 0;
        var cursor = parentId;
        while (cursor is Guid id && guard++ < 64)
        {
            if (id == unitId)
            {
                return true;
            }

            var row = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            cursor = row?.ParentId;
        }

        return false;
    }

    public static string UnitPath(IReadOnlyList<OrgUnit> units, OrgUnit unit)
    {
        var map = units.ToDictionary(u => u.Id);
        var parts = new List<string> { unit.Name };
        var guard = 0;
        var cursor = unit.ParentId;
        while (cursor is Guid id && map.TryGetValue(id, out var parent) && guard++ < 32)
        {
            parts.Add(parent.Name);
            cursor = parent.ParentId;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }

    public static string NewInviteCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < 8; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return $"{new string(chars[..4])}-{new string(chars[4..])}";
    }

    public static string NormalizeInviteCode(string? code)
        => (code ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);

    public static async Task<bool> PositionApprovesLeavesAsync(ChatServerDb db, Guid? positionId, CancellationToken ct = default)
    {
        if (positionId is not Guid id)
        {
            return false;
        }

        return await db.Positions.AsNoTracking().AnyAsync(p => p.Id == id && p.CanApproveLeaves, ct);
    }

    public static async Task<bool> CanDecideLeaveAsync(ChatServerDb db, ServerUser actor, Guid subjectUserId, CancellationToken ct = default)
    {
        if (actor.CompanyId is null || actor.PositionId is null)
        {
            return false;
        }

        if (await IsDirectReportAsync(db, actor, subjectUserId, ct))
        {
            return true;
        }

        if (!await PositionApprovesLeavesAsync(db, actor.PositionId, ct))
        {
            return false;
        }

        var subject = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == subjectUserId, ct);
        return subject is not null && subject.CompanyId == actor.CompanyId;
    }

    public static async Task<List<Guid>> LeaveAudienceIdsAsync(ChatServerDb db, ServerUser actor, CancellationToken ct = default)
    {
        if (actor.CompanyId is null || actor.PositionId is null)
        {
            return [];
        }

        if (await PositionApprovesLeavesAsync(db, actor.PositionId, ct))
        {
            return await db.Users.AsNoTracking()
                .Where(u => u.CompanyId == actor.CompanyId && u.Id != actor.Id)
                .Select(u => u.Id)
                .ToListAsync(ct);
        }

        var descendantPositions = await DescendantPositionIdsAsync(db, actor.CompanyId.Value, actor.PositionId.Value, ct);
        if (descendantPositions.Count == 0)
        {
            return [];
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.CompanyId == actor.CompanyId && u.PositionId != null && descendantPositions.Contains(u.PositionId.Value))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    public static async Task<List<Guid>> ManagerUserIdsAsync(ChatServerDb db, ServerUser subject, CancellationToken ct = default)
    {
        if (subject.CompanyId is null || subject.PositionId is null)
        {
            return [];
        }

        var position = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subject.PositionId, ct);
        var ids = new HashSet<Guid>();
        if (position?.ReportsToPositionId is Guid bossId)
        {
            foreach (var id in await db.Users.AsNoTracking()
                         .Where(u => u.CompanyId == subject.CompanyId && u.PositionId == bossId)
                         .Select(u => u.Id)
                         .ToListAsync(ct))
            {
                ids.Add(id);
            }
        }

        var approverPositions = await db.Positions.AsNoTracking()
            .Where(p => p.CompanyId == subject.CompanyId && p.CanApproveLeaves)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (approverPositions.Count > 0)
        {
            foreach (var id in await db.Users.AsNoTracking()
                         .Where(u => u.CompanyId == subject.CompanyId && u.PositionId != null && approverPositions.Contains(u.PositionId.Value))
                         .Select(u => u.Id)
                         .ToListAsync(ct))
            {
                ids.Add(id);
            }
        }

        ids.Remove(subject.Id);
        return ids.ToList();
    }

    public static async Task<bool> CanAccessWorkTaskAsync(ChatServerDb db, ServerUser user, OrgWorkTask task, CancellationToken ct = default)
    {
        if (user.CompanyId != task.CompanyId)
        {
            return false;
        }

        if (task.AssignedToUserId == user.Id || task.AssignedByUserId == user.Id)
        {
            return true;
        }

        if (user.PositionId is not Guid pos || user.CompanyId is not Guid company)
        {
            return false;
        }

        var descendants = await DescendantPositionIdsAsync(db, company, pos, ct);
        var occupant = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == task.AssignedToUserId, ct);
        return occupant?.PositionId is Guid occupantPos && descendants.Contains(occupantPos);
    }

    public static string TodayLeaveLabel(IReadOnlyList<OrgLeave> leaves, DateOnly today)
    {
        var covering = leaves.Where(l => Covers(l, today)).ToList();
        if (covering.Any(l => l.Status is "approved"))
        {
            return "İzinde";
        }

        if (covering.Any(l => l.Status == "pending"))
        {
            return "Onay bekliyor";
        }

        return "İş yerinde";
    }

    public static string NextLeaveLabel(IReadOnlyList<OrgLeave> leaves, DateOnly today)
    {
        var next = leaves
            .Where(l => l.Status is "pending" or "approved" && DateOnly.TryParse(l.StartDate, out var start) && start >= today)
            .OrderBy(l => l.StartDate)
            .FirstOrDefault();
        if (next is null)
        {
            return "—";
        }

        var tag = next.Status == "pending" ? "talep" : "onaylı";
        return next.StartDate + " · " + tag;
    }

    private static bool Covers(OrgLeave leave, DateOnly today)
    {
        if (leave.Status is "rejected")
        {
            return false;
        }

        return DateOnly.TryParse(leave.StartDate, out var start)
               && DateOnly.TryParse(leave.EndDate, out var end)
               && start <= today && today <= end;
    }
}
