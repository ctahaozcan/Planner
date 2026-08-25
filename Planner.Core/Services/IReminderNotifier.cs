using Planner.Core.Models;

namespace Planner.Core.Services;

public interface IReminderNotifier
{
    void ShowTaskReminder(PlannerTask task, DateOnly occurrenceDate);
    void ShowHabitReminder(Habit habit);
    void ShowBriefing(string title, string body);
    void ShowEveningClose(string title, string body);
    void ShowFocusEnded(string title, string body);
    void ShowInfo(string title, string body);
    void ShowFriendRequest(string peerKey, string name);
}

public interface ITaskChangeSignal
{
    event Action? TasksChanged;
    event Action<string, string>? Info;
    void NotifyChanged();
    void NotifyInfo(string title, string body);
}

public static class SnoozePresets
{
    public const string TenMinutes = "10m";
    public const string OneHour = "1h";
    public const string ThisEvening = "evening";
    public const string Tomorrow = "tomorrow";

    public static DateTime Resolve(string key, DateTime now, TimeOnly evening)
    {
        return key switch
        {
            TenMinutes => now.AddMinutes(10),
            OneHour => now.AddHours(1),
            ThisEvening => now.TimeOfDay < evening.ToTimeSpan()
                ? DateOnly.FromDateTime(now).ToDateTime(evening)
                : DateOnly.FromDateTime(now).AddDays(1).ToDateTime(evening),
            Tomorrow => DateOnly.FromDateTime(now).AddDays(1).ToDateTime(new TimeOnly(9, 0)),
            _ => now.AddMinutes(10)
        };
    }
}
