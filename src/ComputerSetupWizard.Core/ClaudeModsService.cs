using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComputerSetupWizard.Core;

public sealed record ClaudeMod(string Name, string Description);

/// <summary>
/// Installs Claude Code mods (plugins) from the SpeakingInBits/claude-mods marketplace at user scope, so they
/// run in every session in the terminal, the desktop app and VS Code. See
/// https://code.claude.com/docs/en/plugins/install.md.
/// </summary>
public static class ClaudeModsService
{
    /// <summary>The marketplace's name from its marketplace.json, which plugin IDs use.</summary>
    public const string MarketplaceName = "speakinginbits-mods";
    public const string MarketplaceRepo = "SpeakingInBits/claude-mods";

    const string MarketplaceUrl = $"https://raw.githubusercontent.com/{MarketplaceRepo}/HEAD/.claude-plugin/marketplace.json";

    static readonly HttpClient Http = new();

    /// <summary>Used when the marketplace can't be read, such as when offline.</summary>
    static readonly IReadOnlyList<ClaudeMod> KnownMods =
    [
        new("session-spend", "A pixel-art card with the session's API spend, plan usage and subagents, plus a status line total"),
    ];

    static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string PluginId(string mod) => $"{mod}@{MarketplaceName}";

    /// <summary>The mods in the marketplace, read from GitHub so new mods show up without a new wizard.</summary>
    public static async Task<IReadOnlyList<ClaudeMod>> GetModsAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(MarketplaceUrl, ct));
            var mods = doc.RootElement.GetProperty("plugins").EnumerateArray()
                .Select(p => new ClaudeMod(
                    p.GetProperty("name").GetString()!,
                    p.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""))
                .ToList();
            return mods.Count > 0 ? mods : KnownMods;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            return KnownMods;
        }
    }

    /// <summary>True if user settings enable the mod, which both install routes below record.</summary>
    public static bool IsEnabled(string mod)
    {
        if (!File.Exists(SettingsPath))
            return false;
        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: ReadOptions);
            return settings?["enabledPlugins"]?[PluginId(mod)]?.GetValue<bool>() == true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs a mod at user scope with the claude CLI if it's installed. Otherwise declares the marketplace
    /// and enables the mod in ~/.claude/settings.json, and Claude Code fetches it when the next session starts.
    /// </summary>
    public static async Task<ProcessResult> InstallAsync(string mod, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (ToolPaths.Find("claude") is null)
        {
            WriteSettings(EnableInSettings(ReadSettings(), mod));
            var message = $"Enabled {PluginId(mod)} in {SettingsPath}. Claude Code installs it when the next session starts.";
            onOutput?.Invoke(message);
            return new ProcessResult(0, message, "");
        }

        // Adding a marketplace that's already there exits 0, so this is safe to repeat
        var add = await ProcessRunner.RunAsync(
            "claude", ["plugin", "marketplace", "add", MarketplaceRepo, "--scope", "user"], onOutput: onOutput, ct: ct);
        if (!add.Succeeded)
            return add;

        return await ProcessRunner.RunAsync(
            "claude", ["plugin", "install", PluginId(mod), "--scope", "user"], onOutput: onOutput, ct: ct);
    }

    /// <summary>
    /// Adds the marketplace to <c>extraKnownMarketplaces</c> and the mod to <c>enabledPlugins</c>,
    /// keeping every other setting. Comments in the file aren't kept.
    /// </summary>
    internal static string EnableInSettings(string json, string mod)
    {
        var settings = (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json, documentOptions: ReadOptions))?.AsObject()
            ?? new JsonObject();

        var marketplaces = settings["extraKnownMarketplaces"] as JsonObject ?? new JsonObject();
        settings["extraKnownMarketplaces"] = marketplaces;
        marketplaces[MarketplaceName] ??= new JsonObject
        {
            ["source"] = new JsonObject { ["source"] = "github", ["repo"] = MarketplaceRepo },
        };

        var plugins = settings["enabledPlugins"] as JsonObject ?? new JsonObject();
        settings["enabledPlugins"] = plugins;
        plugins[PluginId(mod)] = true;

        return settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    static string ReadSettings() => File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "";

    static void WriteSettings(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (File.Exists(SettingsPath))
            File.Copy(SettingsPath, SettingsPath + ".bak", overwrite: true);
        File.WriteAllText(SettingsPath, json);
    }
}
