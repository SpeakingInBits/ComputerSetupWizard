using Microsoft.UI.Xaml.Controls;

namespace ComputerSetupWizard.Controls;

/// <summary>Scrolling command output. Safe to call from any thread.</summary>
public sealed partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    public void Append(string line)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => Append(line));
            return;
        }

        // winget redraws its progress bar and spinner with \r, so keep only the last frame
        var text = line.Split('\r').LastOrDefault(part => part.Trim().Length > 1)?.TrimEnd();
        if (text is null)
            return;

        LogText.Text = LogText.Text.Length == 0 ? text : LogText.Text + "\n" + text;
        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null, disableAnimation: true);
    }

    public void Clear() => LogText.Text = "";
}
