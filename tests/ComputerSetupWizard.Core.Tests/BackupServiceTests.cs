using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public sealed class BackupServiceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "csw-backup-" + Guid.NewGuid().ToString("N"));

    string Source => Path.Combine(_dir, "source");
    string Backups => Path.Combine(_dir, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    void WriteFile(string root, string relative, string text = "x")
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    FolderBackupItem ClaudeLike() => new("Claude", "", "Claude", Source,
        "CLAUDE.md", "settings.json", "agents/*", "projects/*/memory/*");

    [Fact]
    public void FindFiles_TakesOnlyMatchingFiles()
    {
        WriteFile(Source, "CLAUDE.md");
        WriteFile(Source, "settings.json");
        WriteFile(Source, ".credentials.json");
        WriteFile(Source, @"agents\reviewer.md");
        WriteFile(Source, @"agents\nested\helper.md");
        WriteFile(Source, @"projects\E--Code-App\memory\MEMORY.md");
        WriteFile(Source, @"projects\E--Code-App\0123.jsonl");
        WriteFile(Source, @"plugins\cache\big.bin");

        var files = ClaudeLike().FindFiles().Order().ToList();

        Assert.Equal(
            [@"agents\nested\helper.md", @"agents\reviewer.md", "CLAUDE.md", @"projects\E--Code-App\memory\MEMORY.md", "settings.json"],
            files);
    }

    [Fact]
    public async Task BackupAndRestore_RoundTrip()
    {
        var item = ClaudeLike();
        WriteFile(Source, "CLAUDE.md", "mine");
        WriteFile(Source, @"agents\reviewer.md", "agent");

        await BackupService.BackupAsync(Backups, item, _ => { });

        var info = BackupService.GetInfo(Backups, item);
        Assert.NotNull(info);
        Assert.Equal(Environment.MachineName, info.Machine);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Backups, "Claude", "CLAUDE.md")));

        Directory.Delete(Source, recursive: true);
        WriteFile(Source, "CLAUDE.md", "new PC");
        await BackupService.RestoreAsync(Backups, item, _ => { });

        Assert.Equal("mine", File.ReadAllText(Path.Combine(Source, "CLAUDE.md")));
        Assert.Equal("agent", File.ReadAllText(Path.Combine(Source, "agents", "reviewer.md")));
        Assert.False(File.Exists(Path.Combine(Source, BackupService.MarkerFile)));
    }

    [Fact]
    public async Task Backup_ReplacesTheLastBackup()
    {
        var item = ClaudeLike();
        WriteFile(Source, "CLAUDE.md");
        WriteFile(Source, "settings.json");
        await BackupService.BackupAsync(Backups, item, _ => { });

        File.Delete(Path.Combine(Source, "settings.json"));
        await BackupService.BackupAsync(Backups, item, _ => { });

        Assert.False(File.Exists(Path.Combine(Backups, "Claude", "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(Backups, "Claude.partial")));
    }

    [Fact]
    public async Task Backup_LeavesAFolderTheWizardDidNotMake()
    {
        WriteFile(Source, "CLAUDE.md");
        WriteFile(Backups, @"Claude\someone-elses.txt");

        await Assert.ThrowsAsync<IOException>(() => BackupService.BackupAsync(Backups, ClaudeLike(), _ => { }));
        Assert.True(File.Exists(Path.Combine(Backups, "Claude", "someone-elses.txt")));
    }

    [Fact]
    public async Task Restore_NeedsABackup() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => BackupService.RestoreAsync(Backups, ClaudeLike(), _ => { }));

    [Fact]
    public void SecretApps_RoundTripAndMerge()
    {
        BackupService.SaveSecretApps(Backups, [new("AWS CLI", @"~\.aws\credentials"), new("Stripe", "")]);

        var merged = BackupService.MergeSecretApps([new("stripe", "dashboard key")], BackupService.LoadSecretApps(Backups));

        Assert.Equal([new SecretApp("stripe", "dashboard key"), new SecretApp("AWS CLI", @"~\.aws\credentials")], merged);
    }

    [Fact]
    public void LoadSecretApps_IsEmptyWithoutABackup() => Assert.Empty(BackupService.LoadSecretApps(Backups));

    [Fact]
    public void ParsePrinters_ReadsThePrintQueues()
    {
        var output = string.Join("\r\n",
            "Operation mode: query", "", "LISTING PRINT QUEUES", "PM-241-BT", "EPSON5B1EAF (ET-8550 Series)",
            "LISTING PRINTER DRIVERS", "PM-241-BT, Windows x64, QY Monitor", "LISTING PRINTER PORTS");

        Assert.Equal(["PM-241-BT", "EPSON5B1EAF (ET-8550 Series)"], PrintersBackupItem.ParsePrinters(output));
    }

    [Fact]
    public void KeepOnly_RemovesOtherPrintersAndTheirDrivers()
    {
        WriteFile(Source, "BrmPrinters.xml",
            """<PRINTERS><PRINTQUEUE PrinterName="Label" FileName="a.xml"/><PRINTQUEUE PrinterName="Photo" FileName="b.xml"/></PRINTERS>""");
        WriteFile(Source, @"Printers\a.xml", """<PRINTQUEUE PrinterName="Label" DriverName="Label Driver"/>""");
        WriteFile(Source, @"Printers\b.xml", """<PRINTQUEUE PrinterName="Photo" DriverName="Photo Driver"/>""");
        WriteFile(Source, "BrmDrivers.xml",
            """<PRINTERDRIVERS><DRIVER DriverName="Label Driver"/><DRIVER DriverName="Photo Driver"/><DRIVER Environment="Windows x64"/></PRINTERDRIVERS>""");

        PrintersBackupItem.KeepOnly(Source, ["label"]);

        Assert.Equal("""<PRINTERS><PRINTQUEUE PrinterName="Label" FileName="a.xml" /></PRINTERS>""",
            File.ReadAllText(Path.Combine(Source, "BrmPrinters.xml")));
        Assert.False(File.Exists(Path.Combine(Source, "Printers", "b.xml")));
        Assert.Equal("""<PRINTERDRIVERS><DRIVER DriverName="Label Driver" /><DRIVER Environment="Windows x64" /></PRINTERDRIVERS>""",
            File.ReadAllText(Path.Combine(Source, "BrmDrivers.xml")));
    }
}
