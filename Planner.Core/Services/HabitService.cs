using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class HabitService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly ITaskChangeSignal _signal;

    public HabitService(IDbContextFactory<PlannerDbContext> factory, ITaskChangeSignal signal)
    {
        _factory = factory;
        _signal = signal;
    }

    public async Task<IReadOnlyList<Habit>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Habits.AsNoTracking().OrderBy(h => h.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HabitSnapshot>> GetSnapshotsAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var habits = await db.Habits.AsNoTracking().OrderBy(h => h.CreatedAt).ToListAsync(ct);
        var logs = await db.HabitLogs.AsNoTracking().ToListAsync(ct);
        var byHabit = logs.ToLookup(l => l.HabitId);
        return habits.Select(h => new HabitSnapshot
        {
            Habit = h,
            IsDueToday = IsDue(h, date),
            IsCompletedToday = byHabit[h.Id].Any(l => l.Date == date),
            Streak = ComputeStreak(h, date, byHabit[h.Id].Select(l => l.Date).ToHashSet())
        }).ToList();
    }

    public async Task<Habit> AddAsync(string name, HabitScheduleKind schedule, TimeOnly? reminder, CancellationToken ct = default)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Alışkanlık adı boş olamaz.");
        }

        var habit = new Habit
        {
            Id = Guid.NewGuid(),
            Name = name,
            ScheduleKind = schedule,
            ReminderTime = reminder,
            ReminderEnabled = reminder is not null,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Habits.Add(habit);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
        return habit;
    }

    public async Task UpdateAsync(Habit habit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Habits.FirstOrDefaultAsync(h => h.Id == habit.Id, ct)
                       ?? throw new InvalidOperationException("Alışkanlık bulunamadı.");
        existing.Name = habit.Name.Trim();
        existing.ScheduleKind = habit.ScheduleKind;
        existing.ReminderTime = habit.ReminderTime;
        existing.ReminderEnabled = habit.ReminderEnabled && habit.ReminderTime is not null;
        existing.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Habits.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        db.HabitLogs.RemoveRange(await db.HabitLogs.Where(l => l.HabitId == id).ToListAsync(ct));
        db.Habits.Remove(existing);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task ToggleTodayAsync(Guid habitId, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var log = await db.HabitLogs.FirstOrDefaultAsync(l => l.HabitId == habitId && l.Date == date, ct);
        if (log is null)
        {
            db.HabitLogs.Add(new HabitLog
            {
                Id = Guid.NewGuid(),
                HabitId = habitId,
                Date = date,
                CompletedAt = DateTime.Now
            });
        }
        else
        {
            db.HabitLogs.Remove(log);
        }

        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task MarkReminderFiredTodayAsync(Guid habitId, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var key = $"HabitReminded:{habitId:N}:{date:yyyyMMdd}";
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = "true" });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> WasRemindedAsync(Guid habitId, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var key = $"HabitReminded:{habitId:N}:{date:yyyyMMdd}";
        return await db.Settings.AnyAsync(s => s.Key == key, ct);
    }

    public async Task<IReadOnlyList<(Habit Habit, DateTime At)>> GetPendingRemindersAsync(DateTime now, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(now);
        var snapshots = await GetSnapshotsAsync(today, ct);
        var list = new List<(Habit, DateTime)>();
        foreach (var snap in snapshots.Where(s => s.IsDueToday && !s.IsCompletedToday && s.Habit.ReminderEnabled && s.Habit.ReminderTime is not null))
        {
            if (await WasRemindedAsync(snap.Habit.Id, today, ct))
            {
                continue;
            }

            var at = today.ToDateTime(snap.Habit.ReminderTime!.Value);
            list.Add((snap.Habit, at));
        }

        return list;
    }

    public static bool IsDue(Habit habit, DateOnly date)
        => habit.ScheduleKind == HabitScheduleKind.Daily
           || WeekdayBits.Includes(WeekdayBits.Weekdays, date.DayOfWeek);

    private static int ComputeStreak(Habit habit, DateOnly today, HashSet<DateOnly> done)
    {
        var streak = 0;
        var d = today;
        if (!done.Contains(today))
        {
            d = PreviousDue(habit, today);
        }

        while (IsDue(habit, d) && done.Contains(d))
        {
            streak++;
            var prev = PreviousDue(habit, d);
            if (prev == d)
            {
                break;
            }

            d = prev;
            if (streak > 4000)
            {
                break;
            }
        }

        return streak;
    }

    private static DateOnly PreviousDue(Habit habit, DateOnly date)
    {
        for (var i = 1; i <= 8; i++)
        {
            var d = date.AddDays(-i);
            if (IsDue(habit, d))
            {
                return d;
            }
        }

        return date.AddDays(-1);
    }
}
