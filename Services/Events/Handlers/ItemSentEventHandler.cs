using Discord;
using Discord.WebSocket;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Services.Events.Handlers;

public class ItemSentEventHandler(
        IGameRepository gameRepository,
        DiscordSocketClient discord
    ) : IGameEventHandler<ItemSentEvent>
{
    public async Task HandleAsync(ItemSentEvent gameEvent)
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

        var senderPlayer = await gameRepository.GetPlayerBySlotAsync(gameEvent.GameId, gameEvent.SenderName);
        var recipientPlayer = await gameRepository.GetPlayerBySlotAsync(gameEvent.GameId, gameEvent.RecipientName);

        var senderMention = senderPlayer?.Mention ?? $"**{gameEvent.SenderName}**";
        var recipientMention = recipientPlayer?.Mention ?? $"**{gameEvent.RecipientName}**";

        var isSelfSend = gameEvent.SenderName.Equals(gameEvent.RecipientName, StringComparison.InvariantCultureIgnoreCase);

        var description = isSelfSend
            ? $"{senderMention} found their **{gameEvent.ItemName}** from *{gameEvent.LocationName}*"
            : $"{senderMention} sent **{gameEvent.ItemName}** to {recipientMention} from *{gameEvent.LocationName}*";

        var embed = new EmbedBuilder()
            .WithColor(Color.Gold)
            .WithTitle($"📦 Item {(isSelfSend ? "Found" : "Sent")}!")
            .WithDescription(description)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }
}
