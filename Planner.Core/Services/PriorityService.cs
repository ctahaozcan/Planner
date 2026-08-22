using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class PriorityService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public PriorityService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<DayPriority>> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DayPriorities.AsNoTracking()
            .Where(p => p.Date == date)
            .OrderBy(p => p.Slot)
            .ToListAsync(ct);
    }

    public async Task<bool> PinAsync(DateOnly date, Guid taskId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.DayPriorities.AnyAsync(p => p.Date == date && p.TaskId == taskId, ct))
        {
            return true;
        }

        var count = await db.DayPriorities.CountAsync(p => p.Date == date, ct);
        if (count >= 3)
        {
            return false;
        }

        var used = await db.DayPriorities.Where(p => p.Date == date).Select(p => p.Slot).ToListAsync(ct);
        var slot = Enumerable.Range(1, 3).First(s => !used.Contains(s));
        db.DayPriorities.Add(new DayPriority
        {
            Id = Guid.NewGuid(),
            Date = date,
            TaskId = taskId,
            Slot = slot
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UnpinAsync(DateOnly date, Guid taskId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DayPriorities.FirstOrDefaultAsync(p => p.Date == date && p.TaskId == taskId, ct);
        if (row is null)
        {
            return;
        }

        db.DayPriorities.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
