using System.Text.RegularExpressions;

namespace ComputerSetupWizard.Core;

public sealed record GitHubLogin(bool SignedIn, string? Account, string Details);

/// <summary>Signs in to GitHub with gh and manages the personal global Git identity.</summary>
public static partial class GitHubCliService
{
    // A bot token in the environment would hide the user's own gh login
    static readonly Dictionary<string, string?> WithoutTokens = new() { ["GH_TOKEN"] = null, ["GITHUB_TOKEN"] = null };

    public static bool IsGitInstalled => ToolPaths.Find("git") is not null;
    public static bool IsGhInstalled => ToolPaths.Find("gh") is not null;

    public static async Task<GitHubLogin> GetLoginAsync(CancellationToken ct = default)
    {
        if (!IsGhInstalled)
            return new GitHubLogin(false, null, "The GitHub CLI isn't installed.");

        var result = await ProcessRunner.RunAsync(
            "gh", ["auth", "status", "--hostname", "github.com", "--active"], environment: WithoutTokens, ct: ct);
        var account = AccountPattern().Match(result.AllText);
        return new GitHubLogin(result.Succeeded, account.Success ? account.Groups[1].Value : null, result.AllText);
    }

    /// <summary>Opens <c>gh auth login</c> in its own window and waits for the user to finish.</summary>
    public static Task<int> SignInAsync() =>
        ProcessRunner.RunInConsoleWindowAsync(
            "gh", ["auth", "login", "--web", "--hostname", "github.com", "--git-protocol", "https"], WithoutTokens);

    /// <summary>Makes Git use gh for github.com credentials.</summary>
    public static Task<ProcessResult> SetupGitAsync(CancellationToken ct = default) =>
        ProcessRunner.RunAsync("gh", ["auth", "setup-git", "--hostname", "github.com"], environment: WithoutTokens, ct: ct);

    /// <summary>
    /// The signed-in user's display name (or login) and GitHub noreply address, for prefilling the Git identity.
    /// </summary>
    public static async Task<(string? Name, string? Email)> GetSuggestedIdentityAsync(CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(
            "gh",
            ["api", "user", "--jq", @"(.name // .login) + ""\n"" + (.id|tostring) + ""+"" + .login + ""@users.noreply.github.com"""],
            environment: WithoutTokens,
            ct: ct);
        return result.Succeeded && result.Output.Split('\n', StringSplitOptions.TrimEntries) is [var name, var email]
            ? (name, email)
            : (null, null);
    }

    public static async Task<(string? Name, string? Email)> GetGlobalIdentityAsync(CancellationToken ct = default) =>
        (await GitConfig.GetAsync(null, ["--global", "user.name"], ct),
         await GitConfig.GetAsync(null, ["--global", "user.email"], ct));

    public static async Task SetGlobalIdentityAsync(string name, string email, CancellationToken ct = default)
    {
        await GitConfig.SetAsync(null, ["--global", "user.name", name], ct);
        await GitConfig.SetAsync(null, ["--global", "user.email", email], ct);
    }

    [GeneratedRegex(@"account (\S+)")]
    private static partial Regex AccountPattern();
}
