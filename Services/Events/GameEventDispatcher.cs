using Discord.WebSocket;
using IslandParrotCourier.Services.Events.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IslandParrotCourier.Services.Events;

public class GameEventDispatcher(
        GameEventChannel eventChannel,
        DiscordSocketClient discordClient,
        IServiceScopeFactory scopeFactory,
        ILogger<GameEventDispatcher> logger
    ) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Wait for Discord to be ready before processing events, so that channels
        // can be resolved and messages can be sent.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnReady()
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        }

        discordClient.Ready += OnReady;
        try
        {
            // Subscribe before checking state to avoid missing the event.
            if (discordClient.ConnectionState != Discord.ConnectionState.Connected)
            {
                await tcs.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            discordClient.Ready -= OnReady;
        }
        await foreach (var gameEvent in eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await DispatchAsync(gameEvent, scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Unhandled error dispatching {EventType} for game {GameId}",
                    gameEvent.GetType().Name,
                    gameEvent.GameId
                );
            }
        }
    }

    private static Task DispatchAsync(IGameEvent gameEvent, IServiceProvider services)
    {
        return gameEvent switch
        {
            ItemSentEvent e        => Dispatch<ItemSentEvent,        ItemSentEventHandler>(e, services),
            PlayerCompletedEvent e => Dispatch<PlayerCompletedEvent, PlayerCompletedEventHandler>(e, services),
            PlayerJoinedEvent e    => Dispatch<PlayerJoinedEvent,    PlayerJoinedEventHandler>(e, services),
            PlayerLeftEvent e      => Dispatch<PlayerLeftEvent,      PlayerLeftEventHandler>(e, services),

            _ => Task.CompletedTask
        };
    }

    private static async Task Dispatch<TEvent, THandler>(TEvent gameEvent, IServiceProvider services)
        where TEvent : IGameEvent
        where THandler : IGameEventHandler<TEvent>
    {
        var handler = ActivatorUtilities.CreateInstance<THandler>(services);
        await handler.HandleAsync(gameEvent);
    }
}
