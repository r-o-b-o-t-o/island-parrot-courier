using System.Threading.Channels;

namespace IslandParrotCourier.Services.Events;

public class GameEventChannel
{
    private readonly Channel<IGameEvent> channel =
        Channel.CreateUnbounded<IGameEvent>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<IGameEvent> Writer => channel.Writer;
    public ChannelReader<IGameEvent> Reader => channel.Reader;
}
