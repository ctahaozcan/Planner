namespace Planner.Core.Models;

public sealed class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#0F766E";
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }
}
