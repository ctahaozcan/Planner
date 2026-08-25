using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public sealed class AdminSessionStore
{
    private readonly ConcurrentDictionary<string, DateTime> _tokens = new(StringComparer.Ordinal);

    public string Issue()
    {
        var token = ChatPassword.NewToken();
        _tokens[token] = DateTime.UtcNow.AddHours(12);
        return token;
    }

    public bool Valid(string? token)
        => !string.IsNullOrWhiteSpace(token)
           && _tokens.TryGetValue(token, out var exp)
           && exp > DateTime.UtcNow;
}

public static class AdminApi
{
    public static void MapAdminApi(this WebApplication app)
    {
        app.MapPost(ChatRoutes.AdminLogin, async (AdminLoginRequest body, IConfiguration config, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory, HttpContext http) =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await AdminSecrets.EnsureAsync(db, config);
            if (!await AdminSecrets.VerifyAsync(db, body.Password ?? ""))
            {
                return ChatAuth.Error("Yönetici şifresi yanlış.", 401);
            }

            var httpsPort = config.GetValue("Https:Port", ChatRoutes.DefaultHttpsPort);
            return Results.Json(new AdminLoginResponse
            {
                Token = sessions.Issue(),
                WeakPassword = await AdminSecrets.IsStoredWellKnownAsync(db),
                UsingHttps = http.Request.IsHttps
                    || string.Equals(http.Request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase),
                HttpsPort = httpsPort
            }, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.AdminPassword, async (HttpContext http, AdminPasswordChangeRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            try
            {
                AdminSecrets.ValidateNew(body.Next);
            }
            catch (InvalidOperationException ex)
            {
                return ChatAuth.Error(ex.Message);
            }

            await using var db = await factory.CreateDbContextAsync();
            if (!await AdminSecrets.VerifyAsync(db, body.Current ?? ""))
            {
                return ChatAuth.Error("Mevcut şifre yanlış.", 401);
            }

            await AdminSecrets.StoreAsync(db, body.Next.Trim());
            return Results.Ok();
        });

        app.MapGet(ChatRoutes.AdminAudit, async (HttpContext http, Guid? companyId, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var query = db.AuditLog.AsNoTracking().AsQueryable();
            if (companyId is Guid id)
            {
                query = query.Where(e => e.CompanyId == id);
            }

            var rows = await query.OrderByDescending(e => e.At).Take(300).ToListAsync();
            return Results.Json(rows.Select(OrgAudit.ToDto).ToList(), ChatJson.Options);
        });

        app.MapGet(ChatRoutes.AdminInvites, async (HttpContext http, Guid companyId, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var invites = await db.Invites.AsNoTracking()
                .Where(i => i.CompanyId == companyId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
            var positions = await db.Positions.AsNoTracking().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id);
            var units = await db.Units.AsNoTracking().Where(u => u.CompanyId == companyId).ToDictionaryAsync(u => u.Id, u => u.Name);
            var users = await db.Users.AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            var list = invites.Select(i =>
            {
                positions.TryGetValue(i.PositionId, out var pos);
                var unitName = pos is not null && units.TryGetValue(pos.UnitId, out var un) ? un : "";
                return new AdminInviteDto
                {
                    Id = i.Id,
                    Code = i.Code,
                    PositionId = i.PositionId,
                    PositionTitle = pos?.Title ?? "—",
                    UnitName = unitName,
                    Email = i.Email,
                    ExpiresAt = i.ExpiresAt,
                    UsedAt = i.UsedAt,
                    UsedByName = i.UsedByUserId is Guid uid && users.TryGetValue(uid, out var name) ? name : null
                };
            }).ToList();
            return Results.Json(list, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.AdminInvites, async (HttpContext http, AdminInviteCreateRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == body.PositionId && p.CompanyId == body.CompanyId);
            if (position is null)
            {
                return ChatAuth.Error("Kadro bu şirkete ait değil.");
            }

            if (await db.Users.AnyAsync(u => u.PositionId == position.Id))
            {
                return ChatAuth.Error("Bu kadro dolu. Önce oturanı kaldırın veya boş bir kadro seçin.");
            }

            var email = string.IsNullOrWhiteSpace(body.Email) ? null : body.Email.Trim();
            if (email is not null && !email.Contains('@', StringComparison.Ordinal))
            {
                return ChatAuth.Error("E-posta kilitlenecekse geçerli bir adres yazın.");
            }

            var days = body.Days <= 0 ? 14 : Math.Clamp(body.Days, 1, 90);
            string code;
            do
            {
                code = OrgRules.NewInviteCode();
            } while (await db.Invites.AnyAsync(i => i.Code == code));

            var row = new OrgInvite
            {
                Id = Guid.NewGuid(),
                CompanyId = body.CompanyId,
                PositionId = body.PositionId,
                Code = code,
                Email = email,
                ExpiresAt = DateTime.UtcNow.AddDays(days),
                CreatedAt = DateTime.UtcNow
            };
            db.Invites.Add(row);
            await db.SaveChangesAsync();
            var preview = await OrgWorkflowApi.ToInvitePreviewAsync(db, row);
            return Results.Json(new AdminInviteDto
            {
                Id = row.Id,
                Code = row.Code,
                PositionId = row.PositionId,
                PositionTitle = preview.PositionTitle,
                UnitName = preview.UnitName,
                Email = row.Email,
                ExpiresAt = row.ExpiresAt
            }, ChatJson.Options);
        });

        app.MapDelete(ChatRoutes.AdminInvites + "/{id:guid}", async (HttpContext http, Guid id, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Invites.FirstOrDefaultAsync(i => i.Id == id);
            if (row is not null)
            {
                db.Invites.Remove(row);
                await db.SaveChangesAsync();
            }

            return Results.Ok();
        });

        app.MapGet(ChatRoutes.AdminCompanies, async (HttpContext http, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var items = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
            return Results.Json(items, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.AdminCompanies, async (HttpContext http, AdminCompanySaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            var name = (body.Name ?? "").Trim();
            var domain = NormalizeDomainField(body.Domain);
            if (name.Length < 2 || domain.Length < 3)
            {
                return ChatAuth.Error("Şirket adı ve alan adı gerekli (ör. firma.com).");
            }

            await using var db = await factory.CreateDbContextAsync();
            var row = new OrgCompany
            {
                Id = Guid.NewGuid(),
                Name = name,
                Domain = domain,
                Notes = (body.Notes ?? "").Trim(),
                Active = body.Active,
                CreatedAt = DateTime.UtcNow
            };
            db.Companies.Add(row);
            await db.SaveChangesAsync();
            return Results.Json(row, ChatJson.Options);
        });

        app.MapPut(ChatRoutes.AdminCompanies + "/{id:guid}", async (HttpContext http, Guid id, AdminCompanySaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (row is null)
            {
                return ChatAuth.Error("Şirket yok.", 404);
            }

            row.Name = (body.Name ?? "").Trim();
            row.Domain = NormalizeDomainField(body.Domain);
            row.Notes = (body.Notes ?? "").Trim();
            row.Active = body.Active;
            if (row.Name.Length < 2 || row.Domain.Length < 3)
            {
                return ChatAuth.Error("Şirket adı ve alan adı gerekli.");
            }

            await db.SaveChangesAsync();
            return Results.Json(row, ChatJson.Options);
        });

        app.MapDelete(ChatRoutes.AdminCompanies + "/{id:guid}", async (HttpContext http, Guid id, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            db.Positions.RemoveRange(await db.Positions.Where(p => p.CompanyId == id).ToListAsync());
            db.Units.RemoveRange(await db.Units.Where(u => u.CompanyId == id).ToListAsync());
            db.WorkTasks.RemoveRange(await db.WorkTasks.Where(t => t.CompanyId == id).ToListAsync());
            db.Invites.RemoveRange(await db.Invites.Where(i => i.CompanyId == id).ToListAsync());
            db.Leaves.RemoveRange(await db.Leaves.Where(l => l.CompanyId == id).ToListAsync());
            db.WorkFiles.RemoveRange(await db.WorkFiles.Where(f => f.CompanyId == id).ToListAsync());
            db.AuditLog.RemoveRange(await db.AuditLog.Where(e => e.CompanyId == id).ToListAsync());
            var members = await db.Users.Where(u => u.CompanyId == id).ToListAsync();
            foreach (var member in members)
            {
                member.CompanyId = null;
                member.UnitId = null;
                member.PositionId = null;
                member.Usage = AccountUsageKinds.Personal;
            }

            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is not null)
            {
                db.Companies.Remove(company);
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        app.MapGet(ChatRoutes.AdminCompanies + "/{id:guid}/catalog", async (HttpContext http, Guid id, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (company is null)
            {
                return ChatAuth.Error("Şirket yok.", 404);
            }

            return Results.Json(await OrgApi.BuildCatalogAsync(db, company), ChatJson.Options);
        });

        app.MapGet(ChatRoutes.AdminCompanies + "/{id:guid}/members", async (HttpContext http, Guid id, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var users = await db.Users.AsNoTracking().Where(u => u.CompanyId == id).OrderBy(u => u.DisplayName).ToListAsync();
            var units = await db.Units.AsNoTracking().Where(u => u.CompanyId == id).ToDictionaryAsync(u => u.Id, u => u.Name);
            var positions = await db.Positions.AsNoTracking().Where(p => p.CompanyId == id).ToDictionaryAsync(p => p.Id, p => p.Title);
            var list = users.Select(u => new OrgPersonDto
            {
                UserId = u.Id.ToString("N"),
                Username = u.Username,
                DisplayName = u.DisplayName,
                Email = u.Email,
                UnitId = u.UnitId,
                UnitName = u.UnitId is Guid uid && units.TryGetValue(uid, out var un) ? un : null,
                PositionId = u.PositionId,
                PositionTitle = u.PositionId is Guid pid && positions.TryGetValue(pid, out var pt) ? pt : null
            }).ToList();
            return Results.Json(list, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.AdminUnits, async (HttpContext http, AdminUnitSaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            var name = (body.Name ?? "").Trim();
            if (name.Length < 2)
            {
                return ChatAuth.Error("Birim/takım adı gerekli.");
            }

            await using var db = await factory.CreateDbContextAsync();
            if (!await db.Companies.AnyAsync(c => c.Id == body.CompanyId))
            {
                return ChatAuth.Error("Şirket yok.", 404);
            }

            if (body.ParentId is Guid parentId)
            {
                var parent = await db.Units.FirstOrDefaultAsync(u => u.Id == parentId);
                if (parent is null || parent.CompanyId != body.CompanyId)
                {
                    return ChatAuth.Error("Üst birim bu şirkete ait değil.");
                }
            }

            var row = new OrgUnit
            {
                Id = Guid.NewGuid(),
                CompanyId = body.CompanyId,
                ParentId = body.ParentId,
                Name = name,
                Kind = OrgRules.NormalizeKind(body.Kind),
                SortOrder = body.SortOrder
            };
            db.Units.Add(row);
            await db.SaveChangesAsync();
            return Results.Json(row, ChatJson.Options);
        });

        app.MapPut(ChatRoutes.AdminUnits + "/{id:guid}", async (HttpContext http, Guid id, AdminUnitSaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Units.FirstOrDefaultAsync(u => u.Id == id);
            if (row is null)
            {
                return ChatAuth.Error("Birim yok.", 404);
            }

            if (await OrgRules.WouldCreateUnitCycleAsync(db, id, body.ParentId))
            {
                return ChatAuth.Error("Birim kendi altına bağlanamaz.");
            }

            row.Name = (body.Name ?? "").Trim();
            row.ParentId = body.ParentId;
            row.Kind = OrgRules.NormalizeKind(body.Kind);
            row.SortOrder = body.SortOrder;
            await db.SaveChangesAsync();
            return Results.Json(row, ChatJson.Options);
        });

        app.MapDelete(ChatRoutes.AdminUnits + "/{id:guid}", async (HttpContext http, Guid id, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            if (await db.Units.AnyAsync(u => u.ParentId == id) || await db.Positions.AnyAsync(p => p.UnitId == id))
            {
                return ChatAuth.Error("Önce alt birimleri ve görev tanımlarını silin.");
            }

            var row = await db.Units.FirstOrDefaultAsync(u => u.Id == id);
            if (row is not null)
            {
                db.Units.Remove(row);
                await db.SaveChangesAsync();
            }

            return Results.Ok();
        });

        app.MapPost(ChatRoutes.AdminPositions, async (HttpContext http, AdminPositionSaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            var title = (body.Title ?? "").Trim();
            if (title.Length < 2)
            {
                return ChatAuth.Error("Görev tanımı gerekli.");
            }

            await using var db = await factory.CreateDbContextAsync();
            var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == body.UnitId && u.CompanyId == body.CompanyId);
            if (unit is null)
            {
                return ChatAuth.Error("Birim bu şirkete ait değil.");
            }

            if (body.ReportsToPositionId is Guid bossId)
            {
                var boss = await db.Positions.FirstOrDefaultAsync(p => p.Id == bossId);
                if (boss is null || boss.CompanyId != body.CompanyId)
                {
                    return ChatAuth.Error("Bağlı olunan görev bu şirkete ait değil.");
                }
            }

            var row = new OrgPosition
            {
                Id = Guid.NewGuid(),
                CompanyId = body.CompanyId,
                UnitId = body.UnitId,
                Title = title,
                ReportsToPositionId = body.ReportsToPositionId,
                SortOrder = body.SortOrder,
                CanApproveLeaves = body.CanApproveLeaves
            };
            db.Positions.Add(row);
            await db.SaveChangesAsync();
            return Results.Json(row, ChatJson.Options);
        });

        app.MapPut(ChatRoutes.AdminPositions + "/{id:guid}", async (HttpContext http, Guid id, AdminPositionSaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Positions.FirstOrDefaultAsync(p => p.Id == id);
            if (row is null)
            {
                return ChatAuth.Error("Görev tanımı yok.", 404);
            }

            if (await OrgRules.WouldCreatePositionCycleAsync(db, id, body.ReportsToPositionId))
            {
                return ChatAuth.Error("Görev kendi altına bağlanamaz. Şema bir kademe yukarı bakmalıdır.");
            }

            var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == body.UnitId && u.CompanyId == row.CompanyId);
            if (unit is null)
            {
                return ChatAuth.Error("Birim bu şirkete ait değil.");
            }

            row.Title = (body.Title ?? "").Trim();
            row.UnitId = body.UnitId;
            row.ReportsToPositionId = body.ReportsToPositionId;
            row.SortOrder = body.SortOrder;
            row.CanApproveLeaves = body.CanApproveLeaves;
            await db.SaveChangesAsync();
            return Results.Json(row, ChatJson.Options);
        });

        app.MapDelete(ChatRoutes.AdminPositions + "/{id:guid}", async (HttpContext http, Guid id, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            if (await db.Positions.AnyAsync(p => p.ReportsToPositionId == id) || await db.Users.AnyAsync(u => u.PositionId == id))
            {
                return ChatAuth.Error("Önce alt görevleri veya oturan kullanıcıyı kaldırın.");
            }

            var row = await db.Positions.FirstOrDefaultAsync(p => p.Id == id);
            if (row is not null)
            {
                db.Positions.Remove(row);
                await db.SaveChangesAsync();
            }

            return Results.Ok();
        });

        app.MapPut(ChatRoutes.AdminMembers + "/{id:guid}", async (HttpContext http, Guid id, AdminMemberSaveRequest body, AdminSessionStore sessions, IDbContextFactory<ChatServerDb> factory) =>
        {
            if (!RequireAdmin(http, sessions))
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return ChatAuth.Error("Kullanıcı yok.", 404);
            }

            if (body.PositionId is Guid positionId)
            {
                if (await db.Users.AnyAsync(u => u.PositionId == positionId && u.Id != id))
                {
                    return ChatAuth.Error("Bu görev tanımı dolu.");
                }

                var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == positionId);
                if (position is null)
                {
                    return ChatAuth.Error("Görev tanımı yok.");
                }

                user.PositionId = position.Id;
                user.UnitId = body.UnitId ?? position.UnitId;
                user.CompanyId ??= position.CompanyId;
            }
            else
            {
                user.PositionId = null;
                user.UnitId = body.UnitId;
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    private static bool RequireAdmin(HttpContext http, AdminSessionStore sessions)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : "";
        return sessions.Valid(token);
    }

    private static string NormalizeDomainField(string? domain)
        => string.Join(", ", OrgRules.SplitDomains(domain ?? ""));
}
