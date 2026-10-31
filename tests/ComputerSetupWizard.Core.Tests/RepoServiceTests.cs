using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public class RepoServiceTests
{
    const string Exe = @"C:\Users\me\AppData\Local\ComputerSetupWizard\current\ComputerSetupWizard.exe";

    [Theory]
    [InlineData("https://github.com/SpeakingInBits/ComputerSetupWizard", true)]
    [InlineData("https://github.com/speakinginbits/repo.git", true)]
    [InlineData("git@github.com:SpeakingInBits/repo.git", true)]
    [InlineData("https://github.com/Joe-Ortiz/repo", false)]
    [InlineData("https://github.com/SpeakingInBitsFork/repo", false)]
    [InlineData("https://gitlab.com/SpeakingInBits/repo", false)]
    public void IsOrgRemote(string url, bool expected) => Assert.Equal(expected, RepoService.IsOrgRemote(url));

    [Fact]
    public void GetLocalSettings_FindsTheOldBotSetup()
    {
        string[] helpers = ["", BotIdentityService.HelperCommand(Exe)];
        Assert.Equal(new(true, true, true), RepoService.GetLocalSettings(BotConfig.CommitName, BotConfig.CommitEmail, helpers));
    }

    [Fact]
    public void GetLocalSettings_FindsAHelperForAMovedExe()
    {
        string[] helpers = ["", BotIdentityService.HelperCommand(@"C:\Downloads\ComputerSetupWizard.exe")];
        Assert.True(RepoService.GetLocalSettings(null, null, helpers).Helper);
    }

    [Fact]
    public void GetLocalSettings_LeavesOtherSettingsAlone()
    {
        string[] helpers = ["", "!'C:\\Program Files\\GitHub CLI\\gh.exe' auth git-credential"];
        Assert.False(RepoService.GetLocalSettings("Someone", "someone@example.com", helpers).Any);
    }

    [Fact]
    public void FindRepositories_StopsAtRepos()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "a", "nested", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "group", "b"));
            File.WriteAllText(Path.Combine(root, "group", "b", ".git"), "gitdir: elsewhere");
            Directory.CreateDirectory(Path.Combine(root, "node_modules", "c", ".git"));

            var repos = RepoService.FindRepositories([root]).Select(p => Path.GetRelativePath(root, p)).Order().ToList();

            Assert.Equal(["a", Path.Combine("group", "b")], repos);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
