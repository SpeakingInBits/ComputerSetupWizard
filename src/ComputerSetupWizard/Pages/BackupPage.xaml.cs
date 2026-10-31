using System.Collections.ObjectModel;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace ComputerSetupWizard.Pages;

public sealed class PrinterChoice(string name, bool isSelected) : Observable
{
    bool _isSelected = isSelected;

    public string Name { get; } = name;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class BackupRow(BackupItem item) : Observable
{
    bool _hasPrinters;
    bool _isSelected;
    bool _isBusy;
    string _statusText = "";
    string _detail = "";

    public BackupItem Item { get; } = item;

    public BackupInfo? Info { get; private set; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    /// <summary>For the printers item, which printers to back up.</summary>
    public ObservableCollection<PrinterChoice> Printers { get; } = [];

    public bool HasPrinters { get => _hasPrinters; set => Set(ref _hasPrinters, value); }

    bool _refreshed;

    /// <summary>
    /// Reads whether it's on this PC and in <paramref name="folder"/>. The first time, it's selected if it's either.
    /// </summary>
    public void Refresh(string folder)
    {
        Info = BackupService.GetInfo(folder, Item);
        Detail = string.Join(" · ",
            Item.IsOnThisPc ? Item.Location : "Not on this PC",
            Info is not null ? $"Backed up {Info.Time.ToLocalTime():g} from {Info.Machine}" : "Not backed up yet");
        if (!_refreshed)
            IsSelected = Item.IsOnThisPc || Info is not null;
        _refreshed = true;
    }
}

public sealed partial class BackupPage : Page
{
    readonly WizardSettings _settings = WizardSettings.Load();
    readonly BackupRow _printersRow;

    public BackupPage()
    {
        InitializeComponent();
        Items = new(BackupService.Items.Select(item => new BackupRow(item)));
        _printersRow = Items.First(row => row.Item is PrintersBackupItem);
        SecretApps = new(_settings.SecretApps);
    }

    public ObservableCollection<BackupRow> Items { get; }

    public ObservableCollection<SecretApp> SecretApps { get; }

    string Folder => _settings.BackupFolder ?? BackupService.DefaultFolder;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        ShowFolder();
        await LoadPrintersAsync();
    }

    /// <summary>Lists this PC's printers to choose from. Ones unchecked before stay unchecked.</summary>
    async Task LoadPrintersAsync()
    {
        if (!_printersRow.Item.IsOnThisPc)
            return;
        try
        {
            foreach (var name in await PrintersBackupItem.GetPrintersAsync())
            {
                var choice = new PrinterChoice(name, !_settings.SkippedPrinters.Contains(name, StringComparer.OrdinalIgnoreCase));
                choice.PropertyChanged += (_, _) => SaveSkippedPrinters();
                _printersRow.Printers.Add(choice);
            }
            _printersRow.HasPrinters = _printersRow.Printers.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Append($"Couldn't list the printers, so a backup saves all of them: {ex.Message}");
        }
    }

    void SaveSkippedPrinters()
    {
        var listed = _printersRow.Printers.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Keep printers skipped before that aren't installed right now
        _settings.SkippedPrinters =
        [
            .. _settings.SkippedPrinters.Where(name => !listed.Contains(name)),
            .. _printersRow.Printers.Where(p => !p.IsSelected).Select(p => p.Name),
        ];
        SaveSettings();
    }

    void ShowFolder()
    {
        FolderText.Text = Directory.Exists(Folder) ? Folder : $"{Folder} (made when you back up)";
        foreach (var row in Items)
            row.Refresh(Folder);
    }

    async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        if (await picker.PickSingleFolderAsync() is not { } folder)
            return;

        _settings.BackupFolder = folder.Path;
        SaveSettings();
        ShowFolder();
    }

    async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var selected = Items.Where(row => row.IsSelected).ToList();
        var printers = _printersRow.Printers;
        ((PrintersBackupItem)_printersRow.Item).Printers = printers.All(p => p.IsSelected)
            ? null
            : printers.Where(p => p.IsSelected).Select(p => p.Name).ToList();
        SetButtonsEnabled(false);
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var row in selected)
            {
                if (!row.Item.IsOnThisPc)
                {
                    Log.Append($"Skipped {row.Item.Name}: it isn't on this PC.");
                    continue;
                }
                await RunAsync(row, "Backing up", "Backed up", () => BackupService.BackupAsync(Folder, row.Item, Log.Append));
            }

            BackupService.SaveSecretApps(Folder, SecretApps);
            Log.Append($"Saved the list of {SecretApps.Count} apps with secrets.");
        }
        catch (Exception ex)
        {
            Log.Append(ex.Message);
        }
        finally
        {
            SetButtonsEnabled(true);
            ShowFolder();
        }
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var selected = Items.Where(row => row.IsSelected && row.Info is not null).ToList();
        foreach (var row in Items.Where(row => row.IsSelected && row.Info is null))
            Log.Append($"Skipped {row.Item.Name}: there's no backup of it in {Folder}.");

        if (selected.Count > 0 && !await ConfirmRestoreAsync(selected))
            return;

        SetButtonsEnabled(false);
        try
        {
            foreach (var row in selected)
                await RunAsync(row, "Restoring", "Restored", () => BackupService.RestoreAsync(Folder, row.Item, Log.Append));

            var merged = BackupService.MergeSecretApps(SecretApps, BackupService.LoadSecretApps(Folder));
            if (merged.Count > SecretApps.Count)
            {
                Log.Append($"Added {merged.Count - SecretApps.Count} apps with secrets from the backup.");
                foreach (var app in merged.Skip(SecretApps.Count))
                    SecretApps.Add(app);
                SaveSecretApps();
            }
        }
        catch (Exception ex)
        {
            Log.Append(ex.Message);
        }
        finally
        {
            SetButtonsEnabled(true);
            ShowFolder();
        }
    }

    async Task<bool> ConfirmRestoreAsync(IEnumerable<BackupRow> rows)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore from the backup?",
            Content = "The backed-up copies replace what's on this PC:\n\n"
                + string.Join("\n", rows.Select(row => $"• {row.Item.Name}: {row.Item.Location}")),
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    async Task RunAsync(BackupRow row, string doing, string done, Func<Task> action)
    {
        row.IsBusy = true;
        row.StatusText = $"{doing}…";
        Log.Append($"{doing} {row.Item.Name}");
        try
        {
            await action();
            row.StatusText = done;
        }
        catch (Exception ex)
        {
            row.StatusText = "Failed";
            Log.Append(ex.Message);
        }
        finally
        {
            row.IsBusy = false;
            Log.Append("");
        }
    }

    void SetButtonsEnabled(bool enabled) => BackupButton.IsEnabled = RestoreButton.IsEnabled = FolderButton.IsEnabled = enabled;

    void AddSecretApp_Click(object sender, RoutedEventArgs e)
    {
        var name = SecretAppName.Text.Trim();
        if (name.Length == 0)
        {
            SecretAppsStatus.Text = "Type the app's name first.";
            return;
        }
        if (SecretApps.Any(app => string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SecretAppsStatus.Text = $"{name} is already on the list.";
            return;
        }

        SecretApps.Add(new SecretApp(name, SecretAppNote.Text.Trim()));
        SecretAppName.Text = SecretAppNote.Text = "";
        SecretAppsStatus.Text = "";
        SaveSecretApps();
    }

    void RemoveSecretApp_Click(object sender, RoutedEventArgs e)
    {
        SecretApps.Remove((SecretApp)((FrameworkElement)sender).DataContext);
        SaveSecretApps();
    }

    void SaveSecretApps()
    {
        _settings.SecretApps = [.. SecretApps];
        SaveSettings();
    }

    void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Append($"Couldn't save the wizard's settings: {ex.Message}");
        }
    }
}
