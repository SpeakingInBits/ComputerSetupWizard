using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed partial class GitHubPage : Page
{
    public GitHubPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RefreshAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    async Task RefreshAsync()
    {
        ToolPaths.RefreshPath();
        SignInButton.IsEnabled = GitHubCliService.IsGhInstalled;
        SaveButton.IsEnabled = GitHubCliService.IsGitInstalled;

        var login = await GitHubCliService.GetLoginAsync();
        LoginBar.Severity = login.SignedIn ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        LoginBar.Title = login.SignedIn ? $"Signed in as {login.Account}" : "Not signed in";
        LoginBar.Message = GitHubCliService.IsGhInstalled ? "" : "Install the GitHub CLI on the Tools step first.";
        SignInButton.Content = login.SignedIn ? "Sign in again…" : "Sign in…";

        if (!GitHubCliService.IsGitInstalled)
        {
            IdentityStatus.Text = "Install Git on the Tools step first.";
            return;
        }

        var (name, email) = await GitHubCliService.GetGlobalIdentityAsync();
        IdentityStatus.Text = name is not null && email is not null ? "Saved" : "Not set yet";
        if (name is null || email is null)
        {
            var suggested = login.SignedIn ? await GitHubCliService.GetSuggestedIdentityAsync() : (null, null);
            name ??= suggested.Name;
            email ??= suggested.Email;
        }
        NameBox.Text = name ?? "";
        EmailBox.Text = email ?? "";
    }

    async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = RefreshButton.IsEnabled = false;
        try
        {
            await GitHubCliService.SignInAsync();
            var setup = await GitHubCliService.SetupGitAsync();
            if (!setup.Succeeded)
                LoginBar.Message = $"gh auth setup-git failed: {setup.AllText}";
            // gh's helper can land after the bot include, which would make SpeakingInBits pushes use your account
            await BotIdentityService.KeepLastAsync(AppInstall.Exe);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
        await RefreshAsync();
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        if (name.Length == 0 || !email.Contains('@'))
        {
            IdentityStatus.Text = "Enter a name and an email address.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await GitHubCliService.SetGlobalIdentityAsync(name, email);
            // A new [user] section goes at the end, after the bot include, and would override the bot
            await BotIdentityService.KeepLastAsync(AppInstall.Exe);
            IdentityStatus.Text = "Saved";
        }
        catch (Exception ex)
        {
            IdentityStatus.Text = ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
