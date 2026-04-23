using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using IslandParrotCourier.Services.Events;
using IslandParrotCourier.Services.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IslandParrotCourier.Services;

public class ArchipelagoService(
        GameEventChannel eventChannel,
        IServiceScopeFactory scopeFactory,
        ILogger<ArchipelagoService> logger
    ) : IArchipelagoService, IHostedService
{
    private readonly ConcurrentDictionary<(int GameId, string SlotName), ArchipelagoSession> sessions = new();

    // Tracks which gameId already has a primary session subscribing to MessageLog.
    private readonly ConcurrentDictionary<int, bool> primarySessions = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var gameRepository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var activeGames = await gameRepository.GetActiveGamesAsync();

        foreach (var game in activeGames)
        {
            foreach (var player in game.Players.Where(p => !p.IsCompleted))
            {
                try
                {
                    await ConnectAsync(game.Id, game.Host, game.Port, player.SlotName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to reconnect slot {SlotName} for game {GameId} at startup", player.SlotName, game.Id);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in sessions.Values)
        {
            session.Socket.DisconnectAsync();
        }
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(int gameId, string host, int port, string slotName)
    {
        if (sessions.ContainsKey((gameId, slotName)))
        {
            return;
        }

        var session = ArchipelagoSessionFactory.CreateSession(host, port);

        var result = session.TryConnectAndLogin(
            game: "",
            name: slotName,
            itemsHandlingFlags: ItemsHandlingFlags.AllItems,
            tags: ["Tracker", "TextOnly"]
        );

        if (!result.Successful)
        {
            var failure = (LoginFailure)result;
            throw new InvalidOperationException($"Failed to connect to Archipelago: {string.Join(", ", failure.Errors)}");
        }

        sessions[(gameId, slotName)] = session;

        session.Items.ItemReceived += (helper) => OnItemReceived(gameId, slotName, helper);

        // Only the first session per game subscribes to MessageLog to avoid duplicate notifications.
        if (primarySessions.TryAdd(gameId, true))
        {
            session.MessageLog.OnMessageReceived += message =>
            {
                if (message is GoalLogMessage goalMessage)
                {
                    OnGoalMessage(gameId, goalMessage);
                }
                else if (message is JoinLogMessage joinMessage && !joinMessage.Tags.Any(t => t.Equals("TextOnly", StringComparison.InvariantCultureIgnoreCase)))
                {
                    OnJoinMessage(gameId, joinMessage);
                }
                else if (message is LeaveLogMessage leaveMessage)
                {
                    OnLeaveMessage(gameId, leaveMessage);
                }
            };
        }

        logger.LogInformation("Connected slot {SlotName} for game {GameId} at {Host}:{Port}", slotName, gameId, host, port);

        await Task.CompletedTask;
    }

    public void Disconnect(int gameId)
    {
        var keys = sessions.Keys.Where(k => k.GameId == gameId).ToList();
        foreach (var key in keys)
        {
            if (sessions.TryRemove(key, out var session))
            {
                session.Socket.DisconnectAsync();
            }
        }
        primarySessions.TryRemove(gameId, out _);
        logger.LogInformation("Disconnected all sessions for game {GameId}", gameId);
    }

    public bool IsConnected(int gameId, string slotName)
    {
        return sessions.TryGetValue((gameId, slotName), out var session) && session.Socket.Connected;
    }

    public List<HintInfo> GetHints(int gameId, string slotName)
    {
        if (!sessions.TryGetValue((gameId, slotName), out var session))
        {
            throw new InvalidOperationException($"No active session for slot \"{slotName}\" in this game.");
        }

        var player = session.Players.AllPlayers.FirstOrDefault(p => p.Name.Equals(slotName))
            ?? throw new InvalidOperationException($"Could not find slot \"{slotName}\"");

        var hints = session.Hints.GetHints(player.Slot, null);
        return hints.Select(h => new HintInfo()
        {
            ItemName = session.Items.GetItemName(h.ItemId, session.Players.GetPlayerInfo(h.ReceivingPlayer)?.Game ?? ""),
            LocationName = session.Locations.GetLocationNameFromId(h.LocationId, session.Players.GetPlayerInfo(h.FindingPlayer)?.Game ?? ""),
            FindingSlot = session.Players.GetPlayerName(h.FindingPlayer) ?? "Unknown",
            FindingPlayerName = session.Players.GetPlayerAlias(h.FindingPlayer) ?? "Unknown",
            ReceivingSlot = session.Players.GetPlayerName(h.ReceivingPlayer) ?? "Unknown",
            ReceivingPlayerName = session.Players.GetPlayerAlias(h.ReceivingPlayer) ?? "Unknown",
            Found = h.Found
        }).ToList();
    }

    public List<PlayerProgress> GetProgress(int gameId)
    {
        var gameSlotSessions = sessions
            .Where(s => s.Key.GameId == gameId)
            .Select(pair => pair.Value)
            .ToList();

        if (gameSlotSessions.Count == 0)
        {
            throw new InvalidOperationException("Not connected to Archipelago for this game.");
        }

        return gameSlotSessions.Select(s => new PlayerProgress()
        {
            SlotName = s.Players.GetPlayerName(s.ConnectionInfo.Slot) ?? "Unknown",
            LocationsChecked = s.Locations.AllLocationsChecked.Count,
            TotalLocations = s.Locations.AllLocations.Count
        }).ToList();
    }

    private void OnItemReceived(int gameId, string slotName, ReceivedItemsHelper helper)
    {
        try
        {
            var item = helper.DequeueItem();
            eventChannel.Writer.TryWrite(new ItemSentEvent(
                gameId,
                item.Player.Name,
                slotName,
                item.ItemDisplayName,
                item.LocationDisplayName
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueuing item received event for game {GameId}", gameId);
        }
    }

    private void OnGoalMessage(int gameId, GoalLogMessage goalMessage)
    {
        try
        {
            eventChannel.Writer.TryWrite(new PlayerCompletedEvent(
                gameId,
                goalMessage.Player.Name
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueuing goal event for game {GameId}", gameId);
        }
    }

    private void OnJoinMessage(int gameId, JoinLogMessage joinMessage)
    {
        try
        {
            eventChannel.Writer.TryWrite(new PlayerJoinedEvent(
                gameId,
                joinMessage.Player.Name,
                joinMessage.Player.Game
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueuing join event for game {GameId}", gameId);
        }
    }

    private void OnLeaveMessage(int gameId, LeaveLogMessage leaveMessage)
    {
        try
        {
            eventChannel.Writer.TryWrite(new PlayerLeftEvent(
                gameId,
                leaveMessage.Player.Name
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueuing leave event for game {GameId}", gameId);
        }
    }
}
