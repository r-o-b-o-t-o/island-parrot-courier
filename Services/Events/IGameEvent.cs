namespace IslandParrotCourier.Services.Events;

public interface IGameEvent
{
    int GameId { get; }
}

public record ItemSentEvent(
    int GameId,
    string SenderName,
    string RecipientName,
    string ItemName,
    string LocationName
) : IGameEvent;

public record PlayerCompletedEvent(
    int GameId,
    string PlayerName
) : IGameEvent;

public record PlayerJoinedEvent(
    int GameId,
    string PlayerName,
    string GameName
) : IGameEvent;

public record PlayerLeftEvent(
    int GameId,
    string PlayerName
) : IGameEvent;
