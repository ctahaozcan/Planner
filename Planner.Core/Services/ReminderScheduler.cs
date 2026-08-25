using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class ReminderScheduler : IDisposable
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly IReminderNotifier _notifier;
    private readonly ITaskChangeSignal _signal;
    private readonly TaskService _tasks;
    private readonly HabitService _habits;
    private readonly SettingsService _settings;
    private readonly FocusTimerService _focus;
    private readonly BriefingService _briefing;
    private readonly TaskRolloverService _rollover;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ReminderScheduler(
        IDbContextFactory<PlannerDbContext> factory,
        IReminderNotifier notifier,
        ITaskChangeSignal signal,
        TaskService tasks,
        HabitService habits,
        SettingsService settings,
        FocusTimerService focus,
        BriefingService briefing,
        TaskRolloverService rollover)
    {
        _factory = factory;
        _notifier = notifier;
        _signal = signal;
        _tasks = tasks;
        _habits = habits;
        _settings = settings;
        _focus = focus;
        _briefing = briefing;
        _rollover = rollover;
        _signal.TasksChanged += OnTasksChanged;
    }

    public event Action<PlannerTask, DateOnly>? TaskReminderRaised;
    public event Action? EveningCloseRaised;
    public event Action<BriefingContent>? BriefingRaised;

    public void Start() => Restart();

    public void Restart()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => LoopAsync(token), token);
        }
    }

    private void OnTasksChanged() => Restart();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await FireDueAsync(ct);

                var next = await GetNextWakeAsync(ct);
                if (next is null)
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    continue;
                }

                var delay = next.Value - DateTime.Now;
                if (delay <= TimeSpan.Zero)
                {
                    continue;
                }

                if (delay > TimeSpan.FromHours(6))
                {
                    delay = TimeSpan.FromHours(6);
                }

                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task FireDueAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var quiet = await IsQuietAsync(now);

        await _rollover.ApplyAsync(ct);
        await FireTasksAsync(now, quiet, ct);
        await FireHabitsAsync(now, today, quiet, ct);
        await FireBriefingAsync(now, today, quiet, ct);
        await FireEveningAsync(now, today, quiet, ct);
        await FireFocusAsync(now, quiet);
        if (!quiet)
        {
            await FlushQueueAsync(ct);
        }
    }

    private async Task FireTasksAsync(DateTime now, bool quiet, CancellationToken ct)
    {
        var pending = await _tasks.GetPendingRemindersAsync(now, ct);
        foreach (var (task, at) in pending.Where(p => p.At <= now))
        {
            var occ = DateOnly.FromDateTime(at);
            if (quiet)
            {
                await EnqueueAsync(QueuedNotificationKind.TaskReminder, "Anımsatıcı · Yaver", task.Title, task.Id.ToString(), ct);
            }
            else
            {
                _notifier.ShowTaskReminder(task, occ);
                TaskReminderRaised?.Invoke(task, occ);
            }

            await _tasks.MarkReminderFiredAsync(task.Id, occ, ct);
        }
    }

    private async Task FireHabitsAsync(DateTime now, DateOnly today, bool quiet, CancellationToken ct)
    {
        foreach (var (habit, at) in await _habits.GetPendingRemindersAsync(now, ct))
        {
            if (at > now)
            {
                continue;
            }

            if (quiet)
            {
                await EnqueueAsync(QueuedNotificationKind.HabitReminder, "Alışkanlık", habit.Name, habit.Id.ToString(), ct);
            }
            else
            {
                _notifier.ShowHabitReminder(habit);
            }

            await _habits.MarkReminderFiredTodayAsync(habit.Id, today, ct);
        }
    }

    private async Task FireBriefingAsync(DateTime now, DateOnly today, bool quiet, CancellationToken ct)
    {
        if (!await _settings.GetBoolAsync(SettingKeys.MorningBriefingEnabled, true))
        {
            return;
        }

        var last = await _settings.GetDateAsync(SettingKeys.LastBriefingDate);
        if (last == today)
        {
            return;
        }

        var time = await _settings.GetTimeAsync(SettingKeys.MorningBriefingTime, new TimeOnly(8, 0));
        if (now < today.ToDateTime(time))
        {
            return;
        }

        var content = await _briefing.BuildAsync(today, ct);
        if (quiet)
        {
            await EnqueueAsync(QueuedNotificationKind.Briefing, "Günaydın özeti", content.ToastBody, null, ct);
        }
        else
        {
            _notifier.ShowBriefing("Günaydın · Yaver", content.ToastBody);
            BriefingRaised?.Invoke(content);
        }

        await _settings.SetDateAsync(SettingKeys.LastBriefingDate, today);
    }

    private async Task FireEveningAsync(DateTime now, DateOnly today, bool quiet, CancellationToken ct)
    {
        if (!await _settings.GetBoolAsync(SettingKeys.EveningCloseEnabled, true))
        {
            return;
        }

        var last = await _settings.GetDateAsync(SettingKeys.LastEveningCloseDate);
        if (last == today)
        {
            return;
        }

        var time = await _settings.GetTimeAsync(SettingKeys.EveningCloseTime, new TimeOnly(21, 0));
        if (now < today.ToDateTime(time))
        {
            return;
        }

        if (quiet)
        {
            await EnqueueAsync(QueuedNotificationKind.EveningClose, "Akşam kapanışı", "Günü gözden geçirin.", null, ct);
        }
        else
        {
            _notifier.ShowEveningClose("Akşam kapanışı", "Bitirdiklerinizi gözden geçirin, kalanları yarına taşıyın.");
            EveningCloseRaised?.Invoke();
        }

        await _settings.SetDateAsync(SettingKeys.LastEveningCloseDate, today);
    }

    private async Task FireFocusAsync(DateTime now, bool quiet)
    {
        if (!_focus.TryCompleteIfDue(now))
        {
            return;
        }

        var wasFocus = _focus.Phase == FocusPhase.Focus;
        var title = wasFocus ? "Odak bitti" : "Mola bitti";
        var body = wasFocus ? "Kısa bir mola zamanı." : "Yeni bir odak turuna başlayabilirsiniz.";
        _focus.CompleteDue();
        if (quiet)
        {
            await EnqueueAsync(QueuedNotificationKind.FocusEnded, title, body, null, CancellationToken.None);
        }
        else
        {
            _notifier.ShowFocusEnded(title, body);
        }
    }

    private async Task<DateTime?> GetNextWakeAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        DateTime? next = null;

        void Consider(DateTime? at)
        {
            if (at is { } t && t > now && (next is null || t < next))
            {
                next = t;
            }
        }

        foreach (var (_, at) in await _tasks.GetPendingRemindersAsync(now, ct))
        {
            Consider(at);
        }

        foreach (var (_, at) in await _habits.GetPendingRemindersAsync(now, ct))
        {
            Consider(at);
        }

        if (await _settings.GetBoolAsync(SettingKeys.MorningBriefingEnabled, true)
            && await _settings.GetDateAsync(SettingKeys.LastBriefingDate) != today)
        {
            var t = await _settings.GetTimeAsync(SettingKeys.MorningBriefingTime, new TimeOnly(8, 0));
            Consider(TimeSetting.NextAt(t, now));
        }

        if (await _settings.GetBoolAsync(SettingKeys.EveningCloseEnabled, true)
            && await _settings.GetDateAsync(SettingKeys.LastEveningCloseDate) != today)
        {
            var t = await _settings.GetTimeAsync(SettingKeys.EveningCloseTime, new TimeOnly(21, 0));
            Consider(TimeSetting.NextAt(t, now));
        }

        if (_focus.EndsAt is { } end)
        {
            Consider(end);
        }

        Consider(today.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var quietOn = await _settings.GetBoolAsync(SettingKeys.QuietHoursEnabled);
        if (quietOn)
        {
            var qs = await _settings.GetTimeAsync(SettingKeys.QuietHoursStart, new TimeOnly(23, 0));
            var qe = await _settings.GetTimeAsync(SettingKeys.QuietHoursEnd, new TimeOnly(7, 0));
            if (TimeSetting.InQuietHours(now, true, qs, qe))
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                if (await db.QueuedNotifications.AnyAsync(ct))
                {
                    Consider(TimeSetting.NextQuietEnd(now, qs, qe));
                }
            }
        }

        return next;
    }

    public async Task ShowBriefingIfNeededAsync(bool forceToast, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!await _settings.GetBoolAsync(SettingKeys.MorningBriefingEnabled, true))
        {
            return;
        }

        if (await _settings.GetDateAsync(SettingKeys.LastBriefingDate) == today)
        {
            return;
        }

        var content = await _briefing.BuildAsync(today, ct);
        if (forceToast)
        {
            _notifier.ShowBriefing("Günaydın · Yaver", content.ToastBody);
        }

        BriefingRaised?.Invoke(content);
        await _settings.SetDateAsync(SettingKeys.LastBriefingDate, today);
        Restart();
    }

    private async Task<bool> IsQuietAsync(DateTime now)
    {
        var on = await _settings.GetBoolAsync(SettingKeys.QuietHoursEnabled);
        var start = await _settings.GetTimeAsync(SettingKeys.QuietHoursStart, new TimeOnly(23, 0));
        var end = await _settings.GetTimeAsync(SettingKeys.QuietHoursEnd, new TimeOnly(7, 0));
        return TimeSetting.InQuietHours(now, on, start, end);
    }

    private async Task EnqueueAsync(QueuedNotificationKind kind, string title, string body, string? payload, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.QueuedNotifications.Add(new QueuedNotification
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Title = title,
            Body = body,
            Payload = payload,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task FlushQueueAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.QueuedNotifications.OrderBy(x => x.CreatedAt).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            _notifier.ShowInfo(row.Title, row.Body);
        }

        db.QueuedNotifications.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _signal.TasksChanged -= OnTasksChanged;
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
