using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class TaskRolloverService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly SettingsService _settings;
    private readonly ITaskChangeSignal _signal;

    public TaskRolloverService(
        IDbContextFactory<PlannerDbContext> factory,
        SettingsService settings,
        ITaskChangeSignal signal)
    {
        _factory = factory;
        _settings = settings;
        _signal = signal;
    }

    public event Action<int>? Applied;

    public async Task<int> ApplyAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (await _settings.GetDateAsync(SettingKeys.RolloverDate) == today)
        {
            return 0;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var overdue = await db.Tasks
            .Where(t => t.Date < today
                        && t.Status != PlannerTaskStatus.Tamamlandi
                        && (t.RecurrenceKind == RecurrenceKind.None || t.IsSeriesException))
            .ToListAsync(ct);

        foreach (var task in overdue)
        {
            task.Date = today;
            task.UpdatedAt = DateTime.Now;
            task.ReminderFired = false;
            if (task.Time is { } time)
            {
                task.ReminderAt = today.ToDateTime(time);
            }
        }

        await db.SaveChangesAsync(ct);
        await _settings.SetDateAsync(SettingKeys.RolloverDate, today);
        if (overdue.Count > 0)
        {
            _signal.NotifyChanged();
            Applied?.Invoke(overdue.Count);
        }

        return overdue.Count;
    }
}
