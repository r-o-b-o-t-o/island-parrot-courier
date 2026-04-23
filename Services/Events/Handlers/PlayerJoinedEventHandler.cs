using Discord;
using Discord.WebSocket;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Services.Events.Handlers;

public class PlayerJoinedEventHandler(
    IGameRepository gameRepository,
    DiscordSocketClient discord) : IGameEventHandler<PlayerJoinedEvent>
{
    public async Task HandleAsync(PlayerJoinedEvent gameEvent)
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
            .WithColor(Color.Green)
            .WithTitle("👋 Player Connected")
            .WithDescription($"{mention} is now playing *{gameEvent.GameName}*.")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }
}
