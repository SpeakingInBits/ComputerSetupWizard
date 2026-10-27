namespace ComputerSetupWizard.Core;

/// <summary>Thin wrapper over <c>git config</c>, optionally in a specific repo.</summary>
public static class GitConfig
{
    // git config exits with 5 when asked to unset a key that isn't there
    const int KeyNotFound = 5;

    public static async Task<string?> GetAsync(string? repo, string[] arguments, CancellationToken ct = default)
    {
        var result = await RunAsync(repo, ["--get", .. arguments], ct);
        return result.Succeeded ? result.Output : null;
    }

    public static async Task<string[]> GetAllAsync(string? repo, string[] arguments, CancellationToken ct = default)
    {
        var result = await RunAsync(repo, ["--get-all", .. arguments], ct);
        return result.Succeeded ? result.Output.Split('\n', StringSplitOptions.TrimEntries) : [];
    }

    public static async Task SetAsync(string? repo, string[] arguments, CancellationToken ct = default) =>
        EnsureSucceeded(await RunAsync(repo, arguments, ct));

    public static async Task UnsetAllAsync(string? repo, string scope, string key, CancellationToken ct = default)
    {
        var result = await RunAsync(repo, [scope, "--unset-all", key], ct);
        if (result.ExitCode != KeyNotFound)
            EnsureSucceeded(result);
    }

    static Task<ProcessResult> RunAsync(string? repo, string[] arguments, CancellationToken ct) =>
        ProcessRunner.RunAsync("git", repo is null ? ["config", .. arguments] : ["-C", repo, "config", .. arguments], ct: ct);

    static void EnsureSucceeded(ProcessResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"git config failed: {result.AllText}");
    }
}
