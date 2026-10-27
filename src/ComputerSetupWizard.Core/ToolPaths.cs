namespace ComputerSetupWizard.Core;

/// <summary>Finds git, gh and winget, including ones installed after the app started.</summary>
public static class ToolPaths
{
    static readonly Dictionary<string, string[]> KnownLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["git"] = [@"%ProgramFiles%\Git\cmd\git.exe"],
        ["gh"] = [@"%ProgramFiles%\GitHub CLI\gh.exe"],
        ["winget"] = [@"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe"],
    };

    /// <summary>Reloads PATH from the registry, so tools that winget just installed are found.</summary>
    public static void RefreshPath()
    {
        var parts = new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User }
            .Select(target => Environment.GetEnvironmentVariable("Path", target))
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => Environment.ExpandEnvironmentVariables(path!));
        Environment.SetEnvironmentVariable("Path", string.Join(';', parts));
    }

    /// <summary>The full path of <paramref name="name"/>.exe, or null if it isn't installed.</summary>
    public static string? Find(string name)
    {
        if (Path.IsPathRooted(name))
            return File.Exists(name) ? name : null;

        var fileName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        var fromPath = (Environment.GetEnvironmentVariable("Path") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Path.Combine(dir, fileName))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null)
            return fromPath;

        return KnownLocations.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out var locations)
            ? locations.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists)
            : null;
    }

    public static string Resolve(string name) => Find(name) ?? name;
}
