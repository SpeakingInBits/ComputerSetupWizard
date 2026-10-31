namespace ComputerSetupWizard.Core;

/// <summary>Fixed settings for the SpeakingInBits-Bot GitHub App. See docs/bot-setup.md.</summary>
public static class BotConfig
{
    public const string AppId = "5450211";
    public const string Org = "SpeakingInBits";
    public const string CommitName = "speakinginbits-bot[bot]";
    public const string CommitEmail = "347181781+speakinginbits-bot[bot]@users.noreply.github.com";
    public const string AppSettingsUrl = "https://github.com/organizations/SpeakingInBits/settings/apps/speakinginbits-bot";

    public const string DefaultKeyDirectory = @"C:\Dev";
    public const string KeyPattern = "speakinginbits-bot.*.private-key.pem";

    /// <summary>
    /// The user environment variable that holds this machine's key folder when it isn't
    /// <see cref="DefaultKeyDirectory"/>. Get-BotToken.ps1 reads it too.
    /// </summary>
    public const string KeyDirectoryVariable = "SPEAKINGINBITS_BOT_KEY";

    /// <summary>
    /// The folder this machine keeps its key in. The saved user variable is read straight from the
    /// registry as well, so git and terminals started before it changed still find the key.
    /// </summary>
    public static string KeyDirectory => ResolveKeyDirectory(
        Environment.GetEnvironmentVariable(KeyDirectoryVariable),
        Environment.GetEnvironmentVariable(KeyDirectoryVariable, EnvironmentVariableTarget.User));

    internal static string ResolveKeyDirectory(string? process, string? user) =>
        new[] { process, user }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? DefaultKeyDirectory;

    /// <summary>
    /// Saves <paramref name="directory"/> as this machine's key folder, for this process and for
    /// programs started from now on. Choosing <see cref="DefaultKeyDirectory"/> removes the variable.
    /// </summary>
    public static void SetKeyDirectory(string directory)
    {
        var value = string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), DefaultKeyDirectory,
            StringComparison.OrdinalIgnoreCase) ? null : directory;
        Environment.SetEnvironmentVariable(KeyDirectoryVariable, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(KeyDirectoryVariable, value);
    }

    /// <summary>The newest bot private key in the key folder, or null if there is none.</summary>
    public static string? FindNewestKey() => FindNewestKey(KeyDirectory);

    /// <summary>The newest bot private key in <paramref name="directory"/>, or null if there is none.</summary>
    public static string? FindNewestKey(string directory) =>
        Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles(KeyPattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName
            : null;
}
