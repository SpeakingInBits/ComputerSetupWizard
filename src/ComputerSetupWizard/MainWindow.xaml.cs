using ComputerSetupWizard.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace ComputerSetupWizard;

public sealed partial class MainWindow : Window
{
    static readonly Dictionary<string, Type> Pages = new()
    {
        ["Overview"] = typeof(OverviewPage),
        ["Tools"] = typeof(ToolsPage),
        ["GitHub"] = typeof(GitHubPage),
        ["BotKey"] = typeof(BotKeyPage),
        ["Repos"] = typeof(ReposPage),
        ["Claude"] = typeof(ClaudePage),
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // AppWindow sizes are in physical pixels; XamlRoot's scale isn't reliable until the window shows
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(780 * scale)));

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    IList<NavigationViewItem> Steps => Nav.MenuItems.Cast<NavigationViewItem>().ToList();

    int CurrentIndex => Steps.IndexOf((NavigationViewItem)Nav.SelectedItem);

    /// <summary>Opens the step with the given tag, such as "Repos".</summary>
    public void GoTo(string tag) => Nav.SelectedItem = Steps.First(item => (string)item.Tag == tag);

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
            return;

        ContentFrame.Navigate(Pages[tag], null, new EntranceNavigationTransitionInfo());

        var index = CurrentIndex;
        BackButton.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = index < Steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        if (index < Steps.Count - 1)
            NextButton.Content = $"Next: {Steps[index + 1].Content}";
    }

    void Back_Click(object sender, RoutedEventArgs e) => Nav.SelectedItem = Steps[CurrentIndex - 1];

    void Next_Click(object sender, RoutedEventArgs e) => Nav.SelectedItem = Steps[CurrentIndex + 1];

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
