using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace ComputerSetupWizard;

/// <summary>
/// Updates the installed wizard from the GitHub releases with Velopack. The Release workflow publishes each
/// architecture's package and release list (releases.&lt;rid&gt;.json) there, and the wizard only looks at its own.
/// </summary>
public static class AppUpdater
{
    // "latest/download" always serves the newest release's files, without the GitHub API's limit of 60 requests an hour
    const string ReleaseFiles = "https://github.com/SpeakingInBits/ComputerSetupWizard/releases/latest/download/";

    // CSW_UPDATE_SOURCE, a folder or URL holding packaged releases, tries out an update before it's published
    static readonly UpdateManager Manager = Environment.GetEnvironmentVariable("CSW_UPDATE_SOURCE") is { Length: > 0 } source
        ? new(source)
        : new(new SimpleWebSource(ReleaseFiles));

    static UpdateInfo? _update;

    /// <summary>True when Setup.exe installed the wizard, so it can update itself; false for a dotnet build.</summary>
    public static bool IsInstalled => Manager.IsInstalled;

    /// <summary>
    /// Downloads the newest release if it's newer than this one and returns its version, or null when the wizard is
    /// up to date, isn't installed, or the check failed (offline).
    /// </summary>
    public static async Task<string?> DownloadUpdateAsync()
    {
        if (!Manager.IsInstalled)
            return null;

        try
        {
            _update = await Manager.CheckForUpdatesAsync();
            if (_update is null)
                return null;

            await Manager.DownloadUpdatesAsync(_update);
            return _update.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex)
        {
            // Not worth interrupting setup for; the wizard checks again next time it opens
            Trace.WriteLine($"Update check failed: {ex}");
            _update = null;
            return null;
        }
    }

    /// <summary>Closes the wizard, installs the downloaded update and opens the new version.</summary>
    public static void RestartToUpdate()
    {
        if (_update is not null)
            Manager.ApplyUpdatesAndRestart(_update.TargetFullRelease);
    }
}
