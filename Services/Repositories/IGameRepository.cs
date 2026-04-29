using IslandParrotCourier.Data.Entities;

namespace IslandParrotCourier.Services.Repositories;

public interface IGameRepository
{
    Task<Game> CreateGameAsync(string name, string host, int port, ulong channelId);
    Task<Game> GetGameByIdAsync(int gameId);
    Task<Game> GetGameByNameAsync(string name);
    Task<Game> GetGameByChannelAsync(ulong channelId);
    Task<Player> RegisterPlayerAsync(Game game, ulong discordUserId, string slotName);
    Task MarkPlayerCompletedAsync(int playerId);
    Task UpdateItemIndexAsync(int gameId, string slotName, int itemIndex);
    Task MarkGameCompletedAsync(int gameId);
    Task<List<Player>> GetPlayersAsync(int gameId);
    Task<Player> GetPlayerBySlotAsync(int gameId, string slotName);
    Task<List<Game>> GetActiveGamesAsync();
}
