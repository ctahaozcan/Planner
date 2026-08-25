namespace Planner.Core.Models;

public static class NetworkIds
{
    public static readonly Guid Me = Guid.Parse("00000000-0000-0000-0000-000000000001");
}

public sealed class MeProfile
{
    public Guid Id { get; set; } = NetworkIds.Me;
    public string Name { get; set; } = "Ben";
    public string? Notes { get; set; }
    public string? PhotoFileName { get; set; }
    public DateTime UpdatedAt { get; set; }
}
