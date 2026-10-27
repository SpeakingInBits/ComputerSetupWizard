using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ComputerSetupWizard;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum Status { Checking, Ok, Warning, Error }

/// <summary>The icon and color for a <see cref="Status"/> in lists.</summary>
public static class StatusVisuals
{
    public static string Glyph(Status status) => status switch
    {
        Status.Ok => "",      // CheckMark
        Status.Warning => "", // Warning
        Status.Error => "",   // Cancel
        _ => "",
    };

    public static Brush Brush(Status status) => (Brush)Application.Current.Resources[status switch
    {
        Status.Ok => "SystemFillColorSuccessBrush",
        Status.Warning => "SystemFillColorCautionBrush",
        Status.Error => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    }];
}
