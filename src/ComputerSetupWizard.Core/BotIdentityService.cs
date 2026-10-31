namespace ComputerSetupWizard.Core;

public enum BotIdentityState
{
    /// <summary>The global Git config doesn't include the bot settings.</summary>
    Missing,

    /// <summary>
    /// The include is there, but SpeakingInBits repos don't end up with the bot's settings: the wizard
    /// moved, the settings file is gone, or global settings written after the include override it.
    /// </summary>
    Outdated,

    /// <summary>Every repo with a SpeakingInBits remote commits as the bot and pushes with bot tokens.</summary>
    Current,
}

/// <summary>
/// Makes every repo with a SpeakingInBits remote use the bot, from the global Git config. The bot's
/// settings live in their own file, which the global config includes only for repos whose remote is in
/// the org (<c>includeIf "hasconfig:remote.*.url:..."</c>, Git 2.36 and later). Other repos keep the
/// personal identity and gh's credentials.
/// </summary>
public static class BotIdentityService
{
    const string HelperKey = "credential.https://github.com.helper";

    public static string IncludeFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig-speakinginbits");

    /// <summary>
    /// The remote URL forms GitHub clones use, with the org matched in any case like
    /// <see cref="RepoService.IsOrgRemote"/>; hasconfig globs are case-sensitive.
    /// </summary>
    public static IReadOnlyList<string> Conditions { get; } =
        new[] { "https://github.com/", "https://*@github.com/", "git@github.com:", "ssh://git@github.com/" }
            .Select(prefix => $"hasconfig:remote.*.url:{prefix}{AnyCase(BotConfig.Org)}/**")
            .ToList();

    /// <summary>
    /// The git credential helper entry that makes the wizard at <paramref name="exePath"/> answer for github.com.
    /// Git runs it through sh, so the path is single-quoted like gh's own entry.
    /// </summary>
    public static string HelperCommand(string exePath) => $"!'{exePath}' git-credential";

    internal static string AnyCase(string text) =>
        string.Concat(text.Select(c => char.IsLetter(c) ? $"[{char.ToUpperInvariant(c)}{char.ToLowerInvariant(c)}]" : c.ToString()));

    static string IncludeKey(string condition) => $"includeIf.{condition}.path";

    // Git reads include paths with forward slashes, and an absolute one doesn't depend on HOME
    static string IncludeValue => IncludeFile.Replace('\\', '/');

    public static async Task<BotIdentityState> GetStateAsync(string exePath, CancellationToken ct = default)
    {
        var included = 0;
        foreach (var condition in Conditions)
        {
            if (await GitConfig.GetAsync(null, ["--global", IncludeKey(condition)], ct) == IncludeValue)
                included++;
        }
        if (included == 0)
            return BotIdentityState.Missing;
        if (included < Conditions.Count)
            return BotIdentityState.Outdated;

        var (name, email, helpers) = await ProbeAsync(ct);
        return IsBot(name, email, helpers, exePath) ? BotIdentityState.Current : BotIdentityState.Outdated;
    }

    /// <summary>Whether settings that a SpeakingInBits repo ends up with make it commit and push as the bot.</summary>
    internal static bool IsBot(string? name, string? email, string[] helpers, string exePath)
    {
        // An empty helper resets the list, so only the entries after the last one are used
        var used = helpers[(Array.LastIndexOf(helpers, "") + 1)..];
        return name == BotConfig.CommitName && email == BotConfig.CommitEmail
            && used is [var helper] && string.Equals(helper, HelperCommand(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What a repo with a SpeakingInBits remote would get from the system and global config. The remote
    /// comes from the command line, outside any repo, so no local config gets in the way.
    /// </summary>
    static async Task<(string? Name, string? Email, string[] Helpers)> ProbeAsync(CancellationToken ct)
    {
        string[] probe = ["-C", Path.GetTempPath(), "-c", $"remote.origin.url=https://github.com/{BotConfig.Org}/probe.git", "config"];
        var name = await ProcessRunner.RunAsync("git", [.. probe, "--get", "user.name"], ct: ct);
        var email = await ProcessRunner.RunAsync("git", [.. probe, "--get", "user.email"], ct: ct);
        var helpers = await ProcessRunner.RunAsync("git", [.. probe, "--get-all", HelperKey], ct: ct);
        return (name.Succeeded ? name.Output : null,
                email.Succeeded ? email.Output : null,
                helpers.Succeeded ? helpers.Output.Split('\n', StringSplitOptions.TrimEntries) : []);
    }

    /// <summary>
    /// Writes the bot's settings to <see cref="IncludeFile"/> and includes it at the end of the global
    /// config. Git keeps the last value it reads, so the include has to come after the personal identity
    /// and gh's credential helper.
    /// </summary>
    public static async Task SetUpAsync(string exePath, CancellationToken ct = default)
    {
        File.Delete(IncludeFile);
        string[] file = ["--file", IncludeFile];
        await GitConfig.SetAsync(null, [.. file, "user.name", BotConfig.CommitName], ct);
        await GitConfig.SetAsync(null, [.. file, "user.email", BotConfig.CommitEmail], ct);
        await GitConfig.SetAsync(null, [.. file, "--add", HelperKey, ""], ct);
        await GitConfig.SetAsync(null, [.. file, "--add", HelperKey, HelperCommand(exePath)], ct);

        await RemoveIncludesAsync(ct);
        foreach (var condition in Conditions)
            await GitConfig.SetAsync(null, ["--global", IncludeKey(condition), IncludeValue], ct);
    }

    /// <summary>
    /// Moves the include back to the end of the global config, if it's set up. Call this after writing
    /// global settings that could land after it, such as gh's credential helper or a new [user] section.
    /// </summary>
    public static async Task KeepLastAsync(string exePath, CancellationToken ct = default)
    {
        if (await GetStateAsync(exePath, ct) != BotIdentityState.Missing)
            await SetUpAsync(exePath, ct);
    }

    /// <summary>Removes the include and the bot's settings file, so every repo uses the personal identity.</summary>
    public static async Task RemoveAsync(CancellationToken ct = default)
    {
        await RemoveIncludesAsync(ct);
        File.Delete(IncludeFile);
    }

    static async Task RemoveIncludesAsync(CancellationToken ct)
    {
        // Git drops a section when its last entry goes, so setting it again puts it at the end.
        // --remove-section can't find names with a ] in them, such as the any-case org.
        foreach (var condition in Conditions)
            await GitConfig.UnsetAllAsync(null, "--global", IncludeKey(condition), ct);
    }
}
