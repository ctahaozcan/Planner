using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;
using System.Text.Json;

namespace Planner.Core.Services;

public sealed class ChatStore
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public ChatStore(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public static string ConversationKey(string a, string b)
    {
        var x = a ?? "";
        var y = b ?? "";
        return string.CompareOrdinal(x, y) <= 0 ? $"{x}|{y}" : $"{y}|{x}";
    }

    public async Task<bool> SaveAsync(ChatMessage message, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.ChatMessages.AnyAsync(m => m.Id == message.Id, ct))
        {
            return false;
        }

        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ApplyEditAsync(Guid id, string body, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.Body = body;
        row.EditedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task ToggleThumbAsync(Guid id, string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
        {
            return;
        }

        var set = (row.Thumbs ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (!set.Add(key))
        {
            set.Remove(key);
        }

        row.Thumbs = string.Join(",", set);
        await db.SaveChangesAsync(ct);
    }

    public async Task ApplyHiddenIfNewAsync(ChatMessage message, CancellationToken ct = default)
    {
        if (!await SaveAsync(message, ct))
        {
            return;
        }

        try
        {
            if (CollabPayload.IsEdit(message.Body))
            {
                var signal = JsonSerializer.Deserialize<EditSignal>(message.Body[CollabPayload.Edit.Length..]);
                if (signal is not null && signal.Id != Guid.Empty)
                {
                    await ApplyEditAsync(signal.Id, signal.Body ?? "", ct);
                }
            }
            else if (CollabPayload.IsReact(message.Body))
            {
                var signal = JsonSerializer.Deserialize<ReactSignal>(message.Body[CollabPayload.React.Length..]);
                if (signal is not null && signal.Id != Guid.Empty)
                {
                    await ToggleThumbAsync(signal.Id, message.FromKey, ct);
                }
            }
        }
        catch
        {
            // bozuk sinyal
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetThreadAsync(string conversationKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationKey == conversationKey
                        && !m.Body.StartsWith(CollabPayload.Edit)
                        && !m.Body.StartsWith(CollabPayload.React))
            .OrderBy(m => m.SentAt)
            .Take(400)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> RecentAsync(string myKey, int take = 40, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ChatMessages.AsNoTracking()
            .Where(m => m.FromKey == myKey || m.ToKey == myKey)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);
    }
}
