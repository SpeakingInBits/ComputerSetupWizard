using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public class BotIdentityServiceTests
{
    const string Exe = @"C:\Users\me\AppData\Local\ComputerSetupWizard\current\ComputerSetupWizard.exe";
    const string Gh = "!'C:\\Program Files\\GitHub CLI\\gh.exe' auth git-credential";

    [Fact]
    public void AnyCase() => Assert.Equal("[Aa]-[Bb]1", BotIdentityService.AnyCase("a-B1"));

    [Fact]
    public void IsBot_WhenTheWizardIsTheLastHelper() =>
        Assert.True(BotIdentityService.IsBot(
            BotConfig.CommitName, BotConfig.CommitEmail, ["", Gh, "", BotIdentityService.HelperCommand(Exe)], Exe));

    [Fact]
    public void IsBot_NotWhenGhComesAfterTheInclude() =>
        Assert.False(BotIdentityService.IsBot(
            BotConfig.CommitName, BotConfig.CommitEmail, ["", BotIdentityService.HelperCommand(Exe), "", Gh], Exe));

    [Fact]
    public void IsBot_NotForAMovedExe() =>
        Assert.False(BotIdentityService.IsBot(
            BotConfig.CommitName, BotConfig.CommitEmail, ["", BotIdentityService.HelperCommand(@"C:\Downloads\ComputerSetupWizard.exe")], Exe));

    [Fact]
    public void IsBot_NotWithAPersonalIdentity() =>
        Assert.False(BotIdentityService.IsBot("Someone", "someone@example.com", ["", BotIdentityService.HelperCommand(Exe)], Exe));

    [Theory]
    [InlineData("https://github.com/SpeakingInBits/ComputerSetupWizard.git", true)]
    [InlineData("https://github.com/speakinginbits/repo", true)]
    [InlineData("https://Joe-Ortiz@github.com/SpeakingInBits/repo.git", true)]
    [InlineData("git@github.com:SpeakingInBits/repo.git", true)]
    [InlineData("ssh://git@github.com/SpeakingInBits/repo.git", true)]
    [InlineData("https://github.com/Joe-Ortiz/repo.git", false)]
    [InlineData("https://github.com/SpeakingInBitsFork/repo.git", false)]
    public async Task Conditions_MatchOrgRemotesInGit(string url, bool expected)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var include = Path.Combine(dir, "bot.ini").Replace('\\', '/');
            File.WriteAllText(include, "[user]\n\tname = bot\n");
            var global = Path.Combine(dir, "global.ini");
            File.WriteAllText(global, string.Concat(BotIdentityService.Conditions.Select(c => $"[includeIf \"{c}\"]\n\tpath = {include}\n")));

            var result = await ProcessRunner.RunAsync(
                "git", ["-C", dir, "-c", $"remote.origin.url={url}", "config", "--get", "user.name"],
                environment: new Dictionary<string, string?> { ["GIT_CONFIG_GLOBAL"] = global, ["GIT_CONFIG_NOSYSTEM"] = "1" });

            Assert.Equal(expected, result.Output == "bot");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
