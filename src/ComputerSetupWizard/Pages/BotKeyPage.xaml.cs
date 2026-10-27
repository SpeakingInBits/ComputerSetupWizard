using System.IO.Enumeration;
using System.Security.Cryptography;
using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace ComputerSetupWizard.Pages;

public sealed partial class BotKeyPage : Page
{
    public BotKeyPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ShowKey();

    void ShowKey()
    {
        var key = BotConfig.FindNewestKey();
        KeyBar.Severity = key is not null ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        KeyBar.Title = key is not null ? "This machine has a key" : "No key yet";
        KeyBar.Message = key is not null
            ? $"{key} (saved {File.GetLastWriteTime(key):d})"
            : $"There's no {BotConfig.KeyPattern} in {BotConfig.KeyDirectory}.";
        CheckButton.IsEnabled = key is not null;
    }

    async void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(BotConfig.AppSettingsUrl));

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var source = BotConfig.FindNewestKey(downloads) ?? await PickKeyAsync();
        if (source is null)
            return;

        try
        {
            ImportStatus.Text = $"Moved to {ImportKey(source)}";
        }
        catch (Exception ex)
        {
            ImportStatus.Text = ex.Message;
        }
        ShowKey();
    }

    /// <summary>
    /// Moves the key into <see cref="BotConfig.KeyDirectory"/> rather than copying it, so no stray copy
    /// stays in Downloads. Keys with other names are renamed so the wizard finds them.
    /// </summary>
    static string ImportKey(string source)
    {
        using (var rsa = RSA.Create())
        {
            try
            {
                rsa.ImportFromPem(File.ReadAllText(source));
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException($"{Path.GetFileName(source)} isn't a private key.");
            }
        }

        var name = Path.GetFileName(source);
        if (!FileSystemName.MatchesSimpleExpression(BotConfig.KeyPattern, name))
            name = $"speakinginbits-bot.{DateTime.Now:yyyy-MM-dd}.private-key.pem";

        Directory.CreateDirectory(BotConfig.KeyDirectory);
        var destination = Path.Combine(BotConfig.KeyDirectory, name);
        if (File.Exists(destination))
            throw new IOException($"{destination} already exists.");

        File.Move(source, destination);
        return destination;
    }

    static async Task<string?> PickKeyAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".pem");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        CheckProgress.IsActive = true;
        InfoText.Text = "";
        try
        {
            var info = await new BotTokenService().GetInfoAsync();
            InfoText.Text = $"""
                App           {info.AppName} ({info.AppSlug})
                Installation  {info.InstallationId}, {info.RepositorySelection} repositories
                Permissions   {info.Permissions}
                Commits as    {info.BotName} <{info.BotEmail}>
                Token expires {info.TokenExpires.ToLocalTime():t}
                """;
        }
        catch (Exception ex)
        {
            InfoText.Text = ex.Message;
        }
        finally
        {
            CheckButton.IsEnabled = true;
            CheckProgress.IsActive = false;
        }
    }
}
