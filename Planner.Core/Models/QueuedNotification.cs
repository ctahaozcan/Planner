namespace Planner.Core.Models;

public enum QueuedNotificationKind
{
    TaskReminder = 0,
    HabitReminder = 1,
    Briefing = 2,
    EveningClose = 3,
    FocusEnded = 4,
    Info = 5
}

public sealed class QueuedNotification
{
    public Guid Id { get; set; }
    public QueuedNotificationKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; }
}
