namespace IslandParrotCourier.Services.Events;

public interface IGameEventHandler<TEvent> where TEvent : IGameEvent
{
    Task HandleAsync(TEvent gameEvent);
}
