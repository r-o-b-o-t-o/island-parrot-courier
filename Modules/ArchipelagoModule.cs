using System.Text;
using Discord;
using Discord.Interactions;
using IslandParrotCourier.Services;
using IslandParrotCourier.Services.Repositories;

namespace IslandParrotCourier.Modules;

[Group("archipelago", "Archipelago information commands")]
public class ArchipelagoModule(
        IGameRepository gameRepository,
        IArchipelagoService archipelagoService
    ) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("hints-incoming", "Items that will be sent to you, wherever they are in the multiworld")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task GetIncomingHintsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var game = await gameRepository.GetGameByChannelAsync(Context.Channel.Id);
            if (game == null)
            {
                await FollowupAsync("❌ No game is linked to this channel.", ephemeral: true);
                return;
            }

            var player = game.Players.FirstOrDefault(p => p.DiscordUserId == Context.User.Id);
            if (player == null)
            {
                await FollowupAsync("❌ You are not registered in this game.", ephemeral: true);
                return;
            }

            var players = await gameRepository.GetPlayersAsync(game.Id);
            var mentionBySlot = players.ToDictionary(p => p.SlotName, p => p.Mention);

            string SlotMention(string slot, string playerName) =>
                mentionBySlot.TryGetValue(slot, out var m) ? m : $"**{playerName}**";

            var hints = archipelagoService
                .GetHints(game.Id, player.SlotName)
                .Where(h => h.ReceivingSlot.Equals(player.SlotName))
                .ToList();

            if (hints.Count == 0)
            {
                await FollowupAsync("No incoming hints available yet.", ephemeral: true);
                return;
            }

            var lines = hints
                .OrderBy(h => h.Found)
                .ThenBy(h => h.ItemName)
                .ThenBy(h => h.LocationName)
                .Select(hint =>
                {
                    var status = hint.Found ? "✅" : "❓";
                    return $"{status} **{hint.ItemName}** at {SlotMention(hint.FindingSlot, hint.FindingPlayerName)}'s *{hint.LocationName}*";
                });

            string playerName = player.SlotName;
            if (Context.User != null && Context.Guild != null)
            {
                var discordUser = Context.Guild.GetUser(Context.User.Id);
                if (discordUser is IGuildUser guildUser)
                {
                    playerName = guildUser.DisplayName;
                }
            }

            var pages = SplitIntoPages(lines);
            for (var i = 0; i < pages.Count; i++)
            {
                var title = pages.Count > 1
                    ? $"📥 Incoming Hints for {playerName} ({i + 1}/{pages.Count})"
                    : $"📥 Incoming Hints for {playerName}";
                var embed = new EmbedBuilder()
                    .WithColor(Color.Teal)
                    .WithTitle(title)
                    .WithDescription(pages[i])
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();
                await FollowupAsync(embed: embed, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to get hints: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("hints-outgoing", "Items in your world that belong to other players")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task GetOutgoingHintsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var game = await gameRepository.GetGameByChannelAsync(Context.Channel.Id);
            if (game == null)
            {
                await FollowupAsync("❌ No game is linked to this channel.", ephemeral: true);
                return;
            }

            var player = game.Players.FirstOrDefault(p => p.DiscordUserId == Context.User.Id);
            if (player == null)
            {
                await FollowupAsync("❌ You are not registered in this game.", ephemeral: true);
                return;
            }

            var players = await gameRepository.GetPlayersAsync(game.Id);
            var mentionBySlot = players.ToDictionary(p => p.SlotName, p => p.Mention);

            string SlotMention(string slot, string playerName) =>
                mentionBySlot.TryGetValue(slot, out var m) ? m : $"**{playerName}**";

            var hints = archipelagoService
                .GetHints(game.Id, player.SlotName)
                .Where(h =>
                    h.FindingSlot.Equals(player.SlotName) &&
                    !h.ReceivingSlot.Equals(player.SlotName)
                )
                .ToList();

            if (hints.Count == 0)
            {
                await FollowupAsync("No outgoing hints available yet.", ephemeral: true);
                return;
            }

            var lines = hints
                .OrderBy(h => h.Found)
                .ThenBy(h => h.ItemName)
                .ThenBy(h => h.LocationName)
                .Select(hint =>
                {
                    var status = hint.Found ? "✅" : "❓";
                    return $"{status} {SlotMention(hint.ReceivingSlot, hint.ReceivingPlayerName)}'s **{hint.ItemName}** at *{hint.LocationName}*";
                });

            string playerName = player.SlotName;
            if (Context.User != null && Context.Guild != null)
            {
                var discordUser = Context.Guild.GetUser(Context.User.Id);
                if (discordUser is IGuildUser guildUser)
                {
                    playerName = guildUser.DisplayName;
                }
            }

            var pages = SplitIntoPages(lines);
            for (var i = 0; i < pages.Count; i++)
            {
                var title = pages.Count > 1
                    ? $"🔍 Outgoing Hints for {playerName} ({i + 1}/{pages.Count})"
                    : $"🔍 Outgoing Hints for {playerName}";
                var embed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle(title)
                    .WithDescription(pages[i])
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();
                await FollowupAsync(embed: embed, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to get hints: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("hint", "Request a hint for a specific item")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task HintItemAsync([Summary("item", "The name of the item to hint for")] string item)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var game = await gameRepository.GetGameByChannelAsync(Context.Channel.Id);
            if (game == null)
            {
                await FollowupAsync("❌ No game is linked to this channel.", ephemeral: true);
                return;
            }

            var player = game.Players.FirstOrDefault(p => p.DiscordUserId == Context.User.Id);
            if (player == null)
            {
                await FollowupAsync("❌ You are not registered in this game.", ephemeral: true);
                return;
            }

            var players = await gameRepository.GetPlayersAsync(game.Id);
            var mentionBySlot = players.ToDictionary(p => p.SlotName, p => p.Mention);

            string SlotMention(string slot, string playerName) =>
                mentionBySlot.TryGetValue(slot, out var m) ? m : $"**{playerName}**";

            var hints = await archipelagoService.HintItemAsync(game.Id, player.SlotName, item);

            if (hints.Count == 0)
            {
                await FollowupAsync($"No hints found for **{item}**.", ephemeral: true);
                return;
            }

            var lines = hints.Select(hint =>
            {
                var status = hint.Found ? "✅" : "❓";
                return $"{status} {SlotMention(hint.ReceivingSlot, hint.ReceivingPlayerName)}'s **{hint.ItemName}** at {SlotMention(hint.FindingSlot, hint.FindingPlayerName)}'s *{hint.LocationName}*";
            });

            var pages = SplitIntoPages(lines);
            for (var i = 0; i < pages.Count; i++)
            {
                var title = pages.Count > 1
                    ? $"🔎 Hints for \"{item}\" ({i + 1}/{pages.Count})"
                    : $"🔎 Hints for \"{item}\"";
                var embed = new EmbedBuilder()
                    .WithColor(Color.Purple)
                    .WithTitle(title)
                    .WithDescription(pages[i])
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();
                await FollowupAsync(embed: embed, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to hint for item: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("progress", "View the current game progress")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task GetProgressAsync()
    {
        await DeferAsync();

        try
        {
            var game = await gameRepository.GetGameByChannelAsync(Context.Channel.Id);
            if (game == null)
            {
                await FollowupAsync("❌ No game is linked to this channel.", ephemeral: true);
                return;
            }

            var progress = archipelagoService.GetProgress(game.Id);
            if (progress.Count == 0)
            {
                await FollowupAsync("No progress data available.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle($"📊 Progress — {game.Name}");

            long totalChecked = 0;
            long totalLocations = 0;

            foreach (var data in progress.OrderByDescending(p => p.Percentage))
            {
                string playerName = data.PlayerName;

                var player = game.Players.FirstOrDefault(p => p.SlotName.Equals(data.Slot));
                if (player != null && Context.Guild != null)
                {
                    var guildUser = Context.Guild.GetUser(player.DiscordUserId)
                        ?? await ((IGuild)Context.Guild).GetUserAsync(player.DiscordUserId);
                    if (guildUser != null)
                    {
                        playerName = guildUser.DisplayName;
                    }
                }

                var bar = BuildProgressBar(data.Percentage);
                var completedEmoji = player?.IsCompleted == true ? "🏆 " : "";
                embed.AddField(
                    $"{completedEmoji}{playerName}",
                    $"{bar} {data.Percentage}%\n{data.LocationsChecked}/{data.TotalLocations} locations",
                    inline: false
                );

                totalChecked += data.LocationsChecked;
                totalLocations += data.TotalLocations;
            }

            var globalPct = totalLocations == 0 ? 0 : Math.Round((double)totalChecked / totalLocations * 100, 2);
            var globalBar = BuildProgressBar(globalPct);
            embed.AddField(
                "🌍 Global",
                $"{globalBar} {globalPct}%\n{totalChecked}/{totalLocations} locations",
                inline: false
            );

            embed.WithTimestamp(DateTimeOffset.UtcNow);
            await FollowupAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Failed to get progress: {ex.Message}", ephemeral: true);
        }
    }

    private static string BuildProgressBar(double percentage, int length = 10)
    {
        var filled = Math.Clamp((int)Math.Round(percentage / 100 * length), 0, length);
        var empty = length - filled;
        return $"[{new string('█', filled)}{new string('░', empty)}]";
    }

    private static List<string> SplitIntoPages(IEnumerable<string> lines, int maxLength = 2048)
    {
        var pages = new List<string>();
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            if (sb.Length + line.Length + 1 > maxLength)
            {
                pages.Add(sb.ToString());
                sb.Clear();
            }
            sb.AppendLine(line);
        }

        if (sb.Length > 0)
        {
            pages.Add(sb.ToString());
        }

        return pages;
    }
}
