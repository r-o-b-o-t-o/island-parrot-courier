using Discord;
using Discord.Interactions;
using IslandParrotCourier.Services;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Modules;

[Group("player", "Player management commands")]
public class PlayerModule(
        IGameRepository gameRepository,
        IArchipelagoService archipelagoService
    ) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("register", "Register yourself to the current game with an Archipelago slot")]
    public async Task RegisterSelfAsync(
        [Summary("slot", "Your Archipelago slot name")] string slot)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var game = await gameRepository.GetGameByChannelAsync(Context.Channel.Id);
            if (game == null)
            {
                await FollowupAsync("❌ No game is linked to this channel.", ephemeral: true);
                return;
            }

            if (game.Players.Any(p => p.DiscordUserId == Context.User.Id))
            {
                await FollowupAsync($"❌ You are already registered in **{game.Name}**.", ephemeral: true);
                return;
            }

            if (game.Players.Any(p => p.SlotName == slot))
            {
                await FollowupAsync($"❌ Slot **{slot}** is already registered in **{game.Name}**.", ephemeral: true);
                return;
            }

            await gameRepository.RegisterPlayerAsync(game, Context.User.Id, slot);
            await archipelagoService.ConnectAsync(game.Id, game.Host, game.Port, slot);

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("✅ Registered")
                .WithDescription($"You are now registered as **{slot}** in **{game.Name}**.")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to register: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("register-user", "Register another user to the current game (Admin only)")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task RegisterUserAsync(
        [Summary("slot", "Archipelago slot name")] string slot,
        [Summary("user", "The Discord user to register")] IUser user)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var game = await gameRepository.GetGameByChannelAsync(Context.Channel.Id);
            if (game == null)
            {
                await FollowupAsync("❌ No game is linked to this channel.", ephemeral: true);
                return;
            }

            if (game.Players.Any(p => p.SlotName == slot))
            {
                await FollowupAsync($"❌ Slot **{slot}** is already registered in **{game.Name}**.", ephemeral: true);
                return;
            }

            await gameRepository.RegisterPlayerAsync(game, user.Id, slot);
            await archipelagoService.ConnectAsync(game.Id, game.Host, game.Port, slot);

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("✅ User Registered")
                .WithDescription($"{user.Mention} is now registered as **{slot}** in **{game.Name}**.") 
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to register user: {ex.Message}", ephemeral: true);
        }
    }
}
