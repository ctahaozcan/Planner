using Microsoft.Toolkit.Uwp.Notifications;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Services;

public sealed class ToastNotificationService : IReminderNotifier
{
    public const string AppUserModelId = "Tahat.Yaver";

    public void ShowTaskReminder(PlannerTask task, DateOnly occurrenceDate)
    {
        var when = task.Time is { } time
            ? $"{occurrenceDate:dd MMMM} · {time:HH\\:mm}"
            : occurrenceDate.ToString("dd MMMM");

        new ToastContentBuilder()
            .AddArgument("action", "openTask")
            .AddArgument("taskId", task.Id.ToString())
            .AddArgument("date", occurrenceDate.ToString("yyyy-MM-dd"))
            .AddText("Anımsatıcı · Yaver")
            .AddText(task.Title)
            .AddText($"{task.Category.Name} · {when}")
            .AddButton(new ToastButton()
                .SetContent("10 dk")
                .AddArgument("action", "snooze")
                .AddArgument("taskId", task.Id.ToString())
                .AddArgument("preset", SnoozePresets.TenMinutes))
            .AddButton(new ToastButton()
                .SetContent("1 saat")
                .AddArgument("action", "snooze")
                .AddArgument("taskId", task.Id.ToString())
                .AddArgument("preset", SnoozePresets.OneHour))
            .AddButton(new ToastButton()
                .SetContent("Bu akşam")
                .AddArgument("action", "snooze")
                .AddArgument("taskId", task.Id.ToString())
                .AddArgument("preset", SnoozePresets.ThisEvening))
            .AddButton(new ToastButton()
                .SetContent("Yarına")
                .AddArgument("action", "snooze")
                .AddArgument("taskId", task.Id.ToString())
                .AddArgument("preset", SnoozePresets.Tomorrow))
            .SetToastScenario(ToastScenario.Reminder)
            .Show();
    }

    public void ShowHabitReminder(Habit habit)
        => ShowInfo("Alışkanlık · Yaver", habit.Name);

    public void ShowBriefing(string title, string body) => ShowInfo(title, body);

    public void ShowEveningClose(string title, string body) => ShowInfo(title, body);

    public void ShowFocusEnded(string title, string body) => ShowInfo(title, body);

    public void ShowInfo(string title, string body)
    {
        new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddText(title)
            .AddText(body)
            .Show();
    }

    public void ShowFriendRequest(string peerKey, string name)
    {
        var who = string.IsNullOrWhiteSpace(name) ? "Bir kullanıcı" : name.Trim();
        new ToastContentBuilder()
            .AddArgument("action", "friendRequest")
            .AddArgument("peerKey", peerKey)
            .AddArgument("name", who)
            .AddText("Arkadaşlık isteği · Yaver")
            .AddText(who + " sizi eklemek istiyor.")
            .AddButton(new ToastButton()
                .SetContent("Kabul et")
                .AddArgument("action", "friendAccept")
                .AddArgument("peerKey", peerKey)
                .AddArgument("name", who))
            .AddButton(new ToastButton()
                .SetContent("Reddet")
                .AddArgument("action", "friendDecline")
                .AddArgument("peerKey", peerKey)
                .AddArgument("name", who))
            .SetToastScenario(ToastScenario.Reminder)
            .Show();
    }
}
