// Core's Sheet (vendor/core/components/sheet.spec.json): a panel from an edge of the window. UI Automation sees a Pane named
// by its title, with the title as a level 2 heading and a Close button. Windows has no drag-to-dismiss with a mouse (the
// spec's windows deviation): Close and Escape always work.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Ouro.UI.Windows;

/// <summary>A panel that slides in from an edge, over a scrim when modal.</summary>
public sealed partial class OuroSheet : OuroPanel
{
    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(nameof(Side), typeof(OuroSheetSide), typeof(OuroSheet), new PropertyMetadata(OuroSheetSide.Bottom, (d, _) => ((OuroSheet)d).Refresh()));
    public static readonly DependencyProperty ModalProperty = DependencyProperty.Register(nameof(Modal), typeof(bool), typeof(OuroSheet), new PropertyMetadata(true));
    public static readonly DependencyProperty CloseOnOutsidePressProperty = DependencyProperty.Register(nameof(CloseOnOutsidePress), typeof(bool?), typeof(OuroSheet), new PropertyMetadata(null));

    /// <summary>The edge it comes from. Default bottom.</summary>
    public OuroSheetSide Side { get => (OuroSheetSide)GetValue(SideProperty); set => SetValue(SideProperty, value); }

    /// <summary>Whether it sits over a scrim and holds focus. Default true; a non-modal sheet leaves the page usable.</summary>
    public bool Modal { get => (bool)GetValue(ModalProperty); set => SetValue(ModalProperty, value); }

    /// <summary>Whether a press on the scrim closes it. Default: yes when modal, no when not.</summary>
    public bool? CloseOnOutsidePress { get => (bool?)GetValue(CloseOnOutsidePressProperty); set => SetValue(CloseOnOutsidePressProperty, value); }

    public override bool ClosesOnOutsidePress => OuroPanelRules.ClosesOnOutsidePress(CloseOnOutsidePress, Modal);

    protected override string Kind => "sheet";

    protected override AutomationControlType Role => AutomationControlType.Pane;

    protected override bool TakesFocus => Modal;

    internal override (string, object?)[] Facts() => [("side", Side), ("modal", Modal), ("outsidePress", ClosesOnOutsidePress)];

    internal override void Place(FrameworkElement scrim, double hostWidth, double hostHeight)
    {
        var r = OuroUI.Theme.Radius;
        HorizontalAlignment = Side switch { OuroSheetSide.Left => HorizontalAlignment.Left, OuroSheetSide.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch };
        VerticalAlignment = Side switch { OuroSheetSide.Top => VerticalAlignment.Top, OuroSheetSide.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Stretch };
        Corners = Side switch
        {
            OuroSheetSide.Top => new CornerRadius(0, 0, r, r),
            OuroSheetSide.Left => new CornerRadius(0, r, r, 0),
            OuroSheetSide.Right => new CornerRadius(r, 0, 0, r),
            _ => new CornerRadius(r, r, 0, 0),
        };
        Frame.MaxWidth = Side is OuroSheetSide.Left or OuroSheetSide.Right ? 384 : double.PositiveInfinity;
        Refresh();
    }
}
