using System.Collections.Concurrent;
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
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        ILogger<DiscordClientService> logger
    ) : IHostedService, IDiscordClientService
{
    private bool commandsRegistered;
    private readonly ConcurrentDictionary<IInteractionContext, IServiceScope> activeScopes = new();
    private volatile TaskCompletionSource readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        // Loop in case a disconnect races with our await: Disconnected completes the old TCS
        // so we wake up, detect a stale TCS, and re-wait on the freshly created one.
        while (true)
        {
            var tcs = readyTcs;
            await tcs.Task.WaitAsync(cancellationToken);
            if (readyTcs == tcs)
            {
                return;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
            ?? throw new InvalidOperationException("DISCORD_TOKEN environment variable is not set.");

        client.Log += msg =>
        {
            logger.LogInformation("{Message}", msg.ToString(prependTimestamp: false));
            return Task.CompletedTask;
        };

        client.Ready += async () =>
        {
            readyTcs.TrySetResult();

            if (commandsRegistered)
            {
                return;
            }
            commandsRegistered = true;

            await interactions.AddModulesAsync(typeof(DiscordClientService).Assembly, services);

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

        client.Connected += () =>
        {
            // On a resumed session, Ready is not fired again — only Connected is.
            // Gate on commandsRegistered (set on first Ready) so we don't unblock
            // WaitForReadyAsync before Discord's cache is populated on initial startup.
            if (commandsRegistered)
            {
                readyTcs.TrySetResult();
            }
            return Task.CompletedTask;
        };

        client.Disconnected += _ =>
        {
            // Swap in a new TCS and complete the old one so any in-flight WaitForReadyAsync
            // callers wake up, detect the stale TCS in their loop, and re-wait on the new one.
            var oldTcs = readyTcs;
            readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            oldTcs.TrySetResult();
            return Task.CompletedTask;
        };

        client.InteractionCreated += async interaction =>
        {
            var scope = scopeFactory.CreateScope();
            var ctx = new SocketInteractionContext(client, interaction);
            activeScopes[ctx] = scope;
            await interactions.ExecuteCommandAsync(ctx, scope.ServiceProvider);
        };

        interactions.InteractionExecuted += (_, ctx, _) =>
        {
            if (activeScopes.TryRemove(ctx, out var scope))
            {
                scope.Dispose();
            }
            return Task.CompletedTask;
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
