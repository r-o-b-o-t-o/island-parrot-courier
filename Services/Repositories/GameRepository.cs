using IslandParrotCourier.Data;
using IslandParrotCourier.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IslandParrotCourier.Services.Repositories;

public class GameRepository(AppDbContext db) : IGameRepository
{
    private readonly AppDbContext db = db;

    public async Task<Game> CreateGameAsync(string name, string host, int port, ulong channelId)
    {
        Game game = new()
        {
            Name = name,
            Host = host,
            Port = port,
            ChannelId = channelId,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    public async Task<Game> GetGameByIdAsync(int gameId)
    {
        return await db.Games
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);
    }

    public async Task<Game> GetGameByNameAsync(string name)
    {
        return await db.Games
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Name == name);
    }

    public async Task<Game> GetGameByChannelAsync(ulong channelId)
    {
        return await db.Games
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.ChannelId == channelId);
    }

    public async Task<Player> RegisterPlayerAsync(Game game, ulong discordUserId, string slotName)
    {
        Player player = new()
        {
            Game = game,
            DiscordUserId = discordUserId,
            SlotName = slotName,
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    public async Task MarkPlayerCompletedAsync(int playerId)
    {
        var player = await db.Players.FindAsync(playerId)
            ?? throw new InvalidOperationException($"Player {playerId} not found.");
        player.IsCompleted = true;
        await db.SaveChangesAsync();
    }

    public async Task MarkGameCompletedAsync(int gameId)
    {
        var game = await db.Games.FindAsync(gameId)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");
        game.IsCompleted = true;
        await db.SaveChangesAsync();
    }

    public async Task<List<Player>> GetPlayersAsync(int gameId)
    {
        return await db.Players.Where(p => p.GameId == gameId).ToListAsync();
    }

    public async Task<Player> GetPlayerBySlotAsync(int gameId, string slotName)
    {
        return await db.Players.FirstOrDefaultAsync(p => p.GameId == gameId && p.SlotName == slotName);
    }

    public async Task<List<Game>> GetActiveGamesAsync()
    {
        return await db.Games
            .Include(g => g.Players)
            .Where(g => !g.IsCompleted)
            .ToListAsync();
    }
}
