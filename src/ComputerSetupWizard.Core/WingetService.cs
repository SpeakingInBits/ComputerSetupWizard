namespace ComputerSetupWizard.Core;

public sealed record WingetPackage(string Id, string Name, string Description, bool Required);

public static class WingetService
{
    /// <summary>The packages the wizard offers. Required ones are selected by default.</summary>
    public static readonly IReadOnlyList<WingetPackage> Packages =
    [
        new("Git.Git", "Git", "Version control. Includes Git Credential Manager.", Required: true),
        new("GitHub.cli", "GitHub CLI", "gh, for signing in to GitHub and opening pull requests.", Required: true),
        new("Microsoft.PowerShell", "PowerShell 7", "The current cross-platform PowerShell.", Required: false),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Code editor.", Required: false),
        new("Microsoft.DotNet.SDK.10", ".NET 10 SDK", "Builds .NET apps, including this wizard.", Required: false),
    ];

    public static bool IsAvailable => ToolPaths.Find("winget") is not null;

    public static async Task<bool> IsInstalledAsync(string id, CancellationToken ct = default)
    {
        // winget list exits non-zero when no installed package matches
        var result = await ProcessRunner.RunAsync(
            "winget",
            ["list", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity"],
            ct: ct);
        return result.Succeeded;
    }

    /// <summary>Installs a package silently. This accepts the package's license agreement.</summary>
    public static Task<ProcessResult> InstallAsync(string id, Action<string>? onOutput = null, CancellationToken ct = default) =>
        ProcessRunner.RunAsync(
            "winget",
            ["install", "--id", id, "--exact", "--source", "winget", "--silent",
             "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            onOutput: onOutput,
            ct: ct);
}
