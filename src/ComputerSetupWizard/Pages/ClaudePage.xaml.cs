using System.Collections.ObjectModel;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed class ModItem(ClaudeMod mod) : Observable
{
    bool _isSelected;
    bool _enabled;
    bool _isBusy;
    string _statusText = "";

    public ClaudeMod Mod { get; } = mod;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value))
                return;
            OnPropertyChanged(nameof(CanSelect));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(Brush));
        }
    }

    public bool CanSelect => !Enabled;

    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    Status Status => Enabled ? Status.Ok : Status.Warning;
    public string Glyph => StatusVisuals.Glyph(Status);
    public Brush Brush => StatusVisuals.Brush(Status);

    public void Refresh()
    {
        Enabled = ClaudeModsService.IsEnabled(Mod.Name);
        StatusText = Enabled ? "Enabled" : "Not installed";
        IsSelected = !Enabled;
    }
}

public sealed partial class ClaudePage : Page
{
    public ClaudePage()
    {
        InitializeComponent();
    }

    public ObservableCollection<ModItem> Mods { get; } = [];

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        ShowState();
        await LoadModsAsync();
    }

    async Task LoadModsAsync()
    {
        ToolPaths.RefreshPath();
        ModsRoute.Text = ToolPaths.Find("claude") is not null
            ? "Installs with the claude command line."
            : $"The claude command line isn't installed, so the mods are turned on in {ClaudeModsService.SettingsPath} " +
              "(a backup is saved next to it) and Claude Code downloads them when the next session starts.";

        Mods.Clear();
        foreach (var mod in await ClaudeModsService.GetModsAsync())
        {
            var item = new ModItem(mod);
            item.Refresh();
            Mods.Add(item);
        }
        InstallModsButton.IsEnabled = Mods.Count > 0;
    }

    async void InstallMods_Click(object sender, RoutedEventArgs e)
    {
        var selected = Mods.Where(m => m.IsSelected && !m.Enabled).ToList();
        if (selected.Count == 0)
            return;

        InstallModsButton.IsEnabled = false;
        ModsLog.Visibility = Visibility.Visible;
        foreach (var item in selected)
        {
            item.IsBusy = true;
            item.StatusText = "Installing…";
            ModsLog.Append($"> {ClaudeModsService.PluginId(item.Mod.Name)}");
            try
            {
                var result = await ClaudeModsService.InstallAsync(item.Mod.Name, ModsLog.Append);
                item.Refresh();
                if (!result.Succeeded)
                    item.StatusText = $"Failed (exit code {result.ExitCode})";
            }
            catch (Exception ex)
            {
                ModsLog.Append(ex.Message);
                item.StatusText = "Failed";
            }
            item.IsBusy = false;
        }
        InstallModsButton.IsEnabled = true;
    }

    void ShowState()
    {
        var exe = AppInstall.TargetExe;
        Preview.Text = ClaudeMdService.BuildSection(exe);

        var state = ClaudeMdService.GetState(exe);
        StateBar.Severity = state == ClaudeMdState.Current ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        StateBar.Title = state switch
        {
            ClaudeMdState.Current => "CLAUDE.md is up to date",
            ClaudeMdState.Outdated => "CLAUDE.md has an older version of this section",
            _ => "CLAUDE.md doesn't have this section yet",
        };
        StateBar.Message = ClaudeMdService.FilePath;
        WriteButton.Content = state == ClaudeMdState.Missing ? "Add to CLAUDE.md" : "Update CLAUDE.md";
        WriteButton.IsEnabled = state != ClaudeMdState.Current;
    }

    void Write_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // The instructions point Claude at the installed exe, so make sure it's there
            ClaudeMdService.Write(AppInstall.EnsureInstalled());
            WriteStatus.Text = "";
        }
        catch (Exception ex)
        {
            WriteStatus.Text = ex.Message;
        }
        ShowState();
    }
}
