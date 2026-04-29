namespace IslandParrotCourier.Data.Entities;

public class Player
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public ulong DiscordUserId { get; set; }
    public required string SlotName { get; set; }
    public bool IsCompleted { get; set; }
    public int ItemIndex { get; set; }

    public required Game Game { get; set; }

    public string Mention => $"<@{DiscordUserId}>";
}
