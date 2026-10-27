using System.Text.RegularExpressions;

namespace ComputerSetupWizard.Core;

public enum RepoState
{
    /// <summary>Uses the personal identity and credentials.</summary>
    Personal,

    /// <summary>Commits as the bot and pushes with bot tokens from the wizard.</summary>
    Bot,

    /// <summary>Has some bot settings but not all, or points at a wizard exe that moved.</summary>
    Partial,
}

public sealed record RepoInfo(string Path, string RemoteUrl, RepoState State);

/// <summary>Finds SpeakingInBits repos on the machine and switches them between the personal and bot identities.</summary>
public static partial class RepoService
{
    const string HelperKey = "credential.https://github.com.helper";
    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "packages" };

    /// <summary>The usual places for repos that exist on this machine.</summary>
    public static IReadOnlyList<string> DefaultScanRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Path.Combine(home, "source", "repos"),
            Path.Combine(home, "repos"),
            Path.Combine(home, "code"),
            Path.Combine(home, "Projects"),
            Path.Combine(home, "Documents", "GitHub"),
            BotConfig.KeyDirectory,
            @"C:\src",
        ];
        return candidates.Where(Directory.Exists).ToList();
    }

    /// <summary>
    /// The git credential helper entry that makes the wizard at <paramref name="exePath"/> answer for github.com.
    /// Git runs it through sh, so the path is single-quoted like gh's own entry.
    /// </summary>
    public static string HelperCommand(string exePath) => $"!'{exePath}' git-credential";

    public static bool IsOrgRemote(string url) => OrgRemotePattern().IsMatch(url);

    /// <summary>Folders under <paramref name="roots"/> that are Git working trees, without looking inside them.</summary>
    public static IEnumerable<string> FindRepositories(IEnumerable<string> roots, int maxDepth = 3)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Path, int Depth)>(roots.Where(Directory.Exists).Select(r => (Path.GetFullPath(r), 0)));

        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Dequeue();
            if (!seen.Add(dir))
                continue;

            // .git is a folder in a clone and a file in a worktree
            var gitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                yield return dir;
                continue;
            }
            if (depth >= maxDepth)
                continue;

            string[] children;
            try
            {
                children = Directory.GetDirectories(dir);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (!name.StartsWith('.') && !SkippedFolders.Contains(name))
                    pending.Enqueue((child, depth + 1));
            }
        }
    }

    /// <summary>The SpeakingInBits repos under <paramref name="roots"/> and how each is set up.</summary>
    public static async Task<IReadOnlyList<RepoInfo>> ScanAsync(IEnumerable<string> roots, string exePath, CancellationToken ct = default)
    {
        var repos = new List<RepoInfo>();
        foreach (var path in FindRepositories(roots))
        {
            ct.ThrowIfCancellationRequested();
            var info = await GetInfoAsync(path, exePath, ct);
            if (info is not null)
                repos.Add(info);
        }
        return repos.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>How <paramref name="path"/> is set up, or null if it has no SpeakingInBits remote.</summary>
    public static async Task<RepoInfo?> GetInfoAsync(string path, string exePath, CancellationToken ct = default)
    {
        var remotes = await ProcessRunner.RunAsync(
            "git", ["-C", path, "config", "--get-regexp", @"^remote\..*\.url$"], ct: ct);
        var orgUrl = remotes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2) is [_, var url] ? url : "")
            .FirstOrDefault(IsOrgRemote);
        if (orgUrl is null)
            return null;

        var name = await GitConfig.GetAsync(path, ["--local", "user.name"], ct);
        var email = await GitConfig.GetAsync(path, ["--local", "user.email"], ct);
        var helpers = await GitConfig.GetAllAsync(path, ["--local", HelperKey], ct);
        return new RepoInfo(path, orgUrl, GetState(name, email, helpers, exePath));
    }

    internal static RepoState GetState(string? name, string? email, string[] helpers, string exePath)
    {
        var identity = name == BotConfig.CommitName && email == BotConfig.CommitEmail;
        var credentials = helpers.Length == 2 && helpers[0] == ""
            && string.Equals(helpers[1], HelperCommand(exePath), StringComparison.OrdinalIgnoreCase);

        if (identity && credentials)
            return RepoState.Bot;
        if (name == BotConfig.CommitName || email == BotConfig.CommitEmail || helpers.Any(h => h.EndsWith(" git-credential")))
            return RepoState.Partial;
        return RepoState.Personal;
    }

    /// <summary>
    /// Sets the bot's commit identity in the repo's local config, and replaces the global github.com
    /// credential helpers (an empty entry resets the list) with the wizard, so pushes get bot tokens.
    /// </summary>
    public static async Task ConfigureForBotAsync(string path, string exePath, CancellationToken ct = default)
    {
        await GitConfig.SetAsync(path, ["--local", "user.name", BotConfig.CommitName], ct);
        await GitConfig.SetAsync(path, ["--local", "user.email", BotConfig.CommitEmail], ct);
        await GitConfig.UnsetAllAsync(path, "--local", HelperKey, ct);
        await GitConfig.SetAsync(path, ["--local", "--add", HelperKey, ""], ct);
        await GitConfig.SetAsync(path, ["--local", "--add", HelperKey, HelperCommand(exePath)], ct);
    }

    /// <summary>Removes the bot settings, so the repo goes back to the global personal identity.</summary>
    public static async Task ResetToPersonalAsync(string path, CancellationToken ct = default)
    {
        await GitConfig.UnsetAllAsync(path, "--local", "user.name", ct);
        await GitConfig.UnsetAllAsync(path, "--local", "user.email", ct);
        await GitConfig.UnsetAllAsync(path, "--local", HelperKey, ct);
    }

    [GeneratedRegex(@"github\.com[:/]" + BotConfig.Org + "/", RegexOptions.IgnoreCase)]
    private static partial Regex OrgRemotePattern();
}
