using System.Collections.ObjectModel;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace ComputerSetupWizard.Pages;

public sealed class RepoItem(RepoInfo info) : Observable
{
    RepoState _state = info.State;
    bool _isSelected = info.State != RepoState.Bot;

    public string Path { get; } = info.Path;
    public string Name => System.IO.Path.GetFileName(Path);

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public RepoState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value))
                return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(Brush));
        }
    }

    public string StateText => State switch
    {
        RepoState.Bot => "Uses the bot",
        RepoState.Partial => "Partly set up",
        _ => "Personal identity",
    };

    Status Status => State switch { RepoState.Bot => Status.Ok, RepoState.Partial => Status.Warning, _ => Status.Error };
    public string Glyph => StatusVisuals.Glyph(Status);
    public Brush Brush => StatusVisuals.Brush(Status);
}

public sealed partial class ReposPage : Page
{
    public ReposPage()
    {
        InitializeComponent();
        FoldersBox.Text = string.Join(Environment.NewLine, RepoService.DefaultScanRoots());
        InstallNote.Text =
            $"Git runs the wizard to get bot tokens, so it copies itself to {AppInstall.InstallDirectory} first. " +
            "For a new clone, search again or run: ComputerSetupWizard.exe configure-repo <path>";
    }

    public ObservableCollection<RepoItem> Repos { get; } = [];

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ScanAsync();

    async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    IEnumerable<string> Folders =>
        FoldersBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    async Task ScanAsync()
    {
        if (!GitHubCliService.IsGitInstalled)
        {
            ShowResult(InfoBarSeverity.Warning, "Install Git on the Tools step first.");
            return;
        }

        SetBusy(true);
        try
        {
            var found = await RepoService.ScanAsync(Folders, AppInstall.TargetExe);
            Repos.Clear();
            foreach (var repo in found)
                Repos.Add(new RepoItem(repo));
            ResultsTitle.Text = found.Count == 0 ? "No SpeakingInBits repos found" : $"Repos ({found.Count})";
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            FoldersBox.Text = string.Join(Environment.NewLine, Folders.Append(folder.Path).Distinct(StringComparer.OrdinalIgnoreCase));
            await ScanAsync();
        }
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (BotConfig.FindNewestKey() is null)
        {
            ShowResult(InfoBarSeverity.Warning, "Add this machine's bot key on the Bot key step first, or pushes will fail.");
            return;
        }
        await UpdateSelectedAsync("now use the bot", async repo =>
        {
            var exe = AppInstall.EnsureInstalled();
            await RepoService.ConfigureForBotAsync(repo.Path, exe);
            return RepoState.Bot;
        });
    }

    async void Reset_Click(object sender, RoutedEventArgs e) =>
        await UpdateSelectedAsync("now use your personal identity", async repo =>
        {
            await RepoService.ResetToPersonalAsync(repo.Path);
            return RepoState.Personal;
        });

    async Task UpdateSelectedAsync(string done, Func<RepoItem, Task<RepoState>> update)
    {
        var selected = Repos.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ShowResult(InfoBarSeverity.Informational, "Select one or more repos first.");
            return;
        }

        SetBusy(true);
        var failures = new List<string>();
        foreach (var repo in selected)
        {
            try
            {
                repo.State = await update(repo);
                repo.IsSelected = false;
            }
            catch (Exception ex)
            {
                failures.Add($"{repo.Name}: {ex.Message}");
            }
        }
        SetBusy(false);

        var succeeded = selected.Count - failures.Count;
        ShowResult(
            failures.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error,
            string.Join(Environment.NewLine, failures.Prepend($"{succeeded} of {selected.Count} repos {done}.")));
    }

    void SetBusy(bool busy)
    {
        ScanProgress.IsActive = busy;
        ScanButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = ResetButton.IsEnabled = !busy && Repos.Count > 0;
    }

    void ShowResult(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
