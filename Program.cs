using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using dotenv.net;
using IslandParrotCourier.Data;
using IslandParrotCourier.Services;
using IslandParrotCourier.Services.Events;
using IslandParrotCourier.Services.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IslandParrotCourier;

public static class Program
{
    public static async Task Main(string[] args)
    {
#if DEBUG
        string dir = Environment.GetEnvironmentVariable("DEBUG_PROJECT_DIR") ?? Environment.CurrentDirectory;
        string file = Path.Combine(dir, ".env");
        DotEnv.Load(new DotEnvOptions(envFilePaths: [file]));
#else
        DotEnv.Load();
#endif

        var host = Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(ConfigureServices)
            .Build();

        await MigrateDb(host.Services);
        await host.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services
            .AddLogging(builder => builder.AddConsole())
            .AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=islandparrotcourier.db"))
            .AddSingleton(new DiscordSocketConfig()
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMembers
            })
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>()))
            .AddScoped<IGameRepository, GameRepository>()
            .AddSingleton<GameEventChannel>()
            .AddSingleton<ArchipelagoService>()
            .AddSingleton<IArchipelagoService>(sp => sp.GetRequiredService<ArchipelagoService>())
            .AddHostedService(sp => sp.GetRequiredService<ArchipelagoService>())
            .AddHostedService<GameEventDispatcher>()
            .AddHostedService<DiscordClientService>();
    }

    private static async Task MigrateDb(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
