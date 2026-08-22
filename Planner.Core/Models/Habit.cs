namespace Planner.Core.Models;

public enum HabitScheduleKind
{
    Daily = 0,
    Weekdays = 1
}

public sealed class Habit
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public HabitScheduleKind ScheduleKind { get; set; }
    public TimeOnly? ReminderTime { get; set; }
    public bool ReminderEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class HabitLog
{
    public Guid Id { get; set; }
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CompletedAt { get; set; }
}

public sealed class HabitSnapshot
{
    public required Habit Habit { get; init; }
    public bool IsDueToday { get; init; }
    public bool IsCompletedToday { get; init; }
    public int Streak { get; init; }
}
