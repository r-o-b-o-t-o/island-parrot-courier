using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IslandParrotCourier.Services;

public class DiscordClientService(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceScopeFactory scopeFactory,
        ILogger<DiscordClientService> logger
    ) : IHostedService, IDiscordClientService
{
    private bool commandsRegistered;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
            ?? throw new InvalidOperationException("DISCORD_TOKEN environment variable is not set.");

        client.Log += msg =>
        {
            logger.LogInformation("{Message}", msg.ToString());
            return Task.CompletedTask;
        };

        client.Ready += async () =>
        {
            if (commandsRegistered)
            {
                return;
            }
            commandsRegistered = true;

            using var scope = scopeFactory.CreateScope();
            await interactions.AddModulesAsync(typeof(DiscordClientService).Assembly, scope.ServiceProvider);

            var guildIdStr = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
            if (ulong.TryParse(guildIdStr, out var guildId))
            {
                await interactions.RegisterCommandsToGuildAsync(guildId);
                logger.LogInformation("Commands registered to guild {GuildId}", guildId);
            }
            else
            {
                await interactions.RegisterCommandsGloballyAsync();
                logger.LogInformation("Commands registered globally");
            }
        };

        client.InteractionCreated += async interaction =>
        {
            using var scope = scopeFactory.CreateScope();
            var ctx = new SocketInteractionContext(client, interaction);
            await interactions.ExecuteCommandAsync(ctx, scope.ServiceProvider);
        };

        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.LogoutAsync();
        await client.StopAsync();
    }
}
