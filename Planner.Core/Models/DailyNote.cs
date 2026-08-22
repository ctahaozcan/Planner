namespace Planner.Core.Models;

public sealed class DailyNote
{
    public DateOnly Date { get; set; }
    public string Content { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
