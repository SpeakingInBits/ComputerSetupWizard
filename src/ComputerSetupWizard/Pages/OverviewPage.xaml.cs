using System.Collections.ObjectModel;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed class CheckItem(string title, string step) : Observable
{
    Status _status;
    string _detail = "Checking…";

    public string Title { get; } = title;
    public string Step { get; } = step;

    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    public Status Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(IsChecking));
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    public bool IsChecking => Status == Status.Checking;
    public string Glyph => StatusVisuals.Glyph(Status);
    public Brush Brush => StatusVisuals.Brush(Status);

    public void Report(Status status, string detail)
    {
        Status = status;
        Detail = detail;
    }
}

public sealed partial class OverviewPage : Page
{
    readonly CheckItem _winget = new("winget", "Tools");
    readonly CheckItem _git = new("Git", "Tools");
    readonly CheckItem _gh = new("GitHub CLI", "Tools");
    readonly CheckItem _login = new("Signed in to GitHub", "GitHub");
    readonly CheckItem _identity = new("Your Git identity", "GitHub");
    readonly CheckItem _key = new("Bot private key", "BotKey");
    readonly CheckItem _bot = new("SpeakingInBits repos use the bot", "BotIdentity");
    readonly CheckItem _mods = new("Claude Code mods", "Claude");
    readonly CheckItem _claude = new("Claude Code knows about the bot", "Claude");
    readonly CheckItem _backup = new("Settings backed up", "Backup");

    public OverviewPage()
    {
        InitializeComponent();
        Checks = [_winget, _git, _gh, _login, _identity, _key, _bot, _mods, _claude, _backup];
    }

    public ObservableCollection<CheckItem> Checks { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RunChecksAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RunChecksAsync();

    void Check_ItemClick(object sender, ItemClickEventArgs e) => App.MainWindow!.GoTo(((CheckItem)e.ClickedItem).Step);

    async Task RunChecksAsync()
    {
        RefreshButton.IsEnabled = false;
        foreach (var check in Checks)
            check.Report(Status.Checking, "Checking…");

        try
        {
            ToolPaths.RefreshPath();
            _winget.Report(WingetService.IsAvailable ? Status.Ok : Status.Error,
                WingetService.IsAvailable ? "Available" : "Not found. Install App Installer from the Microsoft Store.");
            _git.Report(Found("git"), ToolPaths.Find("git") ?? "Not installed");
            _gh.Report(Found("gh"), ToolPaths.Find("gh") ?? "Not installed");

            var login = await GitHubCliService.GetLoginAsync();
            _login.Report(login.SignedIn ? Status.Ok : Status.Error,
                login.SignedIn ? $"Signed in as {login.Account}" : "Not signed in");

            var (name, email) = GitHubCliService.IsGitInstalled ? await GitHubCliService.GetGlobalIdentityAsync() : (null, null);
            _identity.Report(name is not null && email is not null ? Status.Ok : Status.Error,
                name is not null && email is not null ? $"{name} <{email}>" : "Name or email not set");

            var key = BotConfig.FindNewestKey();
            _key.Report(key is not null ? Status.Ok : Status.Error, key ?? $"No key in {BotConfig.KeyDirectory}");

            if (GitHubCliService.IsGitInstalled)
            {
                var bot = await BotIdentityService.GetStateAsync(AppInstall.Exe);
                _bot.Report(bot == BotIdentityState.Current ? Status.Ok : Status.Error, bot switch
                {
                    BotIdentityState.Current => BotIdentityService.IncludeFile,
                    BotIdentityState.Outdated => "The global Git config has an older or overridden bot setup",
                    _ => "Not in the global Git config yet",
                });
            }
            else
            {
                _bot.Report(Status.Error, "Needs Git");
            }

            var mods = await ClaudeModsService.GetModsAsync();
            var enabled = mods.Count(m => ClaudeModsService.IsEnabled(m.Name));
            _mods.Report(enabled == mods.Count ? Status.Ok : Status.Error, $"{enabled} of {mods.Count} enabled");

            var claude = ClaudeMdService.GetState(AppInstall.Exe);
            _claude.Report(claude == ClaudeMdState.Current ? Status.Ok : Status.Error, claude switch
            {
                ClaudeMdState.Current => ClaudeMdService.FilePath,
                ClaudeMdState.Outdated => "CLAUDE.md has an older version of the bot instructions",
                _ => "Not in CLAUDE.md yet",
            });

            // Optional, so a missing backup is a warning rather than an error
            var folder = WizardSettings.Load().BackupFolder ?? BackupService.DefaultFolder;
            var newest = BackupService.Items.Select(item => BackupService.GetInfo(folder, item)).OfType<BackupInfo>().MaxBy(info => info.Time);
            _backup.Report(newest is not null ? Status.Ok : Status.Warning, newest is not null
                ? $"Last backed up {newest.Time.ToLocalTime():g} from {newest.Machine}, in {folder}"
                : $"Nothing in {folder} yet");
        }
        catch (Exception ex)
        {
            foreach (var check in Checks.Where(c => c.IsChecking))
                check.Report(Status.Error, ex.Message);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    static Status Found(string tool) => ToolPaths.Find(tool) is not null ? Status.Ok : Status.Error;
}
