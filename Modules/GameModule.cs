using Discord;
using Discord.Interactions;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Modules;

[Group("game", "Game management commands")]
public class GameModule(IGameRepository gameRepository) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("create", "Create a new Archipelago game session linked to this channel")]
    [CommandContextType(InteractionContextType.Guild | InteractionContextType.PrivateChannel)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task CreateGameAsync(
        [Summary("name", "Name for this game session")] string name,
        [Summary("host", "Archipelago server host")] string host,
        [Summary("port", "Archipelago server port")] int port)
    {
        await DeferAsync();

        try
        {
            if (await gameRepository.GetGameByChannelAsync(Context.Channel.Id) != null)
            {
                await FollowupAsync("❌ A game is already linked to this channel.", ephemeral: true);
                return;
            }

            var game = await gameRepository.CreateGameAsync(name, host, port, Context.Channel.Id);

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("🎮 Game Created")
                .AddField("Name", game.Name, true)
                .AddField("Host", $"`{game.Host}:{game.Port}`", true)
                .AddField("Channel", $"<#{game.ChannelId}>", true)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to create game: {ex.Message}", ephemeral: true);
        }
    }
}
