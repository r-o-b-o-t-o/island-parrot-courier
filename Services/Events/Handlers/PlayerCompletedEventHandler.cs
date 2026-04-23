using Discord;
using Discord.WebSocket;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Services.Events.Handlers;

public class PlayerCompletedEventHandler(
        IGameRepository gameRepository,
        DiscordSocketClient discord
    ) : IGameEventHandler<PlayerCompletedEvent>
{
    public async Task HandleAsync(PlayerCompletedEvent gameEvent)
    {
        var game = await gameRepository.GetGameByIdAsync(gameEvent.GameId);
        if (game?.ChannelId == null)
        {
            return;
        }

        if (discord.GetChannel(game.ChannelId.Value) is not IMessageChannel channel)
        {
            return;
        }

        var player = await gameRepository.GetPlayerBySlotAsync(gameEvent.GameId, gameEvent.PlayerName);
        if (player != null)
        {
            await gameRepository.MarkPlayerCompletedAsync(player.Id);
        }

        var mention = player?.Mention ?? $"**{gameEvent.PlayerName}**";
        var allPlayers = await gameRepository.GetPlayersAsync(gameEvent.GameId);

        await channel.SendMessageAsync(embed: new EmbedBuilder()
            .WithColor(Color.Green)
            .WithTitle("🎉 World Complete!")
            .WithDescription($"{mention} has completed their world!")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build());

        if (!allPlayers.All(p => p.IsCompleted))
        {
            return;
        }

        await gameRepository.MarkGameCompletedAsync(game.Id);

        await channel.SendMessageAsync(embed: new EmbedBuilder()
            .WithColor(Color.Purple)
            .WithTitle("🏆 Game Complete!")
            .WithDescription($"All players in **{game.Name}** have completed their worlds! Congratulations!")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build());
    }
}
