namespace ComputerSetupWizard;

/// <summary>
/// Git and Claude run the wizard by path. Setup.exe installs it to %LOCALAPPDATA%\ComputerSetupWizard\current,
/// which stays the same when the wizard updates itself, so they use the exe that's running.
/// </summary>
public static class AppInstall
{
    /// <summary>The exe git and Claude should run.</summary>
    public static string Exe => Environment.ProcessPath!;

    public static string Directory => Path.GetDirectoryName(Exe)!;
}
