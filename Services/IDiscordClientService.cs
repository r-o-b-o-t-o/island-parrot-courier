namespace IslandParrotCourier.Services;

public interface IDiscordClientService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task WaitForReadyAsync(CancellationToken cancellationToken = default);
}
