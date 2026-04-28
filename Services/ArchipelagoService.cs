using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;
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
    private const int MaxItemNameLength = 100;

    private readonly ConcurrentDictionary<(int GameId, string SlotName), ArchipelagoSession> sessions = new();

    // Tracks the key of the session currently subscribed to MessageLog for each game.
    private readonly ConcurrentDictionary<int, (int GameId, string SlotName)> primarySessions = new();

    // Tracks the last processed item index per slot, to avoid re-processing items on reconnect.
    private readonly ConcurrentDictionary<(int GameId, string SlotName), int> itemIndices = new();

    // Coalesces async item-index persistence: at most one background task per slot runs at a time.
    private readonly ConcurrentDictionary<(int GameId, string SlotName), int> pendingPersists = new();
    private readonly ConcurrentDictionary<(int GameId, string SlotName), Task> persistTasks = new();

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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(sessions.Values.Select(s => s.Socket.DisconnectAsync()));
    }

    public async Task ConnectAsync(int gameId, string host, int port, string slotName)
    {
        if (sessions.TryGetValue((gameId, slotName), out var existing))
        {
            if (existing.Socket.Connected)
            {
                return;
            }

            // Clean up the stale disconnected session before reconnecting.
            await existing.Socket.DisconnectAsync();
            sessions.TryRemove((gameId, slotName), out _);
        }

        using var scope = scopeFactory.CreateScope();
        var gameRepository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var player = await gameRepository.GetPlayerBySlotAsync(gameId, slotName);

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
        itemIndices[(gameId, slotName)] = player?.ItemIndex ?? 0;

        session.Items.ItemReceived += (helper) => OnItemReceived(gameId, slotName, helper);

        // Subscribe to MessageLog if there is no primary session for this game yet,
        // or if the existing primary session is no longer connected.
        var sessionKey = (gameId, slotName);
        var isPrimary = false;

        if (primarySessions.TryGetValue(gameId, out var currentPrimaryKey))
        {
            if (!sessions.TryGetValue(currentPrimaryKey, out var currentPrimary) || !currentPrimary.Socket.Connected)
            {
                primarySessions[gameId] = sessionKey;
                isPrimary = true;
            }
        }
        else if (primarySessions.TryAdd(gameId, sessionKey))
        {
            isPrimary = true;
        }

        if (isPrimary)
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
    }

    public async Task DisconnectAsync(int gameId)
    {
        var keys = sessions.Keys.Where(k => k.GameId == gameId).ToList();
        var disconnectTasks = new List<Task>();
        foreach (var key in keys)
        {
            if (sessions.TryRemove(key, out var session))
            {
                disconnectTasks.Add(session.Socket.DisconnectAsync());
            }
            itemIndices.TryRemove(key, out _);
            pendingPersists.TryRemove(key, out _);
            persistTasks.TryRemove(key, out _);
        }
        await Task.WhenAll(disconnectTasks);
        primarySessions.TryRemove(gameId, out _);
        logger.LogInformation("Disconnected all sessions for game {GameId}", gameId);
    }

    public bool IsConnected(int gameId, string slotName)
    {
        return sessions.TryGetValue((gameId, slotName), out var session) && session.Socket.Connected;
    }

    public async Task<List<HintInfo>> HintItemAsync(int gameId, string slotName, string itemName)
    {
        if (!sessions.TryGetValue((gameId, slotName), out var session))
        {
            throw new InvalidOperationException($"No active session for slot \"{slotName}\" in this game.");
        }

        var player = session.Players.AllPlayers.FirstOrDefault(p => string.Equals(p.Name, slotName))
            ?? throw new InvalidOperationException($"Could not find slot \"{slotName}\"");

        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new ArgumentException("Item name cannot be null, empty, or whitespace.", nameof(itemName));
        }

        itemName = itemName.Trim().ReplaceLineEndings("");
        if (itemName.Length > MaxItemNameLength)
        {
            itemName = itemName[..MaxItemNameLength];
        }

        // Initial timeout covers server response latency for the first hint.
        // Each matching hint resets the deadline to a debounce window, so
        // we stop waiting 2500ms after the last hint in the batch arrives.
        var initialTimeout = TimeSpan.FromSeconds(5);
        var debounceWindow = TimeSpan.FromMilliseconds(2500);
        using CancellationTokenSource cts = new(initialTimeout);
        int ctsActive = 1;

        void OnMessage(LogMessage message)
        {
            if (message is not HintItemSendLogMessage hint)
            {
                return;
            }
            if (!hint.Item.ItemName.Equals(itemName, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            if (Volatile.Read(ref ctsActive) == 0)
            {
                return;
            }
            try
            {
                cts.CancelAfter(debounceWindow);
            }
            catch (ObjectDisposedException)
            {
                // Expected race: the CTS was disposed between the active-flag check and CancelAfter.
                // The finally block will clear the flag and unsubscribe the handler, so this is safe to ignore.
            }
        }

        session.MessageLog.OnMessageReceived += OnMessage;
        try
        {
            await session.Socket.SendPacketAsync(new SayPacket { Text = $"!hint {itemName}" });
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Intentional: the debounce window or initial timeout fired, which is the expected exit path.
        }
        finally
        {
            Volatile.Write(ref ctsActive, 0);
            session.MessageLog.OnMessageReceived -= OnMessage;
        }

        var hints = await session.Hints.GetHintsAsync(player.Slot, null);
        return hints
            .Where(h => (session.Items.GetItemName(h.ItemId, session.Players.GetPlayerInfo(h.ReceivingPlayer)?.Game ?? "") ?? "")
                .Equals(itemName, StringComparison.InvariantCultureIgnoreCase))
            .Select(h => new HintInfo()
            {
                ItemName = session.Items.GetItemName(h.ItemId, session.Players.GetPlayerInfo(h.ReceivingPlayer)?.Game ?? "") ?? itemName,
                LocationName = session.Locations.GetLocationNameFromId(h.LocationId, session.Players.GetPlayerInfo(h.FindingPlayer)?.Game ?? "") ?? "Unknown",
                FindingSlot = session.Players.GetPlayerName(h.FindingPlayer) ?? "Unknown",
                FindingPlayerName = session.Players.GetPlayerAlias(h.FindingPlayer) ?? "Unknown",
                ReceivingSlot = session.Players.GetPlayerName(h.ReceivingPlayer) ?? "Unknown",
                ReceivingPlayerName = session.Players.GetPlayerAlias(h.ReceivingPlayer) ?? "Unknown",
                Found = h.Found
            })
            .OrderBy(h => h.Found)
            .ThenBy(h => h.ReceivingPlayerName)
            .ToList();
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
            Slot = s.Players.GetPlayerName(s.ConnectionInfo.Slot) ?? "Unknown",
            PlayerName = s.Players.GetPlayerAlias(s.ConnectionInfo.Slot) ?? "Unknown",
            LocationsChecked = s.Locations.AllLocationsChecked.Count,
            TotalLocations = s.Locations.AllLocations.Count
        }).ToList();
    }

    private void OnItemReceived(int gameId, string slotName, ReceivedItemsHelper helper)
    {
        try
        {
            // Drain the pending queue so the library's internal state stays consistent.
            while (helper.Any())
            {
                helper.DequeueItem();
            }

            var sessionKey = (gameId, slotName);
            var savedIndex = itemIndices.GetValueOrDefault(sessionKey, 0);
            var allItems = helper.AllItemsReceived;

            if (savedIndex > 0 && savedIndex <= allItems.Count)
            {
                logger.LogDebug("Skipping {Count} already-processed item(s) for slot {SlotName} in game {GameId}", savedIndex, slotName, gameId);
            }

            int newIndex = savedIndex;
            for (int i = savedIndex; i < allItems.Count; i++)
            {
                var item = allItems[i];
                eventChannel.Writer.TryWrite(new ItemSentEvent(
                    gameId,
                    item.Player.Name,
                    slotName,
                    item.ItemDisplayName,
                    item.LocationDisplayName
                ));
                newIndex = i + 1;
            }

            if (newIndex > savedIndex)
            {
                itemIndices[sessionKey] = newIndex;
                ScheduleIndexPersist(gameId, slotName, newIndex);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueuing item received event for game {GameId}", gameId);
        }
    }

    private void ScheduleIndexPersist(int gameId, string slotName, int index)
    {
        var key = (gameId, slotName);
        pendingPersists[key] = index;
        // Use GetOrAdd to ensure at most one background drain task runs per slot at a time.
        persistTasks.GetOrAdd(key, _ => DrainPersistAsync(gameId, slotName));
    }

    private Task DrainPersistAsync(int gameId, string slotName)
    {
        return Task.Run(async () =>
        {
            var key = (GameId: gameId, SlotName: slotName);
            try
            {
                while (pendingPersists.TryRemove(key, out var index))
                {
                    using var scope = scopeFactory.CreateScope();
                    var gameRepository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                    await gameRepository.UpdateItemIndexAsync(gameId, slotName, index);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist item index for slot {SlotName} in game {GameId}", slotName, gameId);
            }
            finally
            {
                persistTasks.TryRemove(key, out _);
                // Guard against a race where a new pending value was added after the while loop
                // exited but before the TryRemove above — ensure a new drain task picks it up.
                if (pendingPersists.ContainsKey(key))
                {
                    _ = persistTasks.GetOrAdd(key, _ => DrainPersistAsync(gameId, slotName));
                }
            }
        });
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
