namespace ComputerSetupWizard.Core;

public sealed record WingetPackage(string Id, string Name, string Description, bool SelectedByDefault);

public static class WingetService
{
    /// <summary>The packages the wizard offers.</summary>
    public static readonly IReadOnlyList<WingetPackage> Packages =
    [
        new("Git.Git", "Git", "Version control. Includes Git Credential Manager.", SelectedByDefault: true),
        new("GitHub.cli", "GitHub CLI", "gh, for signing in to GitHub and opening pull requests.", SelectedByDefault: true),
        new("Anthropic.ClaudeCode", "Claude Code", "The claude command line. The Claude Code step uses it to install mods.", SelectedByDefault: true),
        new("Microsoft.DotNet.SDK.10", ".NET 10 SDK", "Builds .NET apps, including this wizard and Godot .NET projects.", SelectedByDefault: true),
        new(VisualStudioService.PackageId, "Visual Studio Community 2026",
            "With the ASP.NET and web development, and .NET MAUI workloads. Takes a while.", SelectedByDefault: true),
        new("Microsoft.SQLServer.2025.Developer", "SQL Server 2025 Developer",
            "Full-featured, free for development and test. Not licensed for production.", SelectedByDefault: true),
        new("BlenderFoundation.Blender", "Blender", "3D modeling and animation.", SelectedByDefault: true),
        new("GodotEngine.GodotEngine.Mono", "Godot Engine (.NET)", "Godot with C# support. Uses the .NET SDK.", SelectedByDefault: true),
        new("OpenJS.NodeJS.LTS", "Node.js LTS", "JavaScript runtime with npm, for web front ends and JavaScript tools.", SelectedByDefault: false),
        new("Microsoft.PowerShell", "PowerShell 7", "The current cross-platform PowerShell.", SelectedByDefault: false),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Code editor.", SelectedByDefault: false),
    ];

    public static bool IsAvailable => ToolPaths.Find("winget") is not null;

    public static Task<bool> IsInstalledAsync(WingetPackage package, CancellationToken ct = default) =>
        package.Id == VisualStudioService.PackageId
            ? VisualStudioService.IsInstalledAsync(ct)
            : IsInstalledAsync(package.Id, ct);

    /// <summary>Installs a package silently. This accepts the package's license agreement.</summary>
    public static Task<ProcessResult> InstallAsync(WingetPackage package, Action<string>? onOutput = null, CancellationToken ct = default) =>
        package.Id == VisualStudioService.PackageId
            ? VisualStudioService.InstallAsync(onOutput, ct)
            : InstallAsync(package.Id, installerArguments: null, onOutput, ct);

    public static async Task<bool> IsInstalledAsync(string id, CancellationToken ct = default)
    {
        // winget list exits non-zero when no installed package matches
        var result = await ProcessRunner.RunAsync(
            "winget",
            ["list", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity"],
            ct: ct);
        return result.Succeeded;
    }

    /// <param name="installerArguments">Replaces the package's own installer arguments (winget --override).</param>
    public static Task<ProcessResult> InstallAsync(
        string id, string? installerArguments, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        string[] arguments =
        [
            "install", "--id", id, "--exact", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        ];
        arguments = installerArguments is null ? [.. arguments, "--silent"] : [.. arguments, "--override", installerArguments];
        return ProcessRunner.RunAsync("winget", arguments, onOutput: onOutput, ct: ct);
    }
}
