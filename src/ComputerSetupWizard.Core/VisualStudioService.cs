namespace ComputerSetupWizard.Core;

/// <summary>
/// Visual Studio 2026 Community with the workloads the wizard needs. winget skips a package that's already
/// installed, so an existing install that lacks a workload is changed with the Visual Studio Installer instead.
/// </summary>
public static class VisualStudioService
{
    public const string PackageId = "Microsoft.VisualStudio.Community";

    /// <summary>ASP.NET and web development, and .NET Multi-platform App UI development.</summary>
    public static readonly IReadOnlyList<string> Workloads =
    [
        "Microsoft.VisualStudio.Workload.NetWeb",
        "Microsoft.VisualStudio.Workload.NetCrossPlat",
    ];

    const string Product = "Microsoft.VisualStudio.Product.Community";
    const string Version2026 = "[18.0,19.0)";

    static string InstallerDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");

    /// <summary>Arguments for the Visual Studio Installer that add the workloads and run unattended with progress shown.</summary>
    public static string WorkloadArguments =>
        string.Join(' ', Workloads.Select(w => $"--add {w}")) + " --includeRecommended --passive --norestart --wait";

    /// <summary>True if VS 2026 Community is installed with every workload.</summary>
    public static async Task<bool> IsInstalledAsync(CancellationToken ct = default) =>
        await FindInstallAsync(Workloads, ct) is not null;

    /// <summary>
    /// Installs VS 2026 Community with the workloads, or adds the workloads to an existing install.
    /// The Visual Studio Installer asks for administrator approval itself.
    /// </summary>
    public static async Task<ProcessResult> InstallAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var existing = await FindInstallAsync([], ct);
        if (existing is null)
            return await WingetService.InstallAsync(PackageId, WorkloadArguments, onOutput, ct);

        onOutput?.Invoke($"Visual Studio is installed at {existing}; adding workloads with the Visual Studio Installer.");
        var exitCode = await ProcessRunner.RunElevatedAsync(
            Path.Combine(InstallerDirectory, "setup.exe"),
            $"modify --installPath \"{existing}\" {WorkloadArguments}");
        return new ProcessResult(exitCode, "", "");
    }

    /// <summary>The install path of VS 2026 Community with <paramref name="workloads"/>, or null.</summary>
    static async Task<string?> FindInstallAsync(IReadOnlyList<string> workloads, CancellationToken ct)
    {
        var vswhere = Path.Combine(InstallerDirectory, "vswhere.exe");
        if (!File.Exists(vswhere))
            return null;

        string[] arguments = ["-products", Product, "-version", Version2026, "-property", "installationPath"];
        if (workloads.Count > 0)
            arguments = [.. arguments, "-requires", .. workloads];

        var result = await ProcessRunner.RunAsync(vswhere, arguments, ct: ct);
        var path = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return result.Succeeded && path is not null ? path : null;
    }
}
