// Ouro HX's Button on Windows: a real WinUI Button, restyled through its own lightweight-styling resources, so it
// keeps WinUI's focus visuals, keyboard handling, UI Automation peer and contrast-theme behaviour. It renders core's
// Button contract: a 44 epx target, a press that scales only when Windows animations are on, a label that wraps at
// any text size, and the token colours (the system's own colours under a contrast theme).
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ouro.Actions.Generated;
using Windows.UI.ViewManagement;

namespace Ouro.UI.WinUI;

public enum OuroButtonVariant { Primary, Secondary }

public sealed class OuroButton : Button
{
    /// <summary>Core's minimum target, in effective pixels.</summary>
    public const double MinTarget = 44;

    private static readonly UISettings Settings = new();
    private readonly TextBlock label;

    public OuroButton(OuroActionInfo action, OuroButtonVariant variant = OuroButtonVariant.Primary)
    {
        label = new TextBlock { Text = action.Label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsTextScaleFactorEnabled = true };
        Content = label;
        MinWidth = MinTarget;
        MinHeight = MinTarget;
        MaxWidth = 220;
        Padding = new Thickness(16, 10, 16, 10);
        CornerRadius = new CornerRadius(OuroTokens.Radius);
        AutomationProperties.SetName(this, action.Label);
        AutomationProperties.SetAutomationId(this, action.AutomationId);
        AutomationProperties.SetHelpText(this, action.Description);
        if (!new AccessibilitySettings().HighContrast) Restyle(ActualTheme == ElementTheme.Dark || Application.Current.RequestedTheme == ApplicationTheme.Dark ? OuroTokens.Dark : OuroTokens.Light, variant);

        if (Settings.AnimationsEnabled) ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(OuroTokens.PressMillis) };
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => Press(true)), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => Press(false)), true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => Press(false)), true);
        AddHandler(PointerExitedEvent, new PointerEventHandler((_, _) => Press(false)), true);
        Loaded += (_, _) => Report();
        SizeChanged += (_, _) => Report();
    }

    /// <summary>The scale a press shrinks to: core's press scale, or none when Windows animations are off.</summary>
    public static float PressScale(bool animationsEnabled) => animationsEnabled ? OuroTokens.PressScale : 1f;

    private void Press(bool down)
    {
        CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        var s = down ? PressScale(Settings.AnimationsEnabled) : 1f;
        Scale = new Vector3(s, s, 1);
    }

    private void Restyle(OuroColors c, OuroButtonVariant variant)
    {
        var (bg, fg) = variant == OuroButtonVariant.Primary ? (c.Primary, c.PrimaryForeground) : (c.Secondary, c.SecondaryForeground);
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            Resources["ButtonBackground" + state] = new SolidColorBrush(bg);
            Resources["ButtonForeground" + state] = new SolidColorBrush(fg);
            Resources["ButtonBorderBrush" + state] = new SolidColorBrush(bg);
        }
    }

    /// <summary>What the claims tests read through UI Automation: the press scale in effect, the text scale, and whether the label is cut off.</summary>
    private void Report() => AutomationProperties.SetItemStatus(this,
        $"pressScale={PressScale(Settings.AnimationsEnabled):0.00};textScale={Settings.TextScaleFactor:0.00};trimmed={label.IsTextTrimmed.ToString().ToLowerInvariant()};labelHeight={label.ActualHeight:0}");
}
