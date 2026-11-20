// Puts an OuroPanel over a page: a scrim across the host and the panel placed on it, drawn in the window itself.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Ouro.UI.Windows;

/// <summary>Shows a Dialog, Sheet or ConfirmDialog over a page.</summary>
public static class OuroOverlay
{
    /// <summary>Adds the panel and its scrim to the host grid (which should span the page). Closing the panel removes both.</summary>
    public static void Show(Grid host, OuroPanel panel)
    {
        var modal = panel is not OuroSheet { Modal: false };
        Border? scrim = null;
        if (modal)
        {
            uint colour = OuroUI.HighContrast ? (OuroUI.System(global::Windows.UI.ViewManagement.UIElementType.Window) & 0x00FFFFFF) | 0xB3000000 : 0x66000000;
            scrim = new Border { Background = OuroUI.Brush(colour) };
            scrim.Tapped += (_, _) => panel.Close(OuroCloseReason.OutsidePress);
            Grid.SetRowSpan(scrim, Math.Max(1, host.RowDefinitions.Count));
            Grid.SetColumnSpan(scrim, Math.Max(1, host.ColumnDefinitions.Count));
            host.Children.Add(scrim);
        }
        Grid.SetRowSpan(panel, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumnSpan(panel, Math.Max(1, host.ColumnDefinitions.Count));
        host.Children.Add(panel);
        panel.Place(scrim ?? (FrameworkElement)panel, host.ActualWidth, host.ActualHeight);
        var opener = modal ? FocusManager.GetFocusedElement(host.XamlRoot) as Control : null;
        KeyEventHandler escape = (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape && panel.Close(OuroCloseReason.Escape)) e.Handled = true;
        };
        if (modal) host.KeyDown += escape;
        panel.Closed += (_, _) =>
        {
            host.KeyDown -= escape;
            opener?.Focus(FocusState.Programmatic);
            host.Children.Remove(panel);
            if (scrim is not null) host.Children.Remove(scrim);
        };
    }
}
