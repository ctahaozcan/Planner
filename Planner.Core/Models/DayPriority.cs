namespace Planner.Core.Models;

public sealed class DayPriority
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public Guid TaskId { get; set; }
    public int Slot { get; set; }
}
