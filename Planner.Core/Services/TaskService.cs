using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class TaskChangeSignal : ITaskChangeSignal
{
    public event Action? TasksChanged;

    public void NotifyChanged() => TasksChanged?.Invoke();
}

public sealed class TaskService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly ITaskChangeSignal _signal;

    public TaskService(IDbContextFactory<PlannerDbContext> factory, ITaskChangeSignal signal)
    {
        _factory = factory;
        _signal = signal;
    }

    public async Task<IReadOnlyList<PlannerTask>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
        => (await GetOccurrencesForDateAsync(date, ct)).Select(o => CloneForOccurrence(o)).ToList();

    public async Task<IReadOnlyList<TaskOccurrence>> GetOccurrencesForDateAsync(DateOnly date, CancellationToken ct = default)
        => await GetOccurrencesRangeAsync(date, date, ct);

    public async Task<IReadOnlyList<TaskOccurrence>> GetOccurrencesRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var tasks = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.IsSeriesException
                        ? t.Date >= from && t.Date <= to
                        : t.RecurrenceKind == RecurrenceKind.None
                            ? t.Date >= from && t.Date <= to
                            : t.Date <= to && (t.RecurrenceEndDate == null || t.RecurrenceEndDate >= from))
            .ToListAsync(ct);

        var seriesIds = tasks.Where(t => t.IsRecurring).Select(t => t.Id).ToList();
        var marks = seriesIds.Count == 0
            ? []
            : await db.RecurrenceExceptions.AsNoTracking()
                .Where(x => seriesIds.Contains(x.SeriesId) && x.Date >= from && x.Date <= to)
                .ToListAsync(ct);
        var markLookup = marks.ToLookup(m => (m.SeriesId, m.Date));

        var list = new List<TaskOccurrence>();
        foreach (var task in tasks)
        {
            foreach (var date in RecurrenceExpander.Enumerate(task, from, to))
            {
                var mark = markLookup[(task.EffectiveSeriesId, date)].FirstOrDefault();
                if (mark?.Kind == OccurrenceMarkKind.Skipped)
                {
                    continue;
                }

                list.Add(new TaskOccurrence
                {
                    Task = task,
                    Date = date,
                    IsVirtual = task.IsRecurring && (date != task.Date || mark is not null),
                    IsCompletedOccurrence = mark?.Kind == OccurrenceMarkKind.Completed
                        || (!task.IsRecurring && task.Status == PlannerTaskStatus.Tamamlandi)
                });
            }
        }

        return list
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Task.Time == null)
            .ThenBy(o => o.Task.Time)
            .ThenBy(o => o.Task.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<PlannerTask>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => (await GetOccurrencesRangeAsync(from, to, ct)).Select(CloneForOccurrence).ToList();

    public async Task<IReadOnlyList<PlannerTask>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Time)
            .ToListAsync(ct);
    }

    public async Task<PlannerTask?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Tasks.AsNoTracking().Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<int> CountForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var items = await GetOccurrencesForDateAsync(date, ct);
        return items.Count(o => o.Status != PlannerTaskStatus.Tamamlandi);
    }

    public async Task<IReadOnlyList<PlannerTask>> GetOverdueAsync(DateOnly today, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.RecurrenceKind == RecurrenceKind.None
                        && t.Date < today
                        && t.Status != PlannerTaskStatus.Tamamlandi)
            .OrderBy(t => t.Date)
            .ToListAsync(ct);
    }

    public async Task<PlannerTask> AddAsync(PlannerTask task, CancellationToken ct = default)
    {
        task.Id = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
        task.CreatedAt = DateTime.Now;
        task.UpdatedAt = task.CreatedAt;
        task.ReminderFired = false;
        if (task.IsRecurring)
        {
            task.SeriesId ??= task.Id;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
        return task;
    }

    public async Task UpdateAsync(PlannerTask task, bool seriesEdit = true, DateOnly? occurrenceDate = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == task.Id, ct)
                       ?? throw new InvalidOperationException("Görev bulunamadı.");

        if (existing.IsRecurring && !seriesEdit && occurrenceDate is { } occ)
        {
            await SkipOccurrenceCoreAsync(db, existing, occ, ct);
            var clone = CloneEntity(task);
            clone.Id = Guid.NewGuid();
            clone.RecurrenceKind = RecurrenceKind.None;
            clone.RecurrenceWeekdays = 0;
            clone.RecurrenceMonthDay = null;
            clone.RecurrenceEndDate = null;
            clone.SeriesId = existing.Id;
            clone.IsSeriesException = true;
            clone.Date = occ;
            clone.CreatedAt = DateTime.Now;
            clone.UpdatedAt = clone.CreatedAt;
            clone.ReminderFired = false;
            db.Tasks.Add(clone);
            await db.SaveChangesAsync(ct);
            _signal.NotifyChanged();
            return;
        }

        var reminderChanged = existing.ReminderAt != task.ReminderAt;
        existing.Title = task.Title;
        existing.Notes = task.Notes;
        existing.CategoryId = task.CategoryId;
        existing.Date = task.Date;
        existing.Time = task.Time;
        existing.ReminderAt = task.ReminderAt;
        existing.Status = task.Status;
        existing.UpdatedAt = DateTime.Now;
        existing.IsQuickAdd = task.IsQuickAdd;
        existing.RecurrenceKind = task.RecurrenceKind;
        existing.RecurrenceWeekdays = task.RecurrenceWeekdays;
        existing.RecurrenceMonthDay = task.RecurrenceMonthDay;
        existing.RecurrenceEndDate = task.RecurrenceEndDate;
        existing.SeriesId = task.IsRecurring ? (task.SeriesId ?? existing.Id) : null;
        existing.IsSeriesException = task.IsSeriesException;
        existing.LinkedContactId = task.LinkedContactId;
        if (reminderChanged)
        {
            existing.ReminderFired = false;
        }

        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task SetStatusAsync(Guid id, PlannerTaskStatus status, DateOnly? occurrenceDate = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        if (existing.IsRecurring && occurrenceDate is { } occ)
        {
            if (status == PlannerTaskStatus.Tamamlandi)
            {
                await MarkOccurrenceAsync(db, existing.Id, occ, OccurrenceMarkKind.Completed, ct);
            }
            else
            {
                var row = await db.RecurrenceExceptions.FirstOrDefaultAsync(
                    x => x.SeriesId == existing.Id && x.Date == occ, ct);
                if (row is not null)
                {
                    db.RecurrenceExceptions.Remove(row);
                }
            }

            await db.SaveChangesAsync(ct);
            _signal.NotifyChanged();
            return;
        }

        existing.Status = status;
        existing.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task SkipOccurrenceAsync(Guid seriesId, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == seriesId, ct);
        if (existing is null)
        {
            return;
        }

        await SkipOccurrenceCoreAsync(db, existing, date, ct);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task SnoozeAsync(Guid id, DateTime until, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        existing.ReminderAt = until;
        existing.ReminderFired = false;
        existing.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task SetTimeAsync(Guid id, TimeOnly? time, DateOnly? occurrenceDate = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        if (existing.IsRecurring && occurrenceDate is { } occ && occ != existing.Date)
        {
            await SkipOccurrenceCoreAsync(db, existing, occ, ct);
            var clone = CloneEntity(existing);
            clone.Id = Guid.NewGuid();
            clone.RecurrenceKind = RecurrenceKind.None;
            clone.SeriesId = existing.Id;
            clone.IsSeriesException = true;
            clone.Date = occ;
            clone.Time = time;
            clone.CreatedAt = DateTime.Now;
            clone.UpdatedAt = clone.CreatedAt;
            clone.ReminderFired = false;
            if (time is { } tm)
            {
                clone.ReminderAt = occ.ToDateTime(tm);
            }

            db.Tasks.Add(clone);
        }
        else
        {
            existing.Time = time;
            existing.UpdatedAt = DateTime.Now;
            existing.ReminderFired = false;
            if (time is { } tm)
            {
                existing.ReminderAt = existing.Date.ToDateTime(tm);
            }
        }

        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task MoveToDateAsync(Guid id, DateOnly date, DateOnly? occurrenceDate = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        if (existing.IsRecurring && occurrenceDate is { } occ)
        {
            await SkipOccurrenceCoreAsync(db, existing, occ, ct);
            var clone = CloneEntity(existing);
            clone.Id = Guid.NewGuid();
            clone.RecurrenceKind = RecurrenceKind.None;
            clone.SeriesId = existing.Id;
            clone.IsSeriesException = true;
            clone.Date = date;
            clone.Status = PlannerTaskStatus.Baslamadi;
            clone.CreatedAt = DateTime.Now;
            clone.UpdatedAt = clone.CreatedAt;
            clone.ReminderFired = false;
            db.Tasks.Add(clone);
        }
        else
        {
            existing.Date = date;
            existing.UpdatedAt = DateTime.Now;
            existing.ReminderFired = false;
            if (existing.Time is { } tm)
            {
                existing.ReminderAt = date.ToDateTime(tm);
            }
        }

        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task DeleteAsync(Guid id, bool entireSeries = true, DateOnly? occurrenceDate = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        if (existing.IsRecurring && !entireSeries && occurrenceDate is { } occ)
        {
            await SkipOccurrenceCoreAsync(db, existing, occ, ct);
            await db.SaveChangesAsync(ct);
            _signal.NotifyChanged();
            return;
        }

        var attachments = await db.TaskAttachments.Where(a => a.TaskId == id).ToListAsync(ct);
        db.TaskAttachments.RemoveRange(attachments);
        if (existing.IsRecurring)
        {
            var marks = await db.RecurrenceExceptions.Where(x => x.SeriesId == existing.Id).ToListAsync(ct);
            db.RecurrenceExceptions.RemoveRange(marks);
        }

        db.Tasks.Remove(existing);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task ReassignCategoryAsync(Guid fromCategoryId, Guid toCategoryId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var items = await db.Tasks.Where(t => t.CategoryId == fromCategoryId).ToListAsync(ct);
        foreach (var item in items)
        {
            item.CategoryId = toCategoryId;
        }

        await db.SaveChangesAsync(ct);
        if (items.Count > 0)
        {
            _signal.NotifyChanged();
        }
    }

    public async Task<IReadOnlyList<(PlannerTask Task, DateTime At)>> GetPendingRemindersAsync(DateTime now, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var tasks = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Status != PlannerTaskStatus.Tamamlandi)
            .ToListAsync(ct);

        var seriesIds = tasks.Where(t => t.IsRecurring).Select(t => t.Id).ToList();
        var marks = seriesIds.Count == 0
            ? []
            : await db.RecurrenceExceptions.AsNoTracking()
                .Where(x => seriesIds.Contains(x.SeriesId))
                .ToListAsync(ct);
        var skip = marks.Where(m => m.Kind == OccurrenceMarkKind.Skipped || m.Kind == OccurrenceMarkKind.Completed)
            .ToLookup(m => m.SeriesId, m => m.Date);

        var list = new List<(PlannerTask, DateTime)>();
        var today = DateOnly.FromDateTime(now);
        foreach (var task in tasks)
        {
            if (task.IsRecurring)
            {
                var skipped = skip[task.Id].ToHashSet();
                var nextDate = RecurrenceExpander.NextOnOrAfter(task, today, skipped);
                if (nextDate is null)
                {
                    continue;
                }

                var time = task.Time ?? (task.ReminderAt is { } r ? TimeOnly.FromDateTime(r) : (TimeOnly?)null);
                if (time is null)
                {
                    continue;
                }

                var at = nextDate.Value.ToDateTime(time.Value);
                if (at > now || (at <= now && !task.ReminderFired && nextDate == today))
                {
                    list.Add((task, at));
                }

                continue;
            }

            if (task.ReminderAt is { } reminder && !task.ReminderFired)
            {
                list.Add((task, reminder));
            }
        }

        return list.OrderBy(x => x.Item2).ToList();
    }

    public async Task MarkReminderFiredAsync(Guid id, DateOnly? occurrenceDate, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        existing.ReminderFired = true;
        existing.UpdatedAt = DateTime.Now;
        if (existing.IsRecurring && occurrenceDate is { } occ)
        {
            var skip = (await db.RecurrenceExceptions.Where(x => x.SeriesId == existing.Id).Select(x => x.Date).ToListAsync(ct)).ToHashSet();
            skip.Add(occ);
            var next = RecurrenceExpander.NextOccurrence(existing, occ, skip);
            var time = existing.Time ?? (existing.ReminderAt is { } r ? TimeOnly.FromDateTime(r) : (TimeOnly?)null);
            if (next is { } nd && time is { } tm)
            {
                existing.ReminderAt = nd.ToDateTime(tm);
                existing.ReminderFired = false;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task MarkOccurrenceAsync(
        PlannerDbContext db,
        Guid seriesId,
        DateOnly date,
        OccurrenceMarkKind kind,
        CancellationToken ct)
    {
        var row = await db.RecurrenceExceptions.FirstOrDefaultAsync(x => x.SeriesId == seriesId && x.Date == date, ct);
        if (row is null)
        {
            db.RecurrenceExceptions.Add(new RecurrenceException
            {
                Id = Guid.NewGuid(),
                SeriesId = seriesId,
                Date = date,
                Kind = kind
            });
        }
        else
        {
            row.Kind = kind;
        }
    }

    private static async Task SkipOccurrenceCoreAsync(PlannerDbContext db, PlannerTask existing, DateOnly date, CancellationToken ct)
        => await MarkOccurrenceAsync(db, existing.Id, date, OccurrenceMarkKind.Skipped, ct);

    private static PlannerTask CloneForOccurrence(TaskOccurrence occurrence)
    {
        var t = occurrence.Task;
        return new PlannerTask
        {
            Id = t.Id,
            Title = t.Title,
            Notes = t.Notes,
            CategoryId = t.CategoryId,
            Category = t.Category,
            Date = occurrence.Date,
            Time = t.Time,
            ReminderAt = occurrence.ReminderAtForOccurrence,
            ReminderFired = t.ReminderFired,
            Status = occurrence.Status,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            IsQuickAdd = t.IsQuickAdd,
            RecurrenceKind = t.RecurrenceKind,
            RecurrenceWeekdays = t.RecurrenceWeekdays,
            RecurrenceMonthDay = t.RecurrenceMonthDay,
            RecurrenceEndDate = t.RecurrenceEndDate,
            SeriesId = t.SeriesId ?? t.Id,
            IsSeriesException = t.IsSeriesException,
            LinkedContactId = t.LinkedContactId
        };
    }

    private static PlannerTask CloneEntity(PlannerTask t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Notes = t.Notes,
        CategoryId = t.CategoryId,
        Date = t.Date,
        Time = t.Time,
        ReminderAt = t.ReminderAt,
        ReminderFired = false,
        Status = t.Status,
        IsQuickAdd = t.IsQuickAdd,
        RecurrenceKind = t.RecurrenceKind,
        RecurrenceWeekdays = t.RecurrenceWeekdays,
        RecurrenceMonthDay = t.RecurrenceMonthDay,
        RecurrenceEndDate = t.RecurrenceEndDate,
        SeriesId = t.SeriesId,
        IsSeriesException = t.IsSeriesException,
        LinkedContactId = t.LinkedContactId
    };
}
