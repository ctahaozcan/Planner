using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class FriendshipService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly UserAccountService _users;

    public FriendshipService(IDbContextFactory<PlannerDbContext> factory, UserAccountService users)
    {
        _factory = factory;
        _users = users;
    }

    public async Task<IReadOnlyList<Friendship>> ListMineAsync(CancellationToken ct = default)
    {
        var me = _users.Current?.Id;
        if (me is null)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Friendships.AsNoTracking()
            .Where(f => f.RequesterId == me || f.AddresseeId == me)
            .OrderByDescending(f => f.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> AcceptedFriendIdsAsync(CancellationToken ct = default)
    {
        var rows = await ListMineAsync(ct);
        var me = _users.Current?.Id;
        if (me is null)
        {
            return [];
        }

        return rows.Where(f => f.Status == FriendshipStatus.Accepted)
            .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
            .Distinct()
            .ToList();
    }

    public async Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken ct = default)
    {
        if (a == b)
        {
            return false;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Friendships.AsNoTracking().AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a)), ct);
    }

    public async Task<bool> AreFriendsByKeyAsync(string a, string b, CancellationToken ct = default)
    {
        if (!TryKey(a, out var ga) || !TryKey(b, out var gb))
        {
            return false;
        }

        return await AreFriendsAsync(ga, gb, ct);
    }

    public async Task<FriendshipStatus?> GetStatusByKeyAsync(string a, string b, CancellationToken ct = default)
    {
        if (!TryKey(a, out var ga) || !TryKey(b, out var gb) || ga == gb)
        {
            return null;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.AsNoTracking().FirstOrDefaultAsync(f =>
            (f.RequesterId == ga && f.AddresseeId == gb) || (f.RequesterId == gb && f.AddresseeId == ga), ct);
        return row?.Status;
    }

    public static bool TryKey(string? text, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Guid.TryParse(text, out id) || Guid.TryParseExact(text, "N", out id);
    }

    public async Task RequestByKeyAsync(string key, CancellationToken ct = default)
    {
        if (!TryKey(key, out var id))
        {
            throw new InvalidOperationException("Bu kişiye arkadaşlık isteği gönderilemedi.");
        }

        await RequestAsync(id, ct);
    }

    public async Task<bool> IncomingRequestAsync(Guid requesterId, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        if (me == requesterId)
        {
            return false;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == requesterId && f.AddresseeId == me) ||
            (f.RequesterId == me && f.AddresseeId == requesterId), ct);
        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
            {
                return false;
            }

            existing.Status = FriendshipStatus.Pending;
            existing.RequesterId = requesterId;
            existing.AddresseeId = me;
            existing.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync(ct);
            return true;
        }

        db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            AddresseeId = me,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> ListPendingIncomingAsync(CancellationToken ct = default)
    {
        var me = _users.Current?.Id;
        if (me is null)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Friendships.AsNoTracking()
            .Where(f => f.AddresseeId == me && f.Status == FriendshipStatus.Pending)
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => f.RequesterId)
            .ToListAsync(ct);
    }

    public async Task AcceptFromPeerAsync(Guid peerId, bool canViewAgenda, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == peerId && f.AddresseeId == me) ||
            (f.RequesterId == me && f.AddresseeId == peerId), ct);
        if (row is null)
        {
            row = new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = peerId,
                AddresseeId = me,
                CreatedAt = DateTime.Now
            };
            db.Friendships.Add(row);
        }

        row.Status = FriendshipStatus.Accepted;
        row.CanViewAgenda = canViewAgenda || row.CanViewAgenda;
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task AcceptIncomingByPeerAsync(Guid peerId, bool canViewAgenda, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f =>
            f.AddresseeId == me && f.RequesterId == peerId, ct)
                  ?? throw new InvalidOperationException("İstek bulunamadı.");
        row.Status = FriendshipStatus.Accepted;
        row.CanViewAgenda = canViewAgenda;
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> CanViewAgendaAsync(Guid ownerId, Guid viewerId, CancellationToken ct = default)
        => await GetClassAsync(ownerId, viewerId, ct) == FriendClassKind.Work;

    public async Task<FriendClassKind> GetClassAsync(Guid ownerId, Guid viewerId, CancellationToken ct = default)
    {
        if (ownerId == viewerId)
        {
            return FriendClassKind.Personal;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.AsNoTracking().FirstOrDefaultAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == ownerId && f.AddresseeId == viewerId) ||
             (f.RequesterId == viewerId && f.AddresseeId == ownerId)), ct);
        if (row is null)
        {
            return FriendClassKind.Personal;
        }

        return row.RequesterId == ownerId ? row.RequesterKind : row.AddresseeKind;
    }

    public async Task<FriendClassKind> GetClassByKeyAsync(string ownerKey, string viewerKey, CancellationToken ct = default)
    {
        if (!TryKey(ownerKey, out var owner) || !TryKey(viewerKey, out var viewer))
        {
            return FriendClassKind.Personal;
        }

        return await GetClassAsync(owner, viewer, ct);
    }

    public async Task SetClassByPeerAsync(Guid peerId, FriendClassKind kind, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == me && f.AddresseeId == peerId) ||
             (f.RequesterId == peerId && f.AddresseeId == me)), ct)
                  ?? throw new InvalidOperationException("Arkadaşlık yok.");
        if (row.RequesterId == me)
        {
            row.RequesterKind = kind;
        }
        else
        {
            row.AddresseeKind = kind;
        }

        row.CanViewAgenda = kind == FriendClassKind.Work;
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Friendship> RequestAsync(Guid addresseeId, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        if (me == addresseeId)
        {
            throw new InvalidOperationException("Kendinizi ekleyemezsiniz.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == me && f.AddresseeId == addresseeId) ||
            (f.RequesterId == addresseeId && f.AddresseeId == me), ct);
        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
            {
                return existing;
            }

            if (existing.AddresseeId == me && existing.Status == FriendshipStatus.Pending)
            {
                existing.Status = FriendshipStatus.Accepted;
                existing.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
                return existing;
            }

            existing.Status = FriendshipStatus.Pending;
            existing.RequesterId = me;
            existing.AddresseeId = addresseeId;
            existing.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var row = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = me,
            AddresseeId = addresseeId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        db.Friendships.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task AcceptAsync(Guid friendshipId, bool canViewAgenda, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct)
                  ?? throw new InvalidOperationException("İstek bulunamadı.");
        if (row.AddresseeId != me)
        {
            throw new InvalidOperationException("Bu isteği yalnızca alıcı kabul edebilir.");
        }

        row.Status = FriendshipStatus.Accepted;
        row.CanViewAgenda = canViewAgenda;
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAgendaPermissionAsync(Guid friendUserId, bool allow, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == me && f.AddresseeId == friendUserId) ||
             (f.RequesterId == friendUserId && f.AddresseeId == me)), ct)
                  ?? throw new InvalidOperationException("Arkadaşlık yok.");
        row.CanViewAgenda = allow;
        if (row.RequesterId == me)
        {
            row.RequesterKind = allow ? FriendClassKind.Work : FriendClassKind.Personal;
        }
        else
        {
            row.AddresseeKind = allow ? FriendClassKind.Work : FriendClassKind.Personal;
        }

        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeclineOrRemoveAsync(Guid friendshipId, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);
        if (row is null || (row.RequesterId != me && row.AddresseeId != me))
        {
            return;
        }

        db.Friendships.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeclineFromPeerAsync(Guid peerId, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == peerId && f.AddresseeId == me) ||
            (f.RequesterId == me && f.AddresseeId == peerId), ct);
        if (row is null)
        {
            return;
        }

        db.Friendships.Remove(row);
        await db.SaveChangesAsync(ct);
    }

}
