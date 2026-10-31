using System.Text.RegularExpressions;

namespace ComputerSetupWizard.Core;

/// <summary>
/// Finds SpeakingInBits clones that older versions of the wizard set up one by one, with the bot's
/// settings in their local config, and removes those settings so the global setup applies to them.
/// </summary>
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
            BotConfig.DefaultKeyDirectory,
            @"C:\src",
        ];
        return candidates.Where(Directory.Exists).ToList();
    }

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

    /// <summary>The SpeakingInBits repos under <paramref name="roots"/> that still have bot settings in their local config.</summary>
    public static async Task<IReadOnlyList<string>> FindLocalSetupsAsync(IEnumerable<string> roots, CancellationToken ct = default)
    {
        var repos = new List<string>();
        foreach (var path in FindRepositories(roots))
        {
            ct.ThrowIfCancellationRequested();
            if (await HasOrgRemoteAsync(path, ct) && (await GetLocalSettingsAsync(path, ct)).Any)
                repos.Add(path);
        }
        return repos.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static async Task<bool> HasOrgRemoteAsync(string path, CancellationToken ct)
    {
        var remotes = await ProcessRunner.RunAsync(
            "git", ["-C", path, "config", "--get-regexp", @"^remote\..*\.url$"], ct: ct);
        return remotes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.Split(' ', 2) is [_, var url] && IsOrgRemote(url));
    }

    internal sealed record LocalSettings(bool Name, bool Email, bool Helper)
    {
        public bool Any => Name || Email || Helper;
    }

    internal static LocalSettings GetLocalSettings(string? name, string? email, string[] helpers) =>
        new(name == BotConfig.CommitName,
            email == BotConfig.CommitEmail,
            helpers.Any(h => h.EndsWith("ComputerSetupWizard.exe' git-credential", StringComparison.OrdinalIgnoreCase)));

    static async Task<LocalSettings> GetLocalSettingsAsync(string path, CancellationToken ct) =>
        GetLocalSettings(
            await GitConfig.GetAsync(path, ["--local", "user.name"], ct),
            await GitConfig.GetAsync(path, ["--local", "user.email"], ct),
            await GitConfig.GetAllAsync(path, ["--local", HelperKey], ct));

    /// <summary>
    /// Removes the bot settings an older wizard put in the repo's local config. Anything else set
    /// there, such as a different local identity, stays.
    /// </summary>
    public static async Task RemoveLocalSetupAsync(string path, CancellationToken ct = default)
    {
        var settings = await GetLocalSettingsAsync(path, ct);
        if (settings.Name)
            await GitConfig.UnsetAllAsync(path, "--local", "user.name", ct);
        if (settings.Email)
            await GitConfig.UnsetAllAsync(path, "--local", "user.email", ct);
        if (settings.Helper)
            await GitConfig.UnsetAllAsync(path, "--local", HelperKey, ct);
    }

    [GeneratedRegex(@"github\.com[:/]" + BotConfig.Org + "/", RegexOptions.IgnoreCase)]
    private static partial Regex OrgRemotePattern();
}
