namespace ComputerSetupWizard.Core;

/// <summary>Fixed settings for the SpeakingInBits-Bot GitHub App. See docs/bot-setup.md.</summary>
public static class BotConfig
{
    public const string AppId = "5450211";
    public const string Org = "SpeakingInBits";
    public const string CommitName = "speakinginbits-bot[bot]";
    public const string CommitEmail = "347181781+speakinginbits-bot[bot]@users.noreply.github.com";
    public const string AppSettingsUrl = "https://github.com/organizations/SpeakingInBits/settings/apps/speakinginbits-bot";

    public const string KeyDirectory = @"C:\Dev";
    public const string KeyPattern = "speakinginbits-bot.*.private-key.pem";

    /// <summary>The newest bot private key in <paramref name="directory"/>, or null if there is none.</summary>
    public static string? FindNewestKey(string directory = KeyDirectory) =>
        Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles(KeyPattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName
            : null;
}
