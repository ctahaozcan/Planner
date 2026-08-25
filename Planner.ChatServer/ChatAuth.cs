using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public static class ChatAuth
{
    public static async Task<AuthResponse> IssueSessionAsync(ChatServerDb db, ServerUser user, int sessionDays)
    {
        var token = ChatPassword.NewToken();
        var expires = DateTime.UtcNow.AddDays(Math.Clamp(sessionDays, 1, 365));
        db.Sessions.Add(new ServerSession
        {
            Token = token,
            UserId = user.Id,
            ExpiresAt = expires
        });
        await db.SaveChangesAsync();

        string? companyName = null;
        string? unitName = null;
        string? positionTitle = null;
        var canAssign = false;
        if (user.CompanyId is Guid companyId)
        {
            companyName = await db.Companies.AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();
        }

        if (user.UnitId is Guid unitId)
        {
            unitName = await db.Units.AsNoTracking()
                .Where(u => u.Id == unitId)
                .Select(u => u.Name)
                .FirstOrDefaultAsync();
        }

        if (user.PositionId is Guid positionId)
        {
            positionTitle = await db.Positions.AsNoTracking()
                .Where(p => p.Id == positionId)
                .Select(p => p.Title)
                .FirstOrDefaultAsync();
            canAssign = await db.Positions.AsNoTracking()
                .AnyAsync(p => p.ReportsToPositionId == positionId);
        }

        return new AuthResponse
        {
            Token = token,
            UserId = user.Id.ToString("N"),
            Username = user.Username,
            DisplayName = user.DisplayName,
            ExpiresAt = expires,
            Usage = AccountUsageKinds.Normalize(user.Usage),
            CompanyId = user.CompanyId,
            CompanyName = companyName,
            UnitId = user.UnitId,
            UnitName = unitName,
            PositionId = user.PositionId,
            PositionTitle = positionTitle,
            CanAssignWork = canAssign
        };
    }

    public static async Task<ServerUser?> CurrentUserAsync(HttpContext http, IDbContextFactory<ChatServerDb> factory)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : "";
        return await UserFromTokenAsync(factory, token);
    }

    public static async Task<ServerUser?> UserFromTokenAsync(IDbContextFactory<ChatServerDb> factory, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Token == token);
        if (session is null || session.ExpiresAt < DateTime.UtcNow)
        {
            return null;
        }

        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == session.UserId);
    }

    public static IResult Error(string message, int status = 400)
        => Results.Json(new ErrorResponse { Error = message }, ChatJson.Options, statusCode: status);
}
