namespace Planner.Core.Models;

public sealed class TaskAttachment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string OriginalName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
