using ComputerSetupWizard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ComputerSetupWizard.Pages;

public sealed partial class ClaudePage : Page
{
    public ClaudePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ShowState();

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
