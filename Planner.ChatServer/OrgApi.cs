using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public static class OrgApi
{
    public static void MapOrgApi(this WebApplication app)
    {
        app.MapGet(ChatRoutes.OrgCompanies, async (IDbContextFactory<ChatServerDb> factory) =>
        {
            await using var db = await factory.CreateDbContextAsync();
            var items = await db.Companies.AsNoTracking()
                .Where(c => c.Active)
                .OrderBy(c => c.Name)
                .Select(c => new CompanyOptionDto { Id = c.Id, Name = c.Name, Domain = c.Domain })
                .ToListAsync();
            return Results.Json(new CompanyListResponse { Items = items }, ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgCatalog, async (Guid companyId, IDbContextFactory<ChatServerDb> factory) =>
        {
            await using var db = await factory.CreateDbContextAsync();
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId && c.Active);
            if (company is null)
            {
                return ChatAuth.Error("Şirket bulunamadı.", 404);
            }

            return Results.Json(await BuildCatalogAsync(db, company), ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgMe, async (HttpContext http, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            return Results.Json(await ToPersonAsync(db, user, false), ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgReports, async (HttpContext http, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var ids = await OrgRules.DirectReportIdsAsync(db, user);
            var people = await LoadPeopleAsync(db, ids, true);
            return Results.Json(people, ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgTeam, async (HttpContext http, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!AccountUsageKinds.IncludesWork(user.Usage) || user.CompanyId is null || user.PositionId is null)
            {
                return ChatAuth.Error("Kurum kaydı yok.");
            }

            await using var db = await factory.CreateDbContextAsync();
            var directIds = await OrgRules.DirectReportIdsAsync(db, user);
            var descendantPositions = await OrgRules.DescendantPositionIdsAsync(db, user.CompanyId.Value, user.PositionId.Value);
            var subtreeIds = await db.Users.AsNoTracking()
                .Where(u => u.CompanyId == user.CompanyId && u.PositionId != null && descendantPositions.Contains(u.PositionId.Value))
                .Select(u => u.Id)
                .ToListAsync();
            var watch = subtreeIds.Concat(directIds).Distinct().ToList();
            var tasks = await db.WorkTasks.AsNoTracking()
                .Where(t => t.CompanyId == user.CompanyId && (watch.Contains(t.AssignedToUserId) || t.AssignedByUserId == user.Id))
                .OrderByDescending(t => t.CreatedAt)
                .Take(200)
                .ToListAsync();
            return Results.Json(new OrgTeamResponse
            {
                Me = await ToPersonAsync(db, user, false),
                DirectReports = await LoadPeopleAsync(db, directIds, true),
                Subtree = await LoadPeopleAsync(db, subtreeIds, false),
                Tasks = await ToTaskDtosAsync(db, tasks)
            }, ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgWorkTasks, async (HttpContext http, string? scope, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var query = db.WorkTasks.AsNoTracking().Where(t => t.CompanyId == user.CompanyId);
            query = (scope ?? "inbox").ToLowerInvariant() switch
            {
                "assigned" => query.Where(t => t.AssignedByUserId == user.Id),
                "team" => query.Where(t => t.AssignedToUserId != user.Id),
                _ => query.Where(t => t.AssignedToUserId == user.Id)
            };
            var rows = await query.OrderByDescending(t => t.CreatedAt).Take(200).ToListAsync();
            if (scope == "team" && user.PositionId is Guid pos && user.CompanyId is Guid company)
            {
                var descendantPositions = await OrgRules.DescendantPositionIdsAsync(db, company, pos);
                var subtree = await db.Users.AsNoTracking()
                    .Where(u => u.CompanyId == company && u.PositionId != null && descendantPositions.Contains(u.PositionId.Value))
                    .Select(u => u.Id)
                    .ToListAsync();
                rows = rows.Where(t => subtree.Contains(t.AssignedToUserId)).ToList();
            }

            return Results.Json(await ToTaskDtosAsync(db, rows), ChatJson.Options);
        });

        app.MapPost(ChatRoutes.OrgWorkTasks, async (HttpContext http, WorkTaskCreateRequest body, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            try
            {
                var task = await CreateAssignmentAsync(db, user, body, null, http.RequestAborted);
                await hub.NotifyUserAsync(task.AssignedToUserId, ChatTypes.WorkTask, (await ToTaskDtosAsync(db, [task]))[0], http.RequestAborted);
                return Results.Json((await ToTaskDtosAsync(db, [task]))[0], ChatJson.Options);
            }
            catch (InvalidOperationException ex)
            {
                return ChatAuth.Error(ex.Message);
            }
        });

        app.MapPost(ChatRoutes.OrgDistribute, async (HttpContext http, WorkTaskDistributeRequest body, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var source = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == body.TaskId);
            if (source is null || source.AssignedToUserId != user.Id)
            {
                return ChatAuth.Error("Yalnızca size atanan görevi bir altınıza dağıtabilirsiniz.");
            }

            try
            {
                var child = await CreateAssignmentAsync(db, user, new WorkTaskCreateRequest
                {
                    Title = string.IsNullOrWhiteSpace(body.Title) ? source.Title : body.Title,
                    Notes = body.Notes ?? source.Notes,
                    Date = string.IsNullOrWhiteSpace(body.Date) ? source.Date : body.Date,
                    Time = body.Time ?? source.Time,
                    ToUserId = body.ToUserId
                }, source.Id, http.RequestAborted);
                await hub.NotifyUserAsync(child.AssignedToUserId, ChatTypes.WorkTask, (await ToTaskDtosAsync(db, [child]))[0], http.RequestAborted);
                return Results.Json((await ToTaskDtosAsync(db, [child]))[0], ChatJson.Options);
            }
            catch (InvalidOperationException ex)
            {
                return ChatAuth.Error(ex.Message);
            }
        });
    }

    public static async Task AttachMembershipAsync(ChatServerDb db, ServerUser user, AuthRequest body)
    {
        var usage = AccountUsageKinds.Normalize(body.Usage);
        user.Usage = usage;
        user.Email = (body.Email ?? "").Trim();
        user.FirstName = (body.FirstName ?? "").Trim();
        user.LastName = (body.LastName ?? "").Trim();
        if (!AccountUsageKinds.IncludesWork(usage))
        {
            user.CompanyId = null;
            user.UnitId = null;
            user.PositionId = null;
            return;
        }

        var invite = await OrgWorkflowApi.FindOpenInviteAsync(db, body.InviteCode)
                     ?? throw new InvalidOperationException("İş kaydı için yönetim panelinden alınan geçerli bir davet kodu gerekir. Boş kadroyu herkes seçemez.");
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == invite.CompanyId && c.Active)
                      ?? throw new InvalidOperationException("Şirket bulunamadı veya pasif.");
        user.Email = (body.Email ?? "").Trim();
        if (!OrgRules.EmailMatchesDomain(user.Email, company.Domain))
        {
            throw new InvalidOperationException("E-posta bu şirketin alan adına ait olmalı (" + company.Domain + ").");
        }

        await OrgWorkflowApi.ConsumeInviteAsync(db, user, invite);
    }

    public static async Task<OrgCatalogResponse> BuildCatalogAsync(ChatServerDb db, OrgCompany company)
    {
        var units = await db.Units.AsNoTracking().Where(u => u.CompanyId == company.Id)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name).ToListAsync();
        var positions = await db.Positions.AsNoTracking().Where(p => p.CompanyId == company.Id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync();
        var occupants = await db.Users.AsNoTracking()
            .Where(u => u.CompanyId == company.Id && u.PositionId != null)
            .Select(u => new { u.PositionId, u.DisplayName })
            .ToListAsync();
        var occupantMap = occupants.Where(o => o.PositionId is not null)
            .ToDictionary(o => o.PositionId!.Value, o => o.DisplayName);
        var unitMap = units.ToDictionary(u => u.Id);
        var posMap = positions.ToDictionary(p => p.Id);
        return new OrgCatalogResponse
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            Domain = company.Domain,
            Units = units.Select(u => new OrgUnitDto
            {
                Id = u.Id,
                CompanyId = u.CompanyId,
                ParentId = u.ParentId,
                Name = u.Name,
                Kind = u.Kind,
                SortOrder = u.SortOrder,
                Path = OrgRules.UnitPath(units, u)
            }).ToList(),
            Positions = positions.Select(p => new OrgPositionDto
            {
                Id = p.Id,
                CompanyId = p.CompanyId,
                UnitId = p.UnitId,
                UnitName = unitMap.TryGetValue(p.UnitId, out var unit) ? unit.Name : "",
                Title = p.Title,
                ReportsToPositionId = p.ReportsToPositionId,
                ReportsToTitle = p.ReportsToPositionId is Guid rid && posMap.TryGetValue(rid, out var boss) ? boss.Title : null,
                SortOrder = p.SortOrder,
                Occupied = occupantMap.ContainsKey(p.Id),
                OccupantName = occupantMap.TryGetValue(p.Id, out var name) ? name : null,
                CanApproveLeaves = p.CanApproveLeaves
            }).ToList()
        };
    }

    private static async Task<OrgWorkTask> CreateAssignmentAsync(
        ChatServerDb db,
        ServerUser manager,
        WorkTaskCreateRequest body,
        Guid? parentId,
        CancellationToken ct)
    {
        if (!AccountUsageKinds.IncludesWork(manager.Usage) || manager.CompanyId is null || manager.PositionId is null)
        {
            throw new InvalidOperationException("Görev atamak için kurum hesabı ve şemadaki göreviniz gerekli.");
        }

        var title = (body.Title ?? "").Trim();
        if (title.Length < 2)
        {
            throw new InvalidOperationException("Görev başlığı girin.");
        }

        var date = string.IsNullOrWhiteSpace(body.Date)
            ? DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd")
            : body.Date.Trim();
        if (!DateOnly.TryParse(date, out _))
        {
            throw new InvalidOperationException("Tarih geçersiz.");
        }

        Guid assigneeId;
        if (body.ToUserId is Guid personId)
        {
            assigneeId = personId;
        }
        else if (body.ToUnitId is Guid unitId)
        {
            var reports = await OrgRules.DirectReportIdsAsync(db, manager, ct);
            var inUnit = await db.Users.AsNoTracking()
                .Where(u => reports.Contains(u.Id) && u.UnitId == unitId)
                .Select(u => u.Id)
                .ToListAsync(ct);
            assigneeId = inUnit.Count switch
            {
                1 => inUnit[0],
                0 => throw new InvalidOperationException("Bu takıma yalnızca bir altınızdaki kişi üzerinden görev gidebilir. Doğrudan altınız bu birimde değil."),
                _ => throw new InvalidOperationException("Bu birimde birden fazla doğrudan altınız var; kişiyi seçin.")
            };
        }
        else
        {
            throw new InvalidOperationException("Kişi veya takım seçin.");
        }

        if (assigneeId == manager.Id)
        {
            throw new InvalidOperationException("Kendinize kurum görevi atayamazsınız.");
        }

        if (!await OrgRules.IsDirectReportAsync(db, manager, assigneeId, ct))
        {
            throw new InvalidOperationException("Organizasyon şemasında yalnızca bir altınızdaki kişiye görev verebilirsiniz. Görev oradan dağıtılır.");
        }

        var task = new OrgWorkTask
        {
            Id = Guid.NewGuid(),
            CompanyId = manager.CompanyId.Value,
            Title = title,
            Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim(),
            Date = date,
            Time = string.IsNullOrWhiteSpace(body.Time) ? null : body.Time.Trim(),
            AssignedByUserId = manager.Id,
            AssignedToUserId = assigneeId,
            ParentTaskId = parentId,
            Status = 0,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkTasks.Add(task);
        var assignee = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == assigneeId, ct);
        OrgAudit.Add(
            db,
            task.CompanyId,
            manager,
            parentId is null ? OrgAudit.TaskAssign : OrgAudit.TaskDistribute,
            assignee,
            title + " · " + date);
        await db.SaveChangesAsync(ct);
        return task;
    }

    private static async Task<List<OrgPersonDto>> LoadPeopleAsync(ChatServerDb db, IReadOnlyList<Guid> ids, bool direct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync();
        var list = new List<OrgPersonDto>();
        foreach (var user in users.OrderBy(u => u.DisplayName))
        {
            list.Add(await ToPersonAsync(db, user, direct));
        }

        return list;
    }

    public static Task<OrgPersonDto> ToPersonPublicAsync(ChatServerDb db, ServerUser user, bool direct)
        => ToPersonAsync(db, user, direct);

    private static async Task<OrgPersonDto> ToPersonAsync(ChatServerDb db, ServerUser user, bool direct)
    {
        string? unitName = null;
        string? title = null;
        if (user.UnitId is Guid unitId)
        {
            unitName = await db.Units.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Name).FirstOrDefaultAsync();
        }

        if (user.PositionId is Guid positionId)
        {
            title = await db.Positions.AsNoTracking().Where(p => p.Id == positionId).Select(p => p.Title).FirstOrDefaultAsync();
        }

        return new OrgPersonDto
        {
            UserId = user.Id.ToString("N"),
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            UnitId = user.UnitId,
            UnitName = unitName,
            PositionId = user.PositionId,
            PositionTitle = title,
            DirectReport = direct
        };
    }

    private static async Task<List<WorkTaskDto>> ToTaskDtosAsync(ChatServerDb db, IReadOnlyList<OrgWorkTask> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.SelectMany(r => new[] { r.AssignedByUserId, r.AssignedToUserId }).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        var taskIds = rows.Select(r => r.Id).ToList();
        var files = await db.WorkFiles.AsNoTracking()
            .Where(f => taskIds.Contains(f.TaskId))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();
        var filesByTask = files.ToLookup(f => f.TaskId);
        return rows.Select(t => new WorkTaskDto
        {
            Id = t.Id,
            CompanyId = t.CompanyId,
            Title = t.Title,
            Notes = t.Notes,
            Date = t.Date,
            Time = t.Time,
            AssignedByUserId = t.AssignedByUserId.ToString("N"),
            AssignedByName = names.GetValueOrDefault(t.AssignedByUserId, "Yönetici"),
            AssignedToUserId = t.AssignedToUserId.ToString("N"),
            AssignedToName = names.GetValueOrDefault(t.AssignedToUserId, "Çalışan"),
            ParentTaskId = t.ParentTaskId,
            Status = t.Status,
            CreatedAt = t.CreatedAt,
            Files = filesByTask[t.Id].Select(f => new WorkFileDto
            {
                Id = f.Id,
                TaskId = f.TaskId,
                Name = f.Name,
                SizeBytes = f.SizeBytes
            }).ToList()
        }).ToList();
    }
}
