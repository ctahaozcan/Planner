namespace Planner.Core.Models;

public sealed class VaultMeta
{
    public int Id { get; set; } = 1;
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] KeyVerifier { get; set; } = [];
    public int Iterations { get; set; }
    public DateTime CreatedAt { get; set; }
}
