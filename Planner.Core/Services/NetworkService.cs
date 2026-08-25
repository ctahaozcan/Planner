using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class NetworkService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public NetworkService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<MeProfile> GetMeAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MeProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is not null)
        {
            return row;
        }

        var created = new MeProfile
        {
            Id = NetworkIds.Me,
            Name = "Ben",
            UpdatedAt = DateTime.Now
        };
        db.MeProfiles.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task SaveMeAsync(string name, string? notes, string? photoFileName = null, bool updatePhoto = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MeProfiles.FirstOrDefaultAsync(ct);
        var trimmedName = string.IsNullOrWhiteSpace(name) ? "Ben" : name.Trim();
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (row is null)
        {
            db.MeProfiles.Add(new MeProfile
            {
                Id = NetworkIds.Me,
                Name = trimmedName,
                Notes = trimmedNotes,
                PhotoFileName = updatePhoto ? photoFileName : null,
                UpdatedAt = DateTime.Now
            });
        }
        else
        {
            row.Name = trimmedName;
            row.Notes = trimmedNotes;
            if (updatePhoto)
            {
                row.PhotoFileName = photoFileName;
            }

            row.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Segment>> GetSegmentsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Segments.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<Segment> AddSegmentAsync(Segment segment, CancellationToken ct = default)
    {
        segment.Id = segment.Id == Guid.Empty ? Guid.NewGuid() : segment.Id;
        if (segment.CreatedAt == default)
        {
            segment.CreatedAt = DateTime.Now;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (segment.SortOrder == 0)
        {
            segment.SortOrder = (await db.Segments.MaxAsync(s => (int?)s.SortOrder, ct) ?? 0) + 1;
        }

        db.Segments.Add(segment);
        if (segment.Kind == SegmentKind.Kurum)
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(),
                SegmentId = segment.Id,
                Name = segment.Name,
                UpdatedAt = DateTime.Now
            });
        }

        await db.SaveChangesAsync(ct);
        return segment;
    }

    public async Task UpdateSegmentAsync(Segment segment, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Segments.FirstOrDefaultAsync(s => s.Id == segment.Id, ct)
                  ?? throw new InvalidOperationException("Segment bulunamadı.");
        row.Name = segment.Name.Trim();
        row.Kind = segment.Kind;
        row.ColorHex = segment.ColorHex;
        row.SortOrder = segment.SortOrder;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSegmentAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
        {
            return;
        }

        db.PersonSegments.RemoveRange(await db.PersonSegments.Where(p => p.SegmentId == id).ToListAsync(ct));
        var orgs = await db.Organizations.Where(o => o.SegmentId == id).ToListAsync(ct);
        var orgIds = orgs.Select(o => o.Id).ToHashSet();
        db.PersonOrganizations.RemoveRange(
            await db.PersonOrganizations.Where(p => orgIds.Contains(p.OrganizationId)).ToListAsync(ct));
        db.Organizations.RemoveRange(orgs);
        db.Segments.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);
    }

    public async Task<Organization> AddOrganizationAsync(Organization org, CancellationToken ct = default)
    {
        org.Id = org.Id == Guid.Empty ? Guid.NewGuid() : org.Id;
        org.UpdatedAt = DateTime.Now;
        org.Name = org.Name.Trim();
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);
        return org;
    }

    public async Task UpdateOrganizationAsync(Organization org, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Organizations.FirstOrDefaultAsync(o => o.Id == org.Id, ct)
                  ?? throw new InvalidOperationException("Kurum bulunamadı.");
        row.Name = org.Name.Trim();
        row.Role = EmptyToNull(org.Role);
        row.Phone = EmptyToNull(org.Phone);
        row.Address = EmptyToNull(org.Address);
        row.Notes = EmptyToNull(org.Notes);
        row.SegmentId = org.SegmentId;
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteOrganizationAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.PersonOrganizations.RemoveRange(
            await db.PersonOrganizations.Where(p => p.OrganizationId == id).ToListAsync(ct));
        var row = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (row is not null)
        {
            db.Organizations.Remove(row);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PersonRelationship>> GetRelationshipsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Relationships.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync(ct);
    }

    public async Task<PersonRelationship> AddRelationshipAsync(PersonRelationship rel, CancellationToken ct = default)
    {
        if (rel.FromPersonId == rel.ToPersonId)
        {
            throw new InvalidOperationException("Bir kişi kendisiyle ilişkilendirilemez.");
        }

        rel.Id = rel.Id == Guid.Empty ? Guid.NewGuid() : rel.Id;
        rel.Label = rel.Label.Trim();
        if (rel.CreatedAt == default)
        {
            rel.CreatedAt = DateTime.Now;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var exists = await db.Relationships.AnyAsync(r =>
            r.FromPersonId == rel.FromPersonId && r.ToPersonId == rel.ToPersonId && r.Label == rel.Label, ct);
        if (exists)
        {
            throw new InvalidOperationException("Bu ilişki zaten var.");
        }

        db.Relationships.Add(rel);
        await db.SaveChangesAsync(ct);
        return rel;
    }

    public async Task UpdateRelationshipAsync(PersonRelationship rel, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Relationships.FirstOrDefaultAsync(r => r.Id == rel.Id, ct)
                  ?? throw new InvalidOperationException("İlişki bulunamadı.");
        row.FromPersonId = rel.FromPersonId;
        row.ToPersonId = rel.ToPersonId;
        row.Label = rel.Label.Trim();
        row.IsDirected = rel.IsDirected;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRelationshipAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Relationships.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return;
        }

        db.Relationships.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetSegmentIdsForPersonAsync(Guid personId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PersonSegments.AsNoTracking()
            .Where(p => p.PersonId == personId)
            .Select(p => p.SegmentId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, List<Guid>>> GetAllSegmentMembershipsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.PersonSegments.AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(r => r.PersonId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SegmentId).ToList());
    }

    public async Task SetPersonSegmentsAsync(Guid personId, IEnumerable<Guid> segmentIds, CancellationToken ct = default)
    {
        var ids = segmentIds.Distinct().ToHashSet();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.PersonSegments.Where(p => p.PersonId == personId).ToListAsync(ct);
        db.PersonSegments.RemoveRange(existing.Where(e => !ids.Contains(e.SegmentId)));
        var have = existing.Select(e => e.SegmentId).ToHashSet();
        foreach (var id in ids.Where(id => !have.Contains(id)))
        {
            db.PersonSegments.Add(new PersonSegment { PersonId = personId, SegmentId = id });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> GetPrimaryOrganizationIdAsync(Guid personId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PersonOrganizations.AsNoTracking()
            .Where(p => p.PersonId == personId && p.IsPrimary)
            .Select(p => (Guid?)p.OrganizationId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryOrganizationsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PersonOrganizations.AsNoTracking()
            .Where(p => p.IsPrimary)
            .ToDictionaryAsync(p => p.PersonId, p => p.OrganizationId, ct);
    }

    public async Task<IReadOnlyList<PersonOrganization>> GetOrgMembershipsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PersonOrganizations.AsNoTracking().ToListAsync(ct);
    }

    public async Task SetPrimaryOrganizationAsync(Guid personId, Guid? organizationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.PersonOrganizations.Where(p => p.PersonId == personId).ToListAsync(ct);
        string? title = null;
        Guid? manager = null;
        if (organizationId is { } keepId)
        {
            var same = existing.FirstOrDefault(e => e.OrganizationId == keepId);
            title = same?.Title;
            manager = same?.ManagerPersonId;
        }

        db.PersonOrganizations.RemoveRange(existing);
        if (organizationId is { } orgId)
        {
            db.PersonOrganizations.Add(new PersonOrganization
            {
                PersonId = personId,
                OrganizationId = orgId,
                IsPrimary = true,
                Title = title,
                ManagerPersonId = manager
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetOrgPlacementAsync(
        Guid personId,
        Guid? organizationId,
        string? title,
        Guid? managerPersonId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.PersonOrganizations.Where(p => p.PersonId == personId).ToListAsync(ct);
        db.PersonOrganizations.RemoveRange(existing);
        if (organizationId is { } orgId)
        {
            Guid? manager = managerPersonId is { } mid && mid != Guid.Empty && mid != personId ? mid : null;
            if (manager is { } boss && await WouldCreateCycleAsync(db, orgId, personId, boss, ct))
            {
                manager = null;
            }

            db.PersonOrganizations.Add(new PersonOrganization
            {
                PersonId = personId,
                OrganizationId = orgId,
                IsPrimary = true,
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                ManagerPersonId = manager
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> WouldCreateCycleAsync(
        PlannerDbContext db,
        Guid organizationId,
        Guid personId,
        Guid managerId,
        CancellationToken ct)
    {
        var rows = await db.PersonOrganizations.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .ToListAsync(ct);
        var byPerson = rows.ToDictionary(r => r.PersonId, r => r.ManagerPersonId);
        var cursor = managerId;
        var guard = 0;
        while (cursor != Guid.Empty && guard++ < 64)
        {
            if (cursor == personId)
            {
                return true;
            }

            if (!byPerson.TryGetValue(cursor, out var next) || next is null)
            {
                break;
            }

            cursor = next.Value;
        }

        return false;
    }

    public async Task ConnectSegmentMembersAsync(
        Guid segmentId,
        IReadOnlyList<Guid> memberIds,
        bool linkToMe,
        bool linkToEachOther,
        string? labelToMe,
        string? labelBetween,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existingRows = await db.PersonSegments.Where(p => p.SegmentId == segmentId).ToListAsync(ct);
        db.PersonSegments.RemoveRange(existingRows);
        foreach (var id in memberIds.Distinct())
        {
            db.PersonSegments.Add(new PersonSegment { PersonId = id, SegmentId = segmentId });
        }

        if (linkToMe)
        {
            var label = string.IsNullOrWhiteSpace(labelToMe) ? "bağlantı" : labelToMe.Trim();
            foreach (var id in memberIds)
            {
                if (id == NetworkIds.Me) continue;
                var exists = await db.Relationships.AnyAsync(r =>
                    r.FromPersonId == NetworkIds.Me && r.ToPersonId == id, ct);
                if (!exists)
                {
                    db.Relationships.Add(new PersonRelationship
                    {
                        Id = Guid.NewGuid(),
                        FromPersonId = NetworkIds.Me,
                        ToPersonId = id,
                        Label = label,
                        IsDirected = true,
                        CreatedAt = DateTime.Now
                    });
                }
            }
        }

        if (linkToEachOther && memberIds.Count > 1)
        {
            var label = string.IsNullOrWhiteSpace(labelBetween) ? "bağlantı" : labelBetween.Trim();
            for (var i = 0; i < memberIds.Count; i++)
            {
                for (var j = i + 1; j < memberIds.Count; j++)
                {
                    var a = memberIds[i];
                    var b = memberIds[j];
                    var exists = await db.Relationships.AnyAsync(r =>
                        (r.FromPersonId == a && r.ToPersonId == b) ||
                        (r.FromPersonId == b && r.ToPersonId == a), ct);
                    if (!exists)
                    {
                        db.Relationships.Add(new PersonRelationship
                        {
                            Id = Guid.NewGuid(),
                            FromPersonId = a,
                            ToPersonId = b,
                            Label = label,
                            IsDirected = false,
                            CreatedAt = DateTime.Now
                        });
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemovePersonAsync(Guid personId, CancellationToken ct = default)
    {
        if (personId == NetworkIds.Me)
        {
            return;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Relationships.RemoveRange(await db.Relationships
            .Where(r => r.FromPersonId == personId || r.ToPersonId == personId).ToListAsync(ct));
        db.PersonSegments.RemoveRange(await db.PersonSegments.Where(p => p.PersonId == personId).ToListAsync(ct));
        db.PersonOrganizations.RemoveRange(await db.PersonOrganizations.Where(p => p.PersonId == personId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearVaultPeopleAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Relationships.RemoveRange(await db.Relationships
            .Where(r => r.FromPersonId != NetworkIds.Me || r.ToPersonId != NetworkIds.Me)
            .ToListAsync(ct));
        db.PersonSegments.RemoveRange(await db.PersonSegments
            .Where(p => p.PersonId != NetworkIds.Me).ToListAsync(ct));
        db.PersonOrganizations.RemoveRange(await db.PersonOrganizations
            .Where(p => p.PersonId != NetworkIds.Me).ToListAsync(ct));
        var me = await db.MeProfiles.FirstOrDefaultAsync(ct);
        if (me is not null)
        {
            me.PhotoFileName = null;
            me.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
