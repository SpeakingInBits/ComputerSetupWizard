namespace ComputerSetupWizard.Core;

public enum ClaudeMdState { Missing, Outdated, Current }

/// <summary>Keeps the bot section of ~/.claude/CLAUDE.md in step with where the wizard is installed.</summary>
public static class ClaudeMdService
{
    public const string Heading = "# SpeakingInBits GitHub org: use the bot identity";

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "CLAUDE.md");

    public static string BuildSection(string exePath) => $"""
        {Heading}

        For any repo in the SpeakingInBits GitHub org, make commits and open PRs as the SpeakingInBits-Bot GitHub App, not as my personal account.

        - ComputerSetupWizard (`{exePath}`) handles the bot. Never print a bot token or read the private key.
        - Every repo with a SpeakingInBits remote already commits as the bot and pushes with bot tokens, through `{BotIdentityService.IncludeFile}`, which the global Git config includes for those repos. Don't set `user.name`, `user.email` or credential helpers in their local config.
        - For `gh`, get a token (valid ~1 hour; get a fresh one if it expires): `$env:GH_TOKEN = & "{exePath}" token`.
        - `& "{exePath}" info` shows the app, installation and permissions without the token.
        - The bot can write contents, pull requests and workflows only. Ask me before working around anything outside those permissions.
        - Repos outside SpeakingInBits use my normal Git identity.
        """;

    public static ClaudeMdState GetState(string exePath)
    {
        if (!File.Exists(FilePath))
            return ClaudeMdState.Missing;
        var existing = File.ReadAllText(FilePath);
        if (!existing.Contains(Heading))
            return ClaudeMdState.Missing;
        return Merge(existing, BuildSection(exePath)) == existing ? ClaudeMdState.Current : ClaudeMdState.Outdated;
    }

    public static void Write(string exePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var existing = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
        File.WriteAllText(FilePath, Merge(existing, BuildSection(exePath)));
    }

    /// <summary>
    /// Replaces the bot section of <paramref name="existing"/> (from its heading to the next top-level
    /// heading) with <paramref name="section"/>, or appends it if there isn't one.
    /// </summary>
    internal static string Merge(string existing, string section)
    {
        var newline = existing.Contains("\r\n") ? "\r\n" : "\n";
        var sectionLines = section.ReplaceLineEndings("\n").Split('\n');
        var lines = existing.ReplaceLineEndings("\n").Split('\n').ToList();

        var start = lines.FindIndex(line => line.TrimEnd() == Heading);
        if (start < 0)
        {
            var trimmed = existing.TrimEnd();
            var prefix = trimmed.Length > 0 ? trimmed + newline + newline : "";
            return prefix + string.Join(newline, sectionLines) + newline;
        }

        var end = lines.FindIndex(start + 1, line => line.StartsWith("# "));
        if (end < 0)
            end = lines.Count;

        var after = lines.Skip(end).ToList();
        var merged = lines.Take(start)
            .Concat(sectionLines)
            .Concat(after.Count > 0 ? ["", .. after] : [""]);
        return string.Join(newline, merged);
    }
}
