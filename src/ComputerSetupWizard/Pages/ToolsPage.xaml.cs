using System.Collections.ObjectModel;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed class PackageItem(WingetPackage package) : Observable
{
    bool _isSelected = package.Required;
    bool? _installed;
    bool _isBusy = true;
    string _statusText = "Checking…";

    public WingetPackage Package { get; } = package;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool? Installed
    {
        get => _installed;
        set
        {
            if (!Set(ref _installed, value))
                return;
            if (value == true)
                IsSelected = false;
            OnPropertyChanged(nameof(CanSelect));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(Brush));
        }
    }

    public bool CanSelect => Installed == false;

    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    Status Status => Installed switch { true => Status.Ok, false => Status.Warning, null => Status.Checking };
    public string Glyph => StatusVisuals.Glyph(Status);
    public Brush Brush => StatusVisuals.Brush(Status);
}

public sealed partial class ToolsPage : Page
{
    public ToolsPage()
    {
        InitializeComponent();
        Packages = new(WingetService.Packages.Select(p => new PackageItem(p)));
    }

    public ObservableCollection<PackageItem> Packages { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await CheckAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    async Task CheckAsync()
    {
        ToolPaths.RefreshPath();
        var available = WingetService.IsAvailable;
        WingetMissing.IsOpen = !available;
        InstallButton.IsEnabled = RefreshButton.IsEnabled = false;

        foreach (var item in Packages)
        {
            item.IsBusy = true;
            item.Installed = null;
            item.StatusText = "Checking…";
        }

        await Task.WhenAll(Packages.Select(async item =>
        {
            item.Installed = available && await WingetService.IsInstalledAsync(item.Package.Id);
            item.StatusText = item.Installed == true ? "Installed" : "Not installed";
            item.IsBusy = false;
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

            var result = await WingetService.InstallAsync(item.Package.Id, Log.Append);

            item.IsBusy = false;
            item.Installed = result.Succeeded || await WingetService.IsInstalledAsync(item.Package.Id);
            item.StatusText = item.Installed == true ? "Installed" : $"Failed (exit code {result.ExitCode})";
            Log.Append("");
        }

        // So the next steps find tools installed just now
        ToolPaths.RefreshPath();
        InstallButton.IsEnabled = RefreshButton.IsEnabled = true;
    }
}
