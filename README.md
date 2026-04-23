# 🦜 Island Parrot Courier

A Discord bot that integrates with [Archipelago](https://archipelago.gg/) multiworld randomizer servers to provide real-time game tracking, notifications, and progress monitoring.

![.NET 10](https://img.shields.io/badge/.NET-10-purple)
![Discord.Net](https://img.shields.io/badge/Discord.Net-3.19-blue)
![Archipelago](https://img.shields.io/badge/Archipelago-MultiClient.Net-green)

---

## ✨ Features

- **Game Sessions** — Admins create game sessions linked to Archipelago servers
- **Player Registration** — Players register with their Archipelago slot name
- **Real-time Notifications** — Item sends and world completions posted to Discord
- **Progress Tracking** — Per-player and global location check progress with visual bars
- **Hint Lookup** — Query your Archipelago hints directly from Discord
- **Channel Isolation** — Games are scoped to their linked Discord channel

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A [Discord Bot Token](https://discord.com/developers/applications)
- An Archipelago server to connect to

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-username/IslandParrotCourier.git
   cd IslandParrotCourier
   ```

2. **Configure the environment**
   ```bash
   cp .env.example .env
   ```
   Edit `.env` and fill in your values:
   ```
   DISCORD_TOKEN=your_discord_bot_token
   DISCORD_GUILD_ID=your_guild_id
   ```

3. **Run the bot**
   ```bash
   dotnet run
   ```

### Docker

```bash
docker compose up -d
```

## 📖 Commands

| Command | Description | Permission |
|---------|-------------|------------|
| `/game create` | Create a new game session linked to the current channel | Admin |
| `/player register` | Register yourself with a slot name | Everyone |
| `/player register-user` | Register another user | Admin |
| `/archipelago hints-incoming` | View hints for your items | Everyone |
| `/archipelago hints-outgoing` | View hints for other players' items | Everyone |
| `/archipelago progress` | View game progress | Everyone |

## 🏗️ Architecture

```
IslandParrotCourier/
├── Data/
│   ├── Entities/        # EF Core entity models
│   └── AppDbContext.cs  # EF Core database context
├── Modules/             # Discord slash command modules
├── Services/
│   ├── Events/
│   │   ├── Handlers/
│   │   │   ├── ItemSentEventHandler.cs          # Posts item send notifications to Discord
│   │   │   ├── PlayerCompletedEventHandler.cs   # Announces world completion
│   │   │   ├── PlayerJoinedEventHandler.cs      # Handles player join events
│   │   │   └── PlayerLeftEventHandler.cs        # Handles player disconnect events
│   │   ├── GameEventChannel.cs                  # Unbounded channel for async event queuing
│   │   ├── GameEventDispatcher.cs               # Hosted service that routes events to handlers
│   │   └── IGameEvent.cs / IGameEventHandler.cs # Event contracts
│   ├── Repositories/
│   │   └── GameRepository.cs                    # EF Core data access for games & players
│   ├── ArchipelagoService.cs                    # Manages Archipelago sessions per game/slot
│   └── DiscordClientService.cs                  # Bootstraps the Discord socket client
├── Program.cs           # Entry point & DI setup
├── Dockerfile
└── docker-compose.yml
```

### Services Overview

| Service | Role |
|---------|------|
| `ArchipelagoService` | Hosted service that connects to Archipelago servers, tracks active sessions per game, and publishes events to `GameEventChannel` |
| `DiscordClientService` | Hosted service that starts the Discord WebSocket client and registers slash command interactions |
| `GameEventChannel` | Thread-safe `System.Threading.Channels` pipeline used to decouple Archipelago event production from Discord notification delivery |
| `GameEventDispatcher` | Hosted service that reads from `GameEventChannel` and dispatches events to the appropriate `IGameEventHandler` |
| `GameRepository` | EF Core repository providing scoped data access for game sessions and registered players |

## 📝 License

This project is open source and available under the [MIT License](LICENSE).
