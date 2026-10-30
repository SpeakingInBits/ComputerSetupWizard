using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace ComputerSetupWizard;

/// <summary>
/// The exe is a console app, so scripts, git and Claude can capture the command line's output.
/// The manifest's detached console policy keeps Windows 11 from opening a console when the wizard
/// is started from Explorer; older Windows opens one, which is closed here before the window shows.
/// </summary>
public static partial class Program
{
    /// <summary>With arguments, runs a command (see <see cref="Cli"/>); without, opens the wizard.</summary>
    [STAThread]
    static int Main(string[] args)
    {
        // Must run first. Setup.exe and the updater start the exe with their own arguments (shortcuts, uninstalling),
        // which Run() handles before exiting. Opening the window also installs an update downloaded last time, but a
        // command doesn't: git and Claude read its output, and installing restarts the exe.
        VelopackApp.Build().SetAutoApplyOnStartup(args.Length == 0).Run();

        if (args.Length > 0)
            return Cli.Run(args);

        ReleaseOwnConsole();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callbackParams =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// <summary>Closes the console if Windows made one just for this process, but not a terminal it was started from.</summary>
    static void ReleaseOwnConsole()
    {
        var processes = new uint[2];
        if (GetConsoleProcessList(processes, (uint)processes.Length) == 1)
            FreeConsole();
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleProcessList([Out] uint[] processList, uint count);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
