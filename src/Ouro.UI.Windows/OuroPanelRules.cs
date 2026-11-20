// The rules core's Dialog, Sheet and ConfirmDialog specs state in words, as plain functions so the tests run them off
// Windows: how wide a centered panel is, which side a sheet comes from, and what may close a panel.
namespace Ouro.UI.Windows;

/// <summary>Why a panel closed (the reason on core's close event).</summary>
public enum OuroCloseReason
{
    /// <summary>The Escape key.</summary>
    Escape,
    /// <summary>The close button, or a confirm dialog's Cancel.</summary>
    Button,
    /// <summary>A press outside the panel, on the scrim.</summary>
    OutsidePress,
}

/// <summary>The centered panel's width (core's Dialog size).</summary>
public enum OuroDialogSize
{
    /// <summary>Small.</summary>
    Small,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>Large.</summary>
    Large,
    /// <summary>Extra large.</summary>
    ExtraLarge,
    /// <summary>The default: fits its content up to 512 points.</summary>
    Auto,
    /// <summary>The whole width.</summary>
    Full,
}

/// <summary>The edge a sheet comes from.</summary>
public enum OuroSheetSide
{
    /// <summary>The top edge.</summary>
    Top,
    /// <summary>The bottom edge: the default.</summary>
    Bottom,
    /// <summary>The left edge.</summary>
    Left,
    /// <summary>The right edge.</summary>
    Right,
}

/// <summary>What core's three panel specs decide, apart from how Windows draws it.</summary>
public static class OuroPanelRules
{
    /// <summary>The widest a dialog's panel gets, in effective pixels (core's maxWidthPoints).</summary>
    public const double DialogMaxWidth = 512;

    /// <summary>The close button's and each footer button's smallest target.</summary>
    public const double Target = 44;

    /// <summary>The panel's maximum width for a size; infinity for full.</summary>
    public static double MaxWidth(OuroDialogSize size) => size switch
    {
        OuroDialogSize.Small => 384,
        OuroDialogSize.Medium => 448,
        OuroDialogSize.Large => 512,
        OuroDialogSize.ExtraLarge => 672,
        OuroDialogSize.Full => double.PositiveInfinity,
        _ => DialogMaxWidth,
    };

    /// <summary>Whether a press outside the panel closes it: a sheet that isn't modal never does unless asked; the default otherwise is yes.</summary>
    public static bool ClosesOnOutsidePress(bool? requested, bool modal = true) => requested ?? modal;

    /// <summary>Whether the reason may close a panel that has this outside-press setting. Escape and the buttons always can.</summary>
    public static bool MayClose(OuroCloseReason reason, bool closesOnOutsidePress) => reason != OuroCloseReason.OutsidePress || closesOnOutsidePress;

    /// <summary>The question a confirm dialog asks: the action's confirm text when it has one, else what the caller wrote.</summary>
    public static string Question(string? confirmText, string description) => string.IsNullOrEmpty(confirmText) ? description : confirmText;
}
