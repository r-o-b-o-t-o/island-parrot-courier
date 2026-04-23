namespace IslandParrotCourier.Data.Entities;

public class Game
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public ulong? ChannelId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Player> Players { get; set; } = [];
}
