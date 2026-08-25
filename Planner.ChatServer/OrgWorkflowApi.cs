using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public static class OrgWorkflowApi
{
    public static void MapOrgWorkflowApi(this WebApplication app)
    {
        app.MapGet(ChatRoutes.OrgInvite, async (string? code, IDbContextFactory<ChatServerDb> factory) =>
        {
            await using var db = await factory.CreateDbContextAsync();
            var invite = await FindOpenInviteAsync(db, code);
            if (invite is null)
            {
                return ChatAuth.Error("Davet kodu geçersiz, süresi dolmuş veya kullanılmış.", 404);
            }

            return Results.Json(await ToInvitePreviewAsync(db, invite), ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgLeaves, async (HttpContext http, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!AccountUsageKinds.IncludesWork(user.Usage) || user.CompanyId is null)
            {
                return ChatAuth.Error("Kurum kaydı yok.");
            }

            await using var db = await factory.CreateDbContextAsync();
            return Results.Json(await BuildLeaveBoardAsync(db, user), ChatJson.Options);
        });

        app.MapPost(ChatRoutes.OrgLeaves, async (HttpContext http, OrgLeaveCreateRequest body, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!AccountUsageKinds.IncludesWork(user.Usage) || user.CompanyId is null)
            {
                return ChatAuth.Error("İş hesabı için izin talebi sunucuya gider.");
            }

            var typeName = (body.TypeName ?? "").Trim();
            if (typeName.Length < 2 || !DateOnly.TryParse(body.StartDate, out _) || !DateOnly.TryParse(body.EndDate, out _))
            {
                return ChatAuth.Error("İzin türü ve tarihler gerekli.");
            }

            await using var db = await factory.CreateDbContextAsync();
            if (body.ClientId is Guid clientId)
            {
                var existing = await db.Leaves.FirstOrDefaultAsync(l => l.CompanyId == user.CompanyId && l.ClientId == clientId);
                if (existing is not null)
                {
                    return Results.Json(await ToLeaveDtoAsync(db, existing), ChatJson.Options);
                }
            }

            var row = new OrgLeave
            {
                Id = Guid.NewGuid(),
                CompanyId = user.CompanyId.Value,
                UserId = user.Id,
                ClientId = body.ClientId,
                TypeName = typeName,
                EntryKind = body.EntryKind,
                DurationKind = body.DurationKind,
                StartDate = body.StartDate.Trim(),
                EndDate = body.EndDate.Trim(),
                StartTime = string.IsNullOrWhiteSpace(body.StartTime) ? null : body.StartTime.Trim(),
                EndTime = string.IsNullOrWhiteSpace(body.EndTime) ? null : body.EndTime.Trim(),
                StartHalf = body.StartHalf,
                EndHalf = body.EndHalf,
                Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
                Status = "pending",
                DurationMinutes = body.DurationMinutes,
                CreatedAt = DateTime.UtcNow
            };
            db.Leaves.Add(row);
            OrgAudit.Add(db, row.CompanyId, user, OrgAudit.LeaveSubmit, null, typeName + " · " + row.StartDate + " – " + row.EndDate);
            await db.SaveChangesAsync();
            var dto = await ToLeaveDtoAsync(db, row);
            foreach (var managerId in await OrgRules.ManagerUserIdsAsync(db, user))
            {
                await hub.NotifyUserAsync(managerId, ChatTypes.Leave, dto, http.RequestAborted);
            }

            return Results.Json(dto, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.OrgLeaveDecide, async (HttpContext http, OrgLeaveDecideRequest body, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Leaves.FirstOrDefaultAsync(l => l.Id == body.Id);
            if (row is null || row.CompanyId != user.CompanyId)
            {
                return ChatAuth.Error("İzin talebi bulunamadı.", 404);
            }

            if (row.Status != "pending")
            {
                return ChatAuth.Error("Bu talep zaten karara bağlanmış.");
            }

            if (!await OrgRules.CanDecideLeaveAsync(db, user, row.UserId))
            {
                return ChatAuth.Error("Bu izni yalnızca bir üst amir veya izin yetkilisi onaylar.");
            }

            row.Status = body.Approve ? "approved" : "rejected";
            row.DecidedByUserId = user.Id;
            if (!string.IsNullOrWhiteSpace(body.Note))
            {
                row.Note = string.IsNullOrWhiteSpace(row.Note)
                    ? body.Note.Trim()
                    : row.Note + "\nKarar: " + body.Note.Trim();
            }

            var subject = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId);
            OrgAudit.Add(
                db,
                row.CompanyId,
                user,
                body.Approve ? OrgAudit.LeaveApprove : OrgAudit.LeaveReject,
                subject,
                row.TypeName + " · " + row.StartDate);
            await db.SaveChangesAsync();
            var dto = await ToLeaveDtoAsync(db, row);
            await hub.NotifyUserAsync(row.UserId, ChatTypes.Leave, dto, http.RequestAborted);
            return Results.Json(dto, ChatJson.Options);
        });

        app.MapGet(ChatRoutes.OrgAudit, async (HttpContext http, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!AccountUsageKinds.IncludesWork(user.Usage) || user.CompanyId is null)
            {
                return ChatAuth.Error("Kurum kaydı yok.");
            }

            await using var db = await factory.CreateDbContextAsync();
            var query = db.AuditLog.AsNoTracking().Where(e => e.CompanyId == user.CompanyId);
            if (!await OrgRules.PositionApprovesLeavesAsync(db, user.PositionId)
                && user.PositionId is Guid pos)
            {
                var visible = await OrgRules.LeaveAudienceIdsAsync(db, user);
                visible.Add(user.Id);
                query = query.Where(e => visible.Contains(e.ActorUserId)
                                         || (e.TargetUserId != null && visible.Contains(e.TargetUserId.Value)));
            }

            var rows = await query.OrderByDescending(e => e.At).Take(200).ToListAsync();
            return Results.Json(rows.Select(OrgAudit.ToDto).ToList(), ChatJson.Options);
        });

        app.MapPost(ChatRoutes.OrgWorkTasks + "/{taskId:guid}/files", async (
            HttpContext http,
            Guid taskId,
            IDbContextFactory<ChatServerDb> factory,
            ChatServerPaths paths) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            http.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = ChatRoutes.MaxOrgFileBytes + (1024 * 1024);
            var form = await http.Request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return ChatAuth.Error("Dosya seçin.");
            }

            if (file.Length > ChatRoutes.MaxOrgFileBytes)
            {
                return ChatAuth.Error("Kurum dosyası en fazla 20 MB olabilir.");
            }

            await using var db = await factory.CreateDbContextAsync();
            var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == taskId);
            if (task is null || !await OrgRules.CanAccessWorkTaskAsync(db, user, task))
            {
                return ChatAuth.Error("Göreve dosya ekleme yetkiniz yok.", 404);
            }

            var id = Guid.NewGuid();
            var ext = Path.GetExtension(file.FileName);
            if (ext.Length > 12)
            {
                ext = "";
            }

            var stored = id.ToString("N") + ext;
            var dir = Path.Combine(paths.FilesDir, task.CompanyId.ToString("N"), task.Id.ToString("N"));
            Directory.CreateDirectory(dir);
            var disk = Path.Combine(dir, stored);
            await using (var stream = File.Create(disk))
            {
                await file.CopyToAsync(stream);
            }

            var row = new OrgWorkFile
            {
                Id = id,
                CompanyId = task.CompanyId,
                TaskId = task.Id,
                UploadedByUserId = user.Id,
                Name = Path.GetFileName(file.FileName),
                StoredName = stored,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                SizeBytes = file.Length,
                CreatedAt = DateTime.UtcNow
            };
            db.WorkFiles.Add(row);
            OrgAudit.Add(db, task.CompanyId, user, OrgAudit.FileUpload, null, row.Name + " · " + task.Title);
            await db.SaveChangesAsync();
            return Results.Json(new WorkFileDto
            {
                Id = row.Id,
                TaskId = row.TaskId,
                Name = row.Name,
                SizeBytes = row.SizeBytes
            }, ChatJson.Options);
        }).DisableAntiforgery();

        app.MapGet(ChatRoutes.OrgFile + "/{id:guid}", async (HttpContext http, Guid id, IDbContextFactory<ChatServerDb> factory, ChatServerPaths paths) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var file = await db.WorkFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
            if (file is null)
            {
                return ChatAuth.Error("Dosya yok.", 404);
            }

            var task = await db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == file.TaskId);
            if (task is null || !await OrgRules.CanAccessWorkTaskAsync(db, user, task))
            {
                return Results.Forbid();
            }

            var disk = Path.Combine(paths.FilesDir, file.CompanyId.ToString("N"), file.TaskId.ToString("N"), file.StoredName);
            if (!File.Exists(disk))
            {
                return ChatAuth.Error("Dosya diskte yok.", 404);
            }

            return Results.File(disk, file.ContentType, file.Name);
        });
    }

    public static async Task<OrgInvite?> FindOpenInviteAsync(ChatServerDb db, string? code)
    {
        var normalized = OrgRules.NormalizeInviteCode(code);
        if (normalized.Length < 4)
        {
            return null;
        }

        var invite = await db.Invites.FirstOrDefaultAsync(i => i.Code == normalized);
        if (invite is null || invite.UsedAt is not null || invite.ExpiresAt < DateTime.UtcNow)
        {
            return null;
        }

        return invite;
    }

    public static async Task<InvitePreviewDto> ToInvitePreviewAsync(ChatServerDb db, OrgInvite invite)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == invite.CompanyId);
        var position = await db.Positions.AsNoTracking().FirstAsync(p => p.Id == invite.PositionId);
        var unit = await db.Units.AsNoTracking().FirstAsync(u => u.Id == position.UnitId);
        return new InvitePreviewDto
        {
            Code = invite.Code,
            CompanyId = company.Id,
            CompanyName = company.Name,
            Domain = company.Domain,
            UnitId = unit.Id,
            UnitName = unit.Name,
            PositionId = position.Id,
            PositionTitle = position.Title,
            Email = invite.Email,
            ExpiresAt = invite.ExpiresAt
        };
    }

    public static async Task ConsumeInviteAsync(ChatServerDb db, ServerUser user, OrgInvite invite)
    {
        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == invite.PositionId)
                       ?? throw new InvalidOperationException("Davetteki kadro artık yok.");
        if (await db.Users.AnyAsync(u => u.PositionId == position.Id && u.Id != user.Id))
        {
            throw new InvalidOperationException("Bu kadro dolmuş. Yeni davet isteyin.");
        }

        if (!string.IsNullOrWhiteSpace(invite.Email)
            && !string.Equals(invite.Email.Trim(), user.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Bu davet yalnızca " + invite.Email + " adresine açık.");
        }

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == position.UnitId)
                   ?? throw new InvalidOperationException("Davetteki birim yok.");
        user.CompanyId = invite.CompanyId;
        user.UnitId = unit.Id;
        user.PositionId = position.Id;
        invite.UsedAt = DateTime.UtcNow;
        invite.UsedByUserId = user.Id;
        OrgAudit.Add(db, invite.CompanyId, user, OrgAudit.InviteUse, user, position.Title);
    }

    private static async Task<OrgLeaveBoardResponse> BuildLeaveBoardAsync(ChatServerDb db, ServerUser user)
    {
        var audience = await OrgRules.LeaveAudienceIdsAsync(db, user);
        var canManage = audience.Count > 0 || await OrgRules.PositionApprovesLeavesAsync(db, user.PositionId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var peopleIds = audience.Distinct().ToList();
        var leaves = await db.Leaves.AsNoTracking()
            .Where(l => l.CompanyId == user.CompanyId && (peopleIds.Contains(l.UserId) || l.UserId == user.Id))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        var byUser = leaves.ToLookup(l => l.UserId);
        var inbox = new List<OrgLeaveDto>();
        foreach (var pending in leaves.Where(l => l.Status == "pending" && peopleIds.Contains(l.UserId)))
        {
            if (await OrgRules.CanDecideLeaveAsync(db, user, pending.UserId))
            {
                inbox.Add(await ToLeaveDtoAsync(db, pending));
            }
        }

        var people = new List<OrgLeavePersonRow>();
        if (peopleIds.Count > 0)
        {
            var users = await db.Users.AsNoTracking().Where(u => peopleIds.Contains(u.Id)).OrderBy(u => u.DisplayName).ToListAsync();
            foreach (var person in users)
            {
                var personLeaves = byUser[person.Id].ToList();
                people.Add(new OrgLeavePersonRow
                {
                    Person = await OrgApi.ToPersonPublicAsync(db, person, await OrgRules.IsDirectReportAsync(db, user, person.Id)),
                    TodayStatus = OrgRules.TodayLeaveLabel(personLeaves, today),
                    PendingCount = personLeaves.Count(l => l.Status == "pending"),
                    NextLeave = OrgRules.NextLeaveLabel(personLeaves, today),
                    Leaves = await ToLeaveDtosAsync(db, personLeaves.Take(12).ToList())
                });
            }
        }

        var mine = await ToLeaveDtosAsync(db, byUser[user.Id].ToList());
        return new OrgLeaveBoardResponse
        {
            CanManage = canManage,
            Inbox = inbox,
            People = people,
            Mine = mine
        };
    }

    private static async Task<List<OrgLeaveDto>> ToLeaveDtosAsync(ChatServerDb db, IReadOnlyList<OrgLeave> rows)
    {
        var list = new List<OrgLeaveDto>(rows.Count);
        foreach (var row in rows)
        {
            list.Add(await ToLeaveDtoAsync(db, row));
        }

        return list;
    }

    private static async Task<OrgLeaveDto> ToLeaveDtoAsync(ChatServerDb db, OrgLeave row)
    {
        var names = await db.Users.AsNoTracking()
            .Where(u => u.Id == row.UserId || u.Id == row.DecidedByUserId)
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        return new OrgLeaveDto
        {
            Id = row.Id,
            ClientId = row.ClientId,
            UserId = row.UserId.ToString("N"),
            UserName = names.GetValueOrDefault(row.UserId, "Çalışan"),
            TypeName = row.TypeName,
            EntryKind = row.EntryKind,
            DurationKind = row.DurationKind,
            StartDate = row.StartDate,
            EndDate = row.EndDate,
            StartTime = row.StartTime,
            EndTime = row.EndTime,
            StartHalf = row.StartHalf,
            EndHalf = row.EndHalf,
            Note = row.Note,
            Status = row.Status,
            DecidedByName = row.DecidedByUserId is Guid did ? names.GetValueOrDefault(did) : null,
            DurationMinutes = row.DurationMinutes
        };
    }
}
