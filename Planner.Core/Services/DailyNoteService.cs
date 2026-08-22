using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class DailyNoteService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public DailyNoteService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<string> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DailyNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Date == date, ct);
        return row?.Content ?? "";
    }

    public async Task SaveAsync(DateOnly date, string content, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DailyNotes.FirstOrDefaultAsync(n => n.Date == date, ct);
        if (row is null)
        {
            db.DailyNotes.Add(new DailyNote
            {
                Date = date,
                Content = content,
                UpdatedAt = DateTime.Now
            });
        }
        else
        {
            row.Content = content;
            row.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DailyNote>> SearchAsync(string query, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DailyNotes.AsNoTracking()
            .Where(n => n.Content.Contains(query))
            .OrderByDescending(n => n.Date)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DailyNote>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DailyNotes.AsNoTracking().OrderByDescending(n => n.Date).ToListAsync(ct);
    }
}
