namespace ComputerSetupWizard;

/// <summary>
/// Git and Claude run the wizard by path, so it copies itself to a fixed per-user location
/// instead of being used from wherever it was downloaded.
/// </summary>
public static class AppInstall
{
    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ComputerSetupWizard");

    public static string InstalledExe => Path.Combine(InstallDirectory, "ComputerSetupWizard.exe");

    static string CurrentExe => Environment.ProcessPath!;

    /// <summary>
    /// True for a dotnet build (exe plus DLLs), which can't be copied as one file.
    /// Those builds use themselves in place. A single-file publish extracts its DLLs to a temp
    /// folder, so look next to the exe rather than in <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    static bool IsDevelopmentBuild => File.Exists(Path.Combine(Path.GetDirectoryName(CurrentExe)!, "ComputerSetupWizard.dll"));

    /// <summary>The exe that git and Claude should run, if it has been installed.</summary>
    public static string? ExistingExe =>
        IsDevelopmentBuild ? CurrentExe : File.Exists(InstalledExe) ? InstalledExe : null;

    /// <summary>The exe git and Claude should run: where it would be installed, even if it isn't yet.</summary>
    public static string TargetExe => IsDevelopmentBuild ? CurrentExe : InstalledExe;

    /// <summary>Copies this exe to <see cref="InstalledExe"/> if it isn't running from there, and returns the exe to use.</summary>
    public static string EnsureInstalled()
    {
        if (IsDevelopmentBuild || string.Equals(CurrentExe, InstalledExe, StringComparison.OrdinalIgnoreCase))
            return CurrentExe;

        Directory.CreateDirectory(InstallDirectory);
        File.Copy(CurrentExe, InstalledExe, overwrite: true);
        return InstalledExe;
    }
}
