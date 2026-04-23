using Discord;
using Discord.WebSocket;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Services.Events.Handlers;

public class PlayerLeftEventHandler(
    IGameRepository gameRepository,
    DiscordSocketClient discord) : IGameEventHandler<PlayerLeftEvent>
{
    public async Task HandleAsync(PlayerLeftEvent gameEvent)
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
        var mention = player?.Mention ?? $"**{gameEvent.PlayerName}**";

        var embed = new EmbedBuilder()
            .WithColor(Color.Orange)
            .WithTitle("🚪 Player Disconnected")
            .WithDescription($"{mention} has disconnected from the game.")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }
}
