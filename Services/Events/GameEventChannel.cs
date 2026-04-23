using System.Threading.Channels;

namespace IslandParrotCourier.Services.Events;

public class GameEventChannel
{
    private readonly Channel<IGameEvent> channel =
        Channel.CreateBounded<IGameEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelWriter<IGameEvent> Writer => channel.Writer;
    public ChannelReader<IGameEvent> Reader => channel.Reader;
}
