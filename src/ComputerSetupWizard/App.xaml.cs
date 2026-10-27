using Microsoft.UI.Xaml;

namespace ComputerSetupWizard;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    public static MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
