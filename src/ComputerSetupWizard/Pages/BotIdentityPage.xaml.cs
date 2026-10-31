using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed partial class BotIdentityPage : Page
{
    public BotIdentityPage()
    {
        InitializeComponent();
        InstallNote.Text = AppUpdater.IsInstalled
            ? $"Git runs the wizard from {AppInstall.Directory} to get bot tokens. It stays there when the wizard updates."
            : $"This copy of the wizard isn't installed, so git runs it from {AppInstall.Directory}, and pushes fail if it moves. " +
              "Install it with the Setup.exe from the latest release instead.";
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ShowStateAsync();

    async Task ShowStateAsync()
    {
        if (!GitHubCliService.IsGitInstalled)
        {
            ShowState(InfoBarSeverity.Warning, "Needs Git", "Install Git on the Tools step first.");
            SetUpButton.IsEnabled = RemoveButton.IsEnabled = CleanUpButton.IsEnabled = false;
            return;
        }

        var state = await BotIdentityService.GetStateAsync(AppInstall.Exe);
        switch (state)
        {
            case BotIdentityState.Current:
                ShowState(InfoBarSeverity.Success, "SpeakingInBits repos use the bot",
                    $"The bot's settings are in {BotIdentityService.IncludeFile}.");
                break;
            case BotIdentityState.Outdated:
                ShowState(InfoBarSeverity.Warning, "Needs updating",
                    "SpeakingInBits repos don't get all the bot's settings, for example because the wizard moved " +
                    "or a later global setting overrides them. Set it up again to fix it.");
                break;
            default:
                ShowState(InfoBarSeverity.Informational, "Not set up", "SpeakingInBits repos use your personal identity.");
                break;
        }
        RemoveButton.IsEnabled = state != BotIdentityState.Missing;
    }

    void ShowState(InfoBarSeverity severity, string title, string message)
    {
        StateBar.Severity = severity;
        StateBar.Title = title;
        StateBar.Message = message;
    }

    async void SetUp_Click(object sender, RoutedEventArgs e)
    {
        if (BotConfig.FindNewestKey() is null)
        {
            ShowState(InfoBarSeverity.Warning, "No bot key", "Add this machine's bot key on the Bot key step first, or pushes will fail.");
            return;
        }
        await UpdateAsync(() => BotIdentityService.SetUpAsync(AppInstall.Exe));
    }

    async void Remove_Click(object sender, RoutedEventArgs e) => await UpdateAsync(() => BotIdentityService.RemoveAsync());

    async Task UpdateAsync(Func<Task> update)
    {
        SetUpButton.IsEnabled = RemoveButton.IsEnabled = false;
        try
        {
            await update();
            await ShowStateAsync();
        }
        catch (Exception ex)
        {
            ShowState(InfoBarSeverity.Error, "Couldn't change the Git config", ex.Message);
        }
        finally
        {
            SetUpButton.IsEnabled = true;
        }
    }

    async void CleanUp_Click(object sender, RoutedEventArgs e)
    {
        CleanUpButton.IsEnabled = false;
        CleanUpProgress.IsActive = true;
        CleanUpStatus.Text = "";
        try
        {
            var repos = await RepoService.FindLocalSetupsAsync(RepoService.DefaultScanRoots());
            var lines = new List<string>();
            var cleaned = 0;
            foreach (var repo in repos)
            {
                try
                {
                    await RepoService.RemoveLocalSetupAsync(repo);
                    lines.Add(repo);
                    cleaned++;
                }
                catch (Exception ex)
                {
                    lines.Add($"{repo}: {ex.Message}");
                }
            }
            CleanUpStatus.Text = repos.Count == 0
                ? "No clones in the usual folders have the old settings."
                : string.Join(Environment.NewLine, lines.Prepend($"Cleaned up {cleaned} of {repos.Count}:"));
        }
        catch (Exception ex)
        {
            CleanUpStatus.Text = ex.Message;
        }
        finally
        {
            CleanUpButton.IsEnabled = true;
            CleanUpProgress.IsActive = false;
        }
    }
}
