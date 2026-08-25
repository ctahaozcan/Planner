using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public static class ChatApi
{
    private static readonly Regex UsernameRx = new("^[a-z0-9_]{3,32}$", RegexOptions.CultureInvariant);

    public static void MapChatApi(this WebApplication app, int sessionDays)
    {
        app.MapGet(ChatRoutes.Health, () => Results.Json(new HealthResponse(), ChatJson.Options));

        app.MapPost(ChatRoutes.Register, async (AuthRequest body, IDbContextFactory<ChatServerDb> factory) =>
        {
            var username = ChatPassword.NormalizeUsername(body.Username);
            if (!UsernameRx.IsMatch(username))
            {
                return Results.BadRequest(new ErrorResponse { Error = "Kullanıcı adı 3–32 karakter, küçük harf / rakam / alt çizgi." });
            }

            if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 4)
            {
                return Results.BadRequest(new ErrorResponse { Error = "Şifre en az 4 karakter olmalı." });
            }

            await using var db = await factory.CreateDbContextAsync();
            if (await db.Users.AnyAsync(u => u.Username == username))
            {
                return Results.Conflict(new ErrorResponse { Error = "Bu kullanıcı adı alınmış." });
            }

            var email = (body.Email ?? "").Trim();
            if (email.Length > 0 && await db.Users.AnyAsync(u => u.Email == email))
            {
                return Results.Conflict(new ErrorResponse { Error = "Bu e-posta ile kayıt var." });
            }

            var (salt, verifier) = ChatPassword.Hash(body.Password);
            var display = string.IsNullOrWhiteSpace(body.DisplayName)
                ? string.Join(" ", new[] { body.FirstName, body.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim()
                : body.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(display))
            {
                display = username;
            }

            var user = new ServerUser
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = display,
                Email = email,
                FirstName = (body.FirstName ?? "").Trim(),
                LastName = (body.LastName ?? "").Trim(),
                PasswordSalt = salt,
                PasswordVerifier = verifier,
                CreatedAt = DateTime.UtcNow
            };
            try
            {
                await OrgApi.AttachMembershipAsync(db, user, body);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }

            db.Users.Add(user);
            var auth = await ChatAuth.IssueSessionAsync(db, user, sessionDays);
            return Results.Json(auth, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.Login, async (AuthRequest body, IDbContextFactory<ChatServerDb> factory) =>
        {
            var username = ChatPassword.NormalizeUsername(body.Username);
            await using var db = await factory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null || !ChatPassword.Verify(body.Password, user.PasswordSalt, user.PasswordVerifier))
            {
                return Results.Json(new ErrorResponse { Error = "Kullanıcı veya şifre yanlış." }, ChatJson.Options, statusCode: 401);
            }

            var auth = await ChatAuth.IssueSessionAsync(db, user, sessionDays);
            return Results.Json(auth, ChatJson.Options);
        });

        app.MapGet(ChatRoutes.Users, async (HttpContext http, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var q = http.Request.Query["q"].ToString().Trim();
            await using var db = await factory.CreateDbContextAsync();
            if (q.Length >= 2)
            {
                return Results.Json(await hub.SearchDirectoryAsync(db, user.Id, q), ChatJson.Options);
            }

            var snapshot = await hub.BuildSnapshotAsync(db);
            snapshot.Users = snapshot.Users.Where(u => u.UserId != user.Id.ToString("N")).ToList();
            return Results.Json(snapshot, ChatJson.Options);
        });

        app.MapGet(ChatRoutes.UsersSearch, async (HttpContext http, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var q = http.Request.Query["q"].ToString().Trim();
            if (q.Length < 2)
            {
                return Results.BadRequest(new ErrorResponse { Error = "En az 2 karakter yazın." });
            }

            await using var db = await factory.CreateDbContextAsync();
            return Results.Json(await hub.SearchDirectoryAsync(db, user.Id, q), ChatJson.Options);
        });

        app.MapGet(ChatRoutes.Messages, async (HttpContext http, string? peer, IDbContextFactory<ChatServerDb> factory) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await using var db = await factory.CreateDbContextAsync();
            var me = user.Id;
            var query = db.Messages.AsNoTracking().Where(m => m.FromUserId == me || m.ToUserId == me);
            if (Guid.TryParse(peer, out var peerId))
            {
                query = query.Where(m =>
                    (m.FromUserId == me && m.ToUserId == peerId) ||
                    (m.FromUserId == peerId && m.ToUserId == me));
            }

            var items = await query.OrderBy(m => m.SentAt).Take(400).ToListAsync();
            var response = new MessageListResponse
            {
                Items = items.Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    FromUserId = m.FromUserId.ToString("N"),
                    ToUserId = m.ToUserId.ToString("N"),
                    FromName = m.FromName,
                    Body = m.Body,
                    SentAt = m.SentAt
                }).ToList()
            };
            return Results.Json(response, ChatJson.Options);
        });

        app.MapPost(ChatRoutes.Messages, async (HttpContext http, ChatMessageDto body, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            var user = await ChatAuth.CurrentUserAsync(http, factory);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            body.FromUserId = user.Id.ToString("N");
            body.FromName = string.IsNullOrWhiteSpace(body.FromName) ? user.DisplayName : body.FromName;
            await hub.DeliverOrStoreAsync(body, http.RequestAborted);
            return Results.Json(body, ChatJson.Options);
        });

        app.Map(ChatRoutes.WebSocket, async (HttpContext http, IDbContextFactory<ChatServerDb> factory, ConnectionManager hub) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var token = http.Request.Query["token"].ToString();
            var user = await ChatAuth.UserFromTokenAsync(factory, token);
            if (user is null)
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            try
            {
                await hub.RunAsync(user, socket, http.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // istemci kapandı
            }
            catch (WebSocketException)
            {
                // kopuk hat
            }
        });
    }
}
