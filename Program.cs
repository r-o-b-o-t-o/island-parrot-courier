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
        string dir = Environment.GetEnvironmentVariable("DEBUG_PROJECT_DIR") ?? AppContext.BaseDirectory;
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
            .AddLogging(builder =>
            {
                builder.AddSimpleConsole(console =>
                {
                    console.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                    console.IncludeScopes = true;
                    console.SingleLine = true;
                });
            })
            .ConfigureDb()
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
            .AddSingleton<DiscordClientService>()
            .AddSingleton<IDiscordClientService>(sp => sp.GetRequiredService<DiscordClientService>())
            .AddHostedService(sp => sp.GetRequiredService<DiscordClientService>());
    }

    private static IServiceCollection ConfigureDb(this IServiceCollection services)
    {
        string sqliteConnectionString = Environment.GetEnvironmentVariable("SQLITE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(sqliteConnectionString))
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "data", "islandparrotcourier.db");
            string dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }
            sqliteConnectionString = $"Data Source=\"{dbPath}\"";
        }

        return services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqliteConnectionString));
    }

    private static async Task MigrateDb(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
