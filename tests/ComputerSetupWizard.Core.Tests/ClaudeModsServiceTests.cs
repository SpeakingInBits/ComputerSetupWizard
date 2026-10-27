using System.Text.Json.Nodes;
using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public class ClaudeModsServiceTests
{
    [Fact]
    public void EnableInSettings_CreatesSettings()
    {
        var settings = JsonNode.Parse(ClaudeModsService.EnableInSettings("", "session-spend"))!;

        Assert.Equal("github", (string?)settings["extraKnownMarketplaces"]!["speakinginbits-mods"]!["source"]!["source"]);
        Assert.Equal("SpeakingInBits/claude-mods", (string?)settings["extraKnownMarketplaces"]!["speakinginbits-mods"]!["source"]!["repo"]);
        Assert.True((bool)settings["enabledPlugins"]!["session-spend@speakinginbits-mods"]!);
    }

    [Fact]
    public void EnableInSettings_KeepsOtherSettings()
    {
        const string existing = """
            {
              // comments are allowed in settings
              "model": "opus",
              "enabledPlugins": { "other@market": false },
              "extraKnownMarketplaces": { "market": { "source": { "source": "github", "repo": "a/b" } } },
            }
            """;

        var settings = JsonNode.Parse(ClaudeModsService.EnableInSettings(existing, "session-spend"))!;

        Assert.Equal("opus", (string?)settings["model"]);
        Assert.False((bool)settings["enabledPlugins"]!["other@market"]!);
        Assert.True((bool)settings["enabledPlugins"]!["session-spend@speakinginbits-mods"]!);
        Assert.Equal("a/b", (string?)settings["extraKnownMarketplaces"]!["market"]!["source"]!["repo"]);
        Assert.NotNull(settings["extraKnownMarketplaces"]!["speakinginbits-mods"]);
    }

    [Fact]
    public void EnableInSettings_IsStable()
    {
        var once = ClaudeModsService.EnableInSettings("{}", "session-spend");
        Assert.Equal(once, ClaudeModsService.EnableInSettings(once, "session-spend"));
    }
}
