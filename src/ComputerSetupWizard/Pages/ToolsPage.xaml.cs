using System.Collections.ObjectModel;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed class PackageItem(WingetPackage package, bool marked) : Observable
{
    bool _isSelected = package.SelectedByDefault;
    bool? _foundByWinget;
    bool _marked = marked;
    bool _isBusy = true;
    string _statusText = "Checking…";

    public WingetPackage Package { get; } = package;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Whether winget lists the package, or null while checking.</summary>
    public bool? FoundByWinget
    {
        get => _foundByWinget;
        set
        {
            if (Set(ref _foundByWinget, value))
                OnInstalledChanged();
        }
    }

    /// <summary>The user installed it some other way, so winget doesn't list it.</summary>
    public bool Marked
    {
        get => _marked;
        set
        {
            if (Set(ref _marked, value))
                OnInstalledChanged();
        }
    }

    public bool? Installed => FoundByWinget is { } found ? found || Marked : null;

    public bool CanSelect => Installed == false;

    public bool CanMark => Installed == false && !IsBusy;

    public bool CanUnmark => Marked && FoundByWinget == false && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanMark));
                OnPropertyChanged(nameof(CanUnmark));
            }
        }
    }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    Status Status => Installed switch { true => Status.Ok, false => Status.Warning, null => Status.Checking };
    public string Glyph => StatusVisuals.Glyph(Status);
    public Brush Brush => StatusVisuals.Brush(Status);

    /// <summary>Shows whether it's installed, once checking or installing is done.</summary>
    public void ShowInstalled() =>
        StatusText = FoundByWinget == true ? "Installed" : Marked ? "Installed by you" : "Not installed";

    void OnInstalledChanged()
    {
        if (Installed == true)
            IsSelected = false;
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanMark));
        OnPropertyChanged(nameof(CanUnmark));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Brush));
    }
}

public sealed partial class ToolsPage : Page
{
    readonly WizardSettings _settings = WizardSettings.Load();

    public ToolsPage()
    {
        InitializeComponent();
        Packages = new(WingetService.Packages.Select(p => new PackageItem(p, _settings.IsManuallyInstalled(p.Id))));
    }

    public ObservableCollection<PackageItem> Packages { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await CheckAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    void Mark_Click(object sender, RoutedEventArgs e) => SetMarked((PackageItem)((FrameworkElement)sender).DataContext, true);

    void Unmark_Click(object sender, RoutedEventArgs e) => SetMarked((PackageItem)((FrameworkElement)sender).DataContext, false);

    void SetMarked(PackageItem item, bool marked)
    {
        _settings.SetManuallyInstalled(item.Package.Id, marked);
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Append($"Couldn't save that {item.Package.Name} is installed: {ex.Message}");
        }
        item.Marked = marked;
        item.ShowInstalled();
    }

    async Task CheckAsync()
    {
        ToolPaths.RefreshPath();
        var available = WingetService.IsAvailable;
        WingetMissing.IsOpen = !available;
        InstallButton.IsEnabled = RefreshButton.IsEnabled = false;

        foreach (var item in Packages)
        {
            item.IsBusy = true;
            item.FoundByWinget = null;
            item.StatusText = "Checking…";
        }

        await Task.WhenAll(Packages.Select(async item =>
        {
            item.FoundByWinget = available && await WingetService.IsInstalledAsync(item.Package);
            item.IsBusy = false;
            item.ShowInstalled();
        }));

        InstallButton.IsEnabled = RefreshButton.IsEnabled = available;
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = Packages.Where(p => p.IsSelected && p.Installed == false).ToList();
        if (selected.Count == 0)
        {
            Log.Append("Nothing to install: select a package that isn't installed yet.");
            return;
        }

        InstallButton.IsEnabled = RefreshButton.IsEnabled = false;
        foreach (var item in selected)
        {
            item.IsBusy = true;
            item.StatusText = "Installing…";
            Log.Append($"> winget install {item.Package.Id}");

            var result = await WingetService.InstallAsync(item.Package, Log.Append);

            item.IsBusy = false;
            item.FoundByWinget = result.Succeeded || await WingetService.IsInstalledAsync(item.Package);
            if (item.FoundByWinget == true)
                item.ShowInstalled();
            else
                item.StatusText = $"Failed (exit code {result.ExitCode})";
            Log.Append("");
        }

        // So the next steps find tools installed just now
        ToolPaths.RefreshPath();
        InstallButton.IsEnabled = RefreshButton.IsEnabled = true;
    }
}
