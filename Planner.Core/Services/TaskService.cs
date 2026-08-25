using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class TaskChangeSignal : ITaskChangeSignal
{
    public event Action? TasksChanged;
    public event Action<string, string>? Info;

    public void NotifyChanged() => TasksChanged?.Invoke();
    public void NotifyInfo(string title, string body) => Info?.Invoke(title, body);
}

public sealed class TaskService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly ITaskChangeSignal _signal;
    private readonly UserAccountService _users;

    public TaskService(IDbContextFactory<PlannerDbContext> factory, ITaskChangeSignal signal, UserAccountService users)
    {
        _factory = factory;
        _signal = signal;
        _users = users;
    }

    public async Task<IReadOnlyList<PlannerTask>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
        => (await GetOccurrencesForDateAsync(date, ct)).Select(o => CloneForOccurrence(o)).ToList();

    public async Task<IReadOnlyList<TaskOccurrence>> GetOccurrencesForDateAsync(DateOnly date, CancellationToken ct = default)
        => await GetOccurrencesRangeAsync(date, date, ct);

    public async Task<IReadOnlyList<TaskOccurrence>> GetOccurrencesRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default, Guid? agendaOwnerId = null)
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
        tasks = FilterVisible(tasks, agendaOwnerId);

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
            .ThenBy(o => o.Task.SortOrder)
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
        var list = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Time)
            .ToListAsync(ct);
        return FilterVisible(list, null);
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
        var list = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.RecurrenceKind == RecurrenceKind.None
                        && t.Date < today
                        && t.Status != PlannerTaskStatus.Tamamlandi)
            .OrderBy(t => t.Date)
            .ToListAsync(ct);
        return FilterVisible(list, null);
    }

    public async Task<PlannerTask> AddAsync(PlannerTask task, CancellationToken ct = default)
    {
        task.Id = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
        task.CreatedAt = DateTime.Now;
        task.UpdatedAt = task.CreatedAt;
        task.ReminderFired = false;
        ApplyStatusTimestamps(task, task.Status);
        if (task.IsRecurring)
        {
            task.SeriesId ??= task.Id;
        }

        task.OwnerUserId ??= _users.Current?.Id;

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (task.SortOrder == 0)
        {
            var max = await db.Tasks
                .Where(t => t.Date == task.Date && t.Status == task.Status)
                .MaxAsync(t => (int?)t.SortOrder, ct) ?? -1;
            task.SortOrder = max + 1;
        }

        db.Tasks.Add(task);
        await OpenSpanAsync(db, task.Id, task.Status, task.CreatedAt, ct);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
        if (task.AssignedToUserId is Guid assigned && assigned != _users.Current?.Id)
        {
            _signal.NotifyInfo("Görev atandı", task.Title);
        }

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
        ApplyStatusTimestamps(existing, task.Status);
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
        existing.AssignedToUserId = task.AssignedToUserId;
        existing.AssignedByUserId = task.AssignedByUserId;
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
                await MarkOccurrenceAsync(db, existing.Id, occ, OccurrenceMarkKind.Completed, ct, DateTime.Now);
                await db.SaveChangesAsync(ct);
                _signal.NotifyChanged();
                return;
            }

            var mark = await db.RecurrenceExceptions.FirstOrDefaultAsync(
                x => x.SeriesId == existing.Id && x.Date == occ, ct);
            if (mark is not null)
            {
                db.RecurrenceExceptions.Remove(mark);
            }

            if (status != PlannerTaskStatus.Baslamadi || occ != existing.Date)
            {
                await SkipOccurrenceCoreAsync(db, existing, occ, ct);
                var clone = CloneEntity(existing);
                clone.Id = Guid.NewGuid();
                clone.RecurrenceKind = RecurrenceKind.None;
                clone.RecurrenceWeekdays = 0;
                clone.RecurrenceMonthDay = null;
                clone.RecurrenceEndDate = null;
                clone.SeriesId = existing.Id;
                clone.IsSeriesException = true;
                clone.Date = occ;
                clone.Status = status;
                ApplyStatusTimestamps(clone, status);
                clone.CreatedAt = DateTime.Now;
                clone.UpdatedAt = clone.CreatedAt;
                clone.ReminderFired = false;
                db.Tasks.Add(clone);
                await OpenSpanAsync(db, clone.Id, status, DateTime.Now, ct);
            }

            await db.SaveChangesAsync(ct);
            _signal.NotifyChanged();
            return;
        }

        ApplyStatusTimestamps(existing, status);
        existing.Status = status;
        existing.UpdatedAt = DateTime.Now;
        await TransitionSpanAsync(db, existing.Id, status, DateTime.Now, ct);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
        if (existing.AssignedToUserId is Guid to && to != _users.Current?.Id)
        {
            _signal.NotifyInfo("Görev durumu", $"{existing.Title} → {status}");
        }
        else if (existing.AssignedByUserId is Guid by && by != _users.Current?.Id)
        {
            _signal.NotifyInfo("Görev durumu", $"{existing.Title} → {status}");
        }
    }

    public async Task PersistBoardOrderAsync(
        DateOnly date,
        IReadOnlyList<(Guid Id, PlannerTaskStatus Status, int SortOrder)> rows,
        CancellationToken ct = default)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var ids = rows.Select(r => r.Id).ToHashSet();
        var tasks = await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var map = tasks.ToDictionary(t => t.Id);
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.Id, out var task))
            {
                continue;
            }

            if (!task.IsRecurring)
            {
                if (task.Status != row.Status)
                {
                    await TransitionSpanAsync(db, task.Id, row.Status, DateTime.Now, ct);
                }

                ApplyStatusTimestamps(task, row.Status);
                task.Status = row.Status;
            }

            task.SortOrder = row.SortOrder;
            task.UpdatedAt = DateTime.Now;
        }

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

    public async Task<IReadOnlyList<CompletedWorkItem>> GetCompletedHistoryAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt = to.ToDateTime(new TimeOnly(23, 59, 59));
        var tasks = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Status == PlannerTaskStatus.Tamamlandi)
            .ToListAsync(ct);

        var list = new List<CompletedWorkItem>();
        foreach (var task in tasks)
        {
            var done = task.CompletedAt ?? task.UpdatedAt;
            if (done < fromDt || done > toDt)
            {
                continue;
            }

            var start = task.StartedAt ?? task.CreatedAt;
            list.Add(new CompletedWorkItem
            {
                TaskId = task.Id,
                Title = task.Title,
                CategoryName = task.Category?.Name ?? "",
                CategoryColor = task.Category?.ColorHex ?? "#0F766E",
                CompletedAt = done,
                StartedAt = start,
                OccurrenceDate = task.Date,
                DurationText = "",
                IsRecurringOccurrence = false
            });
        }

        var marks = await db.RecurrenceExceptions.AsNoTracking()
            .Where(x => x.Kind == OccurrenceMarkKind.Completed && x.Date >= from && x.Date <= to)
            .ToListAsync(ct);
        if (marks.Count > 0)
        {
            var seriesIds = marks.Select(m => m.SeriesId).Distinct().ToList();
            var series = await db.Tasks.AsNoTracking()
                .Include(t => t.Category)
                .Where(t => seriesIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);
            foreach (var mark in marks)
            {
                if (!series.TryGetValue(mark.SeriesId, out var task))
                {
                    continue;
                }

                var done = mark.CompletedAt ?? mark.Date.ToDateTime(task.Time ?? TimeOnly.MinValue);
                var start = task.Time is { } tm
                    ? mark.Date.ToDateTime(tm)
                    : mark.Date.ToDateTime(TimeOnly.MinValue);
                if (done < start)
                {
                    start = done;
                }

                list.Add(new CompletedWorkItem
                {
                    TaskId = task.Id,
                    Title = task.Title,
                    CategoryName = task.Category?.Name ?? "",
                    CategoryColor = task.Category?.ColorHex ?? "#0F766E",
                    CompletedAt = done,
                    StartedAt = start,
                    OccurrenceDate = mark.Date,
                    DurationText = "",
                    IsRecurringOccurrence = true
                });
            }
        }

        var ids = list.Select(x => x.TaskId).Distinct().ToList();
        var spans = ids.Count == 0
            ? []
            : await db.TaskStatusSpans.AsNoTracking()
                .Where(s => ids.Contains(s.TaskId) && s.Status == PlannerTaskStatus.DevamEdiyor)
                .ToListAsync(ct);
        var byTask = spans.GroupBy(s => s.TaskId).ToDictionary(g => g.Key, g => g.ToList());
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var duration = SumInProgress(byTask.GetValueOrDefault(item.TaskId), closedOnly: true);
            if (duration <= TimeSpan.Zero && item.CompletedAt > item.StartedAt)
            {
                duration = item.CompletedAt - item.StartedAt;
            }

            list[i] = item with { DurationText = DurationText.Format(duration) };
        }

        return list
            .OrderByDescending(x => x.CompletedAt)
            .ThenBy(x => x.Title)
            .ToList();
    }

    private static void ApplyStatusTimestamps(PlannerTask task, PlannerTaskStatus newStatus)
    {
        var now = DateTime.Now;
        if (newStatus == PlannerTaskStatus.DevamEdiyor && task.StartedAt is null)
        {
            task.StartedAt = now;
        }

        if (newStatus == PlannerTaskStatus.Tamamlandi)
        {
            task.CompletedAt ??= now;
        }
        else if (task.Status == PlannerTaskStatus.Tamamlandi)
        {
            task.CompletedAt = null;
        }
    }

    public async Task BackfillStatusSpansAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.TaskStatusSpans.AnyAsync(ct))
        {
            return;
        }

        var tasks = await db.Tasks.AsNoTracking().Select(t => new
        {
            t.Id,
            t.Status,
            t.CreatedAt,
            t.StartedAt,
            t.CompletedAt,
            t.UpdatedAt
        }).ToListAsync(ct);
        foreach (var task in tasks)
        {
            if (task.Status == PlannerTaskStatus.DevamEdiyor)
            {
                db.TaskStatusSpans.Add(new TaskStatusSpan
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    Status = PlannerTaskStatus.DevamEdiyor,
                    StartedAt = task.StartedAt ?? task.CreatedAt,
                    EndedAt = null
                });
            }
            else if (task.Status == PlannerTaskStatus.Tamamlandi && (task.StartedAt ?? task.CreatedAt) < (task.CompletedAt ?? task.UpdatedAt))
            {
                db.TaskStatusSpans.Add(new TaskStatusSpan
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    Status = PlannerTaskStatus.DevamEdiyor,
                    StartedAt = task.StartedAt ?? task.CreatedAt,
                    EndedAt = task.CompletedAt ?? task.UpdatedAt
                });
            }
            else
            {
                db.TaskStatusSpans.Add(new TaskStatusSpan
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    Status = task.Status,
                    StartedAt = task.CreatedAt,
                    EndedAt = task.Status == PlannerTaskStatus.Baslamadi ? null : task.UpdatedAt
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MonthlyWorkStats>> GetMonthlyStatsAsync(int months = 12, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var culture = new System.Globalization.CultureInfo("tr-TR");
        var today = DateTime.Today;
        var cursor = new DateTime(today.Year, today.Month, 1);
        var start = cursor.AddMonths(1 - months);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.CreatedAt >= start || (t.CompletedAt != null && t.CompletedAt >= start))
            .Select(t => new { t.Id, t.CreatedAt, t.CompletedAt, t.Status })
            .ToListAsync(ct);
        var marks = await db.RecurrenceExceptions.AsNoTracking()
            .Where(x => x.Kind == OccurrenceMarkKind.Completed && x.CompletedAt != null && x.CompletedAt >= start)
            .Select(x => x.CompletedAt)
            .ToListAsync(ct);
        var spans = await db.TaskStatusSpans.AsNoTracking()
            .Where(s => s.Status == PlannerTaskStatus.DevamEdiyor)
            .ToListAsync(ct);
        var byTask = spans.GroupBy(s => s.TaskId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<MonthlyWorkStats>();
        for (var i = 0; i < months; i++)
        {
            var monthStart = cursor.AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1);
            var created = tasks.Count(t => t.CreatedAt >= monthStart && t.CreatedAt < monthEnd);
            var completed = tasks.Where(t => t.Status == PlannerTaskStatus.Tamamlandi
                                             && t.CompletedAt is { } done
                                             && done >= monthStart && done < monthEnd)
                .ToList();
            var recurringDone = marks.Count(d => d is { } at && at >= monthStart && at < monthEnd);
            var durations = completed
                .Select(t => SumInProgress(byTask.GetValueOrDefault(t.Id), closedOnly: true))
                .Where(d => d > TimeSpan.Zero)
                .ToList();
            var avg = durations.Count == 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks((long)durations.Average(d => d.Ticks));
            result.Add(new MonthlyWorkStats
            {
                Year = monthStart.Year,
                Month = monthStart.Month,
                MonthLabel = monthStart.ToString("MMMM yyyy", culture),
                CreatedCount = created,
                CompletedCount = completed.Count + recurringDone,
                AverageInProgress = avg,
                AverageText = durations.Count == 0 ? "—" : DurationText.Format(avg)
            });
        }

        return result;
    }

    private static async Task OpenSpanAsync(
        PlannerDbContext db,
        Guid taskId,
        PlannerTaskStatus status,
        DateTime startedAt,
        CancellationToken ct)
    {
        db.TaskStatusSpans.Add(new TaskStatusSpan
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Status = status,
            StartedAt = startedAt,
            EndedAt = null
        });
        await Task.CompletedTask;
    }

    private static async Task TransitionSpanAsync(
        PlannerDbContext db,
        Guid taskId,
        PlannerTaskStatus newStatus,
        DateTime now,
        CancellationToken ct)
    {
        var open = await db.TaskStatusSpans
            .Where(s => s.TaskId == taskId && s.EndedAt == null)
            .ToListAsync(ct);
        foreach (var span in open)
        {
            span.EndedAt = now;
        }

        db.TaskStatusSpans.Add(new TaskStatusSpan
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Status = newStatus,
            StartedAt = now,
            EndedAt = null
        });
    }

    private static TimeSpan SumInProgress(List<TaskStatusSpan>? spans, bool closedOnly)
    {
        if (spans is null || spans.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        var now = DateTime.Now;
        foreach (var span in spans)
        {
            if (closedOnly && span.EndedAt is null)
            {
                continue;
            }

            var end = span.EndedAt ?? now;
            if (end > span.StartedAt)
            {
                total += end - span.StartedAt;
            }
        }

        return total;
    }

    private static async Task MarkOccurrenceAsync(
        PlannerDbContext db,
        Guid seriesId,
        DateOnly date,
        OccurrenceMarkKind kind,
        CancellationToken ct,
        DateTime? completedAt = null)
    {
        var row = await db.RecurrenceExceptions.FirstOrDefaultAsync(x => x.SeriesId == seriesId && x.Date == date, ct);
        if (row is null)
        {
            db.RecurrenceExceptions.Add(new RecurrenceException
            {
                Id = Guid.NewGuid(),
                SeriesId = seriesId,
                Date = date,
                Kind = kind,
                CompletedAt = kind == OccurrenceMarkKind.Completed ? completedAt ?? DateTime.Now : null
            });
        }
        else
        {
            row.Kind = kind;
            if (kind == OccurrenceMarkKind.Completed)
            {
                row.CompletedAt ??= completedAt ?? DateTime.Now;
            }
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
            SortOrder = t.SortOrder,
            CreatedAt = t.CreatedAt,
            StartedAt = t.StartedAt,
            CompletedAt = t.CompletedAt,
            UpdatedAt = t.UpdatedAt,
            IsQuickAdd = t.IsQuickAdd,
            RecurrenceKind = t.RecurrenceKind,
            RecurrenceWeekdays = t.RecurrenceWeekdays,
            RecurrenceMonthDay = t.RecurrenceMonthDay,
            RecurrenceEndDate = t.RecurrenceEndDate,
            SeriesId = t.SeriesId ?? t.Id,
            IsSeriesException = t.IsSeriesException,
            LinkedContactId = t.LinkedContactId,
            OwnerUserId = t.OwnerUserId,
            AssignedToUserId = t.AssignedToUserId,
            AssignedByUserId = t.AssignedByUserId
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
        StartedAt = t.StartedAt,
        CompletedAt = t.CompletedAt,
        IsQuickAdd = t.IsQuickAdd,
        RecurrenceKind = t.RecurrenceKind,
        RecurrenceWeekdays = t.RecurrenceWeekdays,
        RecurrenceMonthDay = t.RecurrenceMonthDay,
        RecurrenceEndDate = t.RecurrenceEndDate,
        SeriesId = t.SeriesId,
        IsSeriesException = t.IsSeriesException,
        LinkedContactId = t.LinkedContactId,
        OwnerUserId = t.OwnerUserId,
        AssignedToUserId = t.AssignedToUserId,
        AssignedByUserId = t.AssignedByUserId,
        ServerWorkTaskId = t.ServerWorkTaskId,
        AssignedByName = t.AssignedByName,
        SortOrder = t.SortOrder
    };

    public async Task UpsertWorkAssignmentAsync(
        Guid serverTaskId,
        string title,
        string? notes,
        DateOnly date,
        TimeOnly? time,
        string assignedByName,
        Guid? assignedByServerId,
        CancellationToken ct = default)
    {
        var workCategory = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Tasks.FirstOrDefaultAsync(t => t.ServerWorkTaskId == serverTaskId, ct);
        var reminder = time is { } clock
            ? date.ToDateTime(clock)
            : date.ToDateTime(new TimeOnly(9, 0));
        if (row is null)
        {
            row = new PlannerTask
            {
                Id = Guid.NewGuid(),
                Title = title,
                Notes = notes,
                CategoryId = workCategory,
                Date = date,
                Time = time,
                ReminderAt = reminder,
                ReminderFired = reminder <= DateTime.Now,
                Status = PlannerTaskStatus.Baslamadi,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                OwnerUserId = _users.Current?.Id,
                AssignedToUserId = _users.Current?.Id,
                AssignedByUserId = assignedByServerId,
                ServerWorkTaskId = serverTaskId,
                AssignedByName = assignedByName
            };
            db.Tasks.Add(row);
            await OpenSpanAsync(db, row.Id, row.Status, row.CreatedAt, ct);
        }
        else
        {
            row.Title = title;
            row.Notes = notes;
            row.Date = date;
            row.Time = time;
            row.AssignedByName = assignedByName;
            row.UpdatedAt = DateTime.Now;
            if (row.ReminderAt is null)
            {
                row.ReminderAt = reminder;
            }
        }

        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    private List<PlannerTask> FilterVisible(List<PlannerTask> tasks, Guid? agendaOwnerId)
    {
        if (agendaOwnerId is Guid owner)
        {
            return tasks.Where(t => t.OwnerUserId == owner).ToList();
        }

        var me = _users.Current?.Id;
        if (me is null)
        {
            return tasks;
        }

        return tasks.Where(t => t.OwnerUserId is null || t.OwnerUserId == me || t.AssignedToUserId == me).ToList();
    }
}
