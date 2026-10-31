using System.Text.Json;

namespace ComputerSetupWizard.Core;

/// <summary>An app that keeps a secret on this PC, such as an API key or a .env file, to move by hand.</summary>
public sealed record SecretApp(string Name, string Note)
{
    // Get-only, because the XAML compiler can't generate code for init-only setters
    public string Name { get; } = Name;
    public string Note { get; } = Note;
}

/// <summary>What the wizard remembers for this Windows user, in %APPDATA%\ComputerSetupWizard\settings.json.</summary>
public sealed class WizardSettings
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComputerSetupWizard", "settings.json");

    /// <summary>Winget package ids the user installed some other way.</summary>
    public List<string> ManuallyInstalled { get; set; } = [];

    /// <summary>Where backups go, or null for <see cref="BackupService.DefaultFolder"/>.</summary>
    public string? BackupFolder { get; set; }

    public List<SecretApp> SecretApps { get; set; } = [];

    public static WizardSettings Load() => Load(DefaultPath);

    /// <summary>The saved settings, or defaults if there are none or the file can't be read.</summary>
    internal static WizardSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<WizardSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save() => Save(DefaultPath);

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public bool IsManuallyInstalled(string packageId) =>
        ManuallyInstalled.Contains(packageId, StringComparer.OrdinalIgnoreCase);

    public void SetManuallyInstalled(string packageId, bool installed)
    {
        ManuallyInstalled.RemoveAll(id => string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
        if (installed)
            ManuallyInstalled.Add(packageId);
    }
}
