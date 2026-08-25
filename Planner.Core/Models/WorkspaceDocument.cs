namespace Planner.Core.Models;

public enum WorkspaceDocumentKind
{
    Text = 0,
    Table = 1
}

public sealed class WorkspaceDocument
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "Adsız";
    public WorkspaceDocumentKind Kind { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? OwnerUserId { get; set; }
}
