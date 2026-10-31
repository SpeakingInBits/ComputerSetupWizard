using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public sealed class WizardSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "csw-settings-" + Guid.NewGuid().ToString("N"));

    string SettingsFile => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_GivesDefaultsWithoutAFile()
    {
        var settings = WizardSettings.Load(SettingsFile);

        Assert.Empty(settings.ManuallyInstalled);
        Assert.Null(settings.BackupFolder);
        Assert.Empty(settings.SecretApps);
    }

    [Fact]
    public void Load_GivesDefaultsForABrokenFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, "{ not json");

        Assert.Empty(WizardSettings.Load(SettingsFile).ManuallyInstalled);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var settings = new WizardSettings { BackupFolder = @"D:\Backup", SecretApps = [new("AWS CLI", "credentials file")] };
        settings.SetManuallyInstalled("Anthropic.ClaudeCode", true);
        settings.Save(SettingsFile);

        var loaded = WizardSettings.Load(SettingsFile);

        Assert.True(loaded.IsManuallyInstalled("anthropic.claudecode"));
        Assert.Equal(@"D:\Backup", loaded.BackupFolder);
        Assert.Equal([new SecretApp("AWS CLI", "credentials file")], loaded.SecretApps);
    }

    [Fact]
    public void SetManuallyInstalled_AddsOnceAndRemoves()
    {
        var settings = new WizardSettings();
        settings.SetManuallyInstalled("Git.Git", true);
        settings.SetManuallyInstalled("git.git", true);
        Assert.Single(settings.ManuallyInstalled);

        settings.SetManuallyInstalled("GIT.GIT", false);
        Assert.False(settings.IsManuallyInstalled("Git.Git"));
    }
}
