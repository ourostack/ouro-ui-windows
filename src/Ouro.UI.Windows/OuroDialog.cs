// Core's Dialog (vendor/core/components/dialog.spec.json): a centered panel over a scrim. UI Automation sees a Window named
// by its title, with the title as a level 2 heading and a Close button. Windows has no bottom sheet form of it unless asked
// for: presentation auto is always the centered panel (the spec's windows deviation).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Ouro.UI.Windows;

/// <summary>How a Dialog is drawn: auto is the centered panel on Windows; sheet slides it from the bottom edge.</summary>
public enum OuroPresentation
{
    /// <summary>The centered panel.</summary>
    Auto,
    /// <summary>The centered panel.</summary>
    Dialog,
    /// <summary>A panel from the bottom edge.</summary>
    Sheet,
}

/// <summary>A centered panel with a title, content and a footer, which closes by Escape, its Close button or a press outside it.</summary>
public sealed partial class OuroDialog : OuroPanel
{
    public static readonly DependencyProperty CloseOnOutsidePressProperty = DependencyProperty.Register(nameof(CloseOnOutsidePress), typeof(bool), typeof(OuroDialog), new PropertyMetadata(true));
    public static readonly DependencyProperty PresentationProperty = DependencyProperty.Register(nameof(Presentation), typeof(OuroPresentation), typeof(OuroDialog), new PropertyMetadata(OuroPresentation.Auto, (d, _) => ((OuroDialog)d).Resized()));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(OuroDialogSize), typeof(OuroDialog), new PropertyMetadata(OuroDialogSize.Auto, (d, _) => ((OuroDialog)d).Resized()));

    /// <summary>Whether a press on the scrim closes it. Default true.</summary>
    public bool CloseOnOutsidePress { get => (bool)GetValue(CloseOnOutsidePressProperty); set => SetValue(CloseOnOutsidePressProperty, value); }

    /// <summary>Auto and dialog are the centered panel; sheet comes from the bottom edge.</summary>
    public OuroPresentation Presentation { get => (OuroPresentation)GetValue(PresentationProperty); set => SetValue(PresentationProperty, value); }

    /// <summary>The centered panel's width: small, medium, large, extra large, auto (up to 512) or full.</summary>
    public OuroDialogSize Size { get => (OuroDialogSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public override bool ClosesOnOutsidePress => OuroPanelRules.ClosesOnOutsidePress(CloseOnOutsidePress);

    protected override string Kind => "dialog";

    protected override AutomationControlType Role => AutomationControlType.Window;

    internal override (string, object?)[] Facts() => [("size", Size), ("maxWidth", OuroPanelRules.MaxWidth(Size)), ("outsidePress", ClosesOnOutsidePress)];

    private void Resized()
    {
        Frame.MaxWidth = Presentation == OuroPresentation.Sheet ? double.PositiveInfinity : OuroPanelRules.MaxWidth(Size);
        Refresh();
    }

    internal override void Place(FrameworkElement scrim, double hostWidth, double hostHeight)
    {
        var sheet = Presentation == OuroPresentation.Sheet;
        HorizontalAlignment = sheet ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        VerticalAlignment = sheet ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        Margin = sheet ? new Thickness(0) : new Thickness(16);
        Corners = sheet ? new CornerRadius(OuroUI.Theme.Radius, OuroUI.Theme.Radius, 0, 0) : new CornerRadius(OuroUI.Theme.Radius);
        Resized();
    }
}
