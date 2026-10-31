using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ComputerSetupWizard.Core;

/// <summary>When and where an item was backed up.</summary>
public sealed record BackupInfo(DateTime Time, string Machine);

/// <summary>Something the wizard can save to the backup folder and put back on another PC.</summary>
public abstract class BackupItem(string name, string description, string folderName)
{
    public string Name { get; } = name;
    public string Description { get; } = description;

    /// <summary>The item's folder inside the backup folder.</summary>
    public string FolderName { get; } = folderName;

    /// <summary>What's backed up on this PC, for showing to the user.</summary>
    public abstract string Location { get; }

    public abstract bool IsOnThisPc { get; }

    /// <summary>Saves the item into <paramref name="folder"/>, which is new and empty.</summary>
    public abstract Task BackupAsync(string folder, Action<string> log, CancellationToken ct);

    public abstract Task RestoreAsync(string folder, Action<string> log, CancellationToken ct);
}

/// <summary>Files under a folder that match any of a list of patterns, such as <c>agents/*</c>.</summary>
public sealed class FolderBackupItem(string name, string description, string folderName, string root, params string[] patterns)
    : BackupItem(name, description, folderName)
{
    static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public string Root { get; } = root;
    public IReadOnlyList<string> Patterns { get; } = patterns;

    public override string Location => Root;

    public override bool IsOnThisPc => Directory.Exists(Root);

    /// <summary>Paths relative to <see cref="Root"/> of the files to back up.</summary>
    public IEnumerable<string> FindFiles() =>
        Directory.EnumerateFiles(Root, "*", AllFiles)
            .Select(file => Path.GetRelativePath(Root, file))
            // Backslash escapes a wildcard in these patterns, so they use forward slashes
            .Where(relative => Patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, relative.Replace('\\', '/'))));

    public override Task BackupAsync(string folder, Action<string> log, CancellationToken ct) =>
        Task.Run(() => Copy(Root, folder, FindFiles(), log, ct), ct);

    public override Task RestoreAsync(string folder, Action<string> log, CancellationToken ct) =>
        Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(folder, "*", AllFiles)
                .Select(file => Path.GetRelativePath(folder, file))
                .Where(relative => relative != BackupService.MarkerFile);
            Copy(folder, Root, files, log, ct);
        }, ct);

    /// <summary>Copies files, replacing ones already there. A file that's in use is skipped and logged.</summary>
    static void Copy(string from, string to, IEnumerable<string> relativePaths, Action<string> log, CancellationToken ct)
    {
        int copied = 0, skipped = 0;
        foreach (var relative in relativePaths)
        {
            ct.ThrowIfCancellationRequested();
            var destination = Path.Combine(to, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(from, relative), destination, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log($"Skipped {relative}: {ex.Message}");
                skipped++;
            }
        }
        log(skipped == 0 ? $"Copied {copied} files." : $"Copied {copied} files and skipped {skipped}. Close the app and try again.");
    }
}

/// <summary>Printers with their drivers and ports, saved by the Printer Migration tool (PrintBrm).</summary>
public sealed class PrintersBackupItem() : BackupItem(
    "Printers and drivers",
    "The printers you choose, with their drivers, ports and settings, saved by Windows' Printer Migration tool. " +
    "The file holds the files of every printer driver on this PC, so it's a few hundred MB. Restoring asks for " +
    "administrator approval and keeps printers the new PC already has.",
    "Printers")
{
    const string ExportFile = "printers.printerExport";

    static string PrintBrm => Path.Combine(Environment.SystemDirectory, "spool", "tools", "PrintBrm.exe");

    public override string Location => "The printers installed in Windows";

    public override bool IsOnThisPc => File.Exists(PrintBrm);

    /// <summary>The printers to back up, or null for all of them.</summary>
    public IReadOnlyCollection<string>? Printers { get; set; }

    /// <summary>The printers the Printer Migration tool would back up.</summary>
    public static async Task<IReadOnlyList<string>> GetPrintersAsync(CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(PrintBrm, ["-Q"], ct: ct);
        if (!result.Succeeded)
            throw new InvalidOperationException($"The Printer Migration tool couldn't list the printers (exit code {result.ExitCode}).");
        return ParsePrinters(result.Output);
    }

    /// <summary>The names under LISTING PRINT QUEUES in the output of <c>PrintBrm -Q</c>.</summary>
    internal static IReadOnlyList<string> ParsePrinters(string output) =>
        output.ReplaceLineEndings("\n").Split('\n')
            .SkipWhile(line => line.Trim() != "LISTING PRINT QUEUES")
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("LISTING ", StringComparison.Ordinal))
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Trim())
            .ToList();

    // Saving doesn't need administrator rights
    public override async Task BackupAsync(string folder, Action<string> log, CancellationToken ct)
    {
        var destination = Path.Combine(folder, ExportFile);
        if (Printers is null)
        {
            await RunPrintBrmAsync(["-B", "-F", destination], log, ct);
            return;
        }
        if (Printers.Count == 0)
            throw new InvalidOperationException("Choose at least one printer to back up.");

        // The tool backs up every printer, so back them all up, unpack, drop the others and pack it again
        var temp = Path.Combine(Path.GetTempPath(), $"ComputerSetupWizard-printers-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temp);
            var all = Path.Combine(temp, ExportFile);
            var unpacked = Path.Combine(temp, "unpacked");
            await RunPrintBrmAsync(["-B", "-F", all], log, ct);
            log("Removing the printers you didn't choose…");
            // With -D, -R only unpacks the file; it doesn't restore anything
            await RunPrintBrmAsync(["-R", "-F", all, "-D", unpacked], _ => { }, ct);
            KeepOnly(unpacked, Printers);
            await RunPrintBrmAsync(["-B", "-D", unpacked, "-F", destination], _ => { }, ct);
            log($"Saved {string.Join(", ", Printers)}.");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    static async Task RunPrintBrmAsync(string[] arguments, Action<string> log, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(PrintBrm, arguments, onOutput: log, ct: ct);
        if (!result.Succeeded)
            throw new InvalidOperationException($"The Printer Migration tool failed (exit code {result.ExitCode}).");
    }

    /// <summary>
    /// Removes the other printers from an unpacked backup, and the drivers only they use, so a restore doesn't install
    /// them. The driver files stay, because drivers share them.
    /// </summary>
    internal static void KeepOnly(string unpacked, IReadOnlyCollection<string> printers)
    {
        var printersFile = Path.Combine(unpacked, "BrmPrinters.xml");
        var queues = XDocument.Load(printersFile);
        var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var queue in queues.Root!.Elements("PRINTQUEUE").ToList())
        {
            var queueFile = Path.Combine(unpacked, "Printers", (string?)queue.Attribute("FileName") ?? "");
            if (printers.Contains((string?)queue.Attribute("PrinterName") ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(queueFile) && (string?)XDocument.Load(queueFile).Root!.Attribute("DriverName") is { } driver)
                    drivers.Add(driver);
            }
            else
            {
                queue.Remove();
                File.Delete(queueFile);
            }
        }
        Save(queues, printersFile);

        var driversFile = Path.Combine(unpacked, "BrmDrivers.xml");
        var driverList = XDocument.Load(driversFile);
        // Entries without a name aren't drivers, so they stay
        driverList.Root!.Elements("DRIVER")
            .Where(entry => (string?)entry.Attribute("DriverName") is { } name && !drivers.Contains(name))
            .Remove();
        Save(driverList, driversFile);
    }

    // Like the tool's own files: no XML declaration or byte order mark
    static void Save(XDocument document, string path)
    {
        using var writer = XmlWriter.Create(path, new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) });
        document.Save(writer);
    }

    // Installing drivers does, and an elevated process can't hand its output back, so it goes through a log file
    public override async Task RestoreAsync(string folder, Action<string> log, CancellationToken ct)
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"ComputerSetupWizard-printers-{Guid.NewGuid():N}.log");
        try
        {
            var command = $"/c \"\"{PrintBrm}\" -R -F \"{Path.Combine(folder, ExportFile)}\" > \"{logFile}\" 2>&1\"";
            var exitCode = await ProcessRunner.RunElevatedAsync("cmd.exe", command);
            if (File.Exists(logFile))
                foreach (var line in File.ReadAllLines(logFile))
                    log(line);
            if (exitCode != 0)
                throw new InvalidOperationException($"The Printer Migration tool failed (exit code {exitCode}).");
        }
        finally
        {
            File.Delete(logFile);
        }
    }
}

/// <summary>Saves settings that winget can't reinstall to a folder that a new PC can restore from.</summary>
public static class BackupService
{
    /// <summary>Marks a folder in the backup as the wizard's, with when and where it was backed up.</summary>
    public const string MarkerFile = ".wizard-backup";

    public const string SecretAppsFile = "Apps with secrets.json";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static readonly IReadOnlyList<BackupItem> Items =
    [
        new FolderBackupItem(
            "Claude Code",
            "Your CLAUDE.md, settings, keybindings, agents, commands, skills, hooks and the memories Claude keeps for each " +
            "project. Your sign-in, chat history and plugin files aren't included.",
            "Claude Code",
            Path.Combine(Home, ".claude"),
            "CLAUDE.md", "settings.json", "keybindings.json",
            "agents/*", "commands/*", "skills/*", "output-styles/*", "hooks/*", "rules/*", "projects/*/memory/*"),
        new FolderBackupItem(
            "Cricut Design Space",
            "Projects saved to this computer, material settings, fonts and images. Projects in the Cricut cloud come back " +
            "when you sign in. Close Design Space first.",
            "Cricut Design Space",
            Path.Combine(Home, ".cricut-design-space"),
            "LocalData/*"),
        new PrintersBackupItem(),
    ];

    /// <summary>A folder in OneDrive if it's set up, so the new PC finds the backup on its own; otherwise in Documents.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } oneDrive && Directory.Exists(oneDrive)
            ? oneDrive
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Computer Setup Wizard backup");

    /// <summary>When <paramref name="item"/> was last backed up to <paramref name="folder"/>, or null if it hasn't been.</summary>
    public static BackupInfo? GetInfo(string folder, BackupItem item)
    {
        var marker = Path.Combine(folder, item.FolderName, MarkerFile);
        if (!File.Exists(marker))
            return null;
        var lines = File.ReadAllLines(marker);
        return lines.Length >= 2 && DateTime.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? new BackupInfo(time, lines[0])
            : null;
    }

    /// <summary>
    /// Backs up <paramref name="item"/> to a new folder, then swaps it in for the last backup, so a failed
    /// backup leaves the last one as it was.
    /// </summary>
    public static async Task BackupAsync(string folder, BackupItem item, Action<string> log, CancellationToken ct = default)
    {
        var destination = Path.Combine(folder, item.FolderName);
        if (Directory.Exists(destination) && !File.Exists(Path.Combine(destination, MarkerFile))
            && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException($"{destination} already has files the wizard didn't put there. Choose another backup folder.");

        var partial = destination + ".partial";
        if (Directory.Exists(partial))
            Directory.Delete(partial, recursive: true);
        Directory.CreateDirectory(partial);
        try
        {
            await item.BackupAsync(partial, log, ct);
            File.WriteAllLines(Path.Combine(partial, MarkerFile),
                [Environment.MachineName, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)]);
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            Directory.Move(partial, destination);
        }
        finally
        {
            if (Directory.Exists(partial))
                Directory.Delete(partial, recursive: true);
        }
    }

    public static Task RestoreAsync(string folder, BackupItem item, Action<string> log, CancellationToken ct = default)
    {
        var source = Path.Combine(folder, item.FolderName);
        if (!File.Exists(Path.Combine(source, MarkerFile)))
            throw new InvalidOperationException($"There's no backup of {item.Name} in {folder}.");
        return item.RestoreAsync(source, log, ct);
    }

    public static void SaveSecretApps(string folder, IEnumerable<SecretApp> apps)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SecretAppsFile), JsonSerializer.Serialize(apps, JsonOptions));
    }

    public static IReadOnlyList<SecretApp> LoadSecretApps(string folder)
    {
        var file = Path.Combine(folder, SecretAppsFile);
        return File.Exists(file) ? JsonSerializer.Deserialize<List<SecretApp>>(File.ReadAllText(file)) ?? [] : [];
    }

    /// <summary>The apps in <paramref name="local"/>, then the backed-up ones it doesn't have yet.</summary>
    public static List<SecretApp> MergeSecretApps(IEnumerable<SecretApp> local, IEnumerable<SecretApp> backedUp)
    {
        var merged = local.ToList();
        foreach (var app in backedUp)
        {
            if (!merged.Any(m => string.Equals(m.Name, app.Name, StringComparison.OrdinalIgnoreCase)))
                merged.Add(app);
        }
        return merged;
    }
}
