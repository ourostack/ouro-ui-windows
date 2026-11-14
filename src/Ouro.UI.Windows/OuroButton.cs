// Core's Button (vendor/core/components/button.spec.json) as a templated WinUI Button, so it keeps WinUI's keyboard
// handling, focus ring and UI Automation peer (ControlType.Button with Invoke). Give it an Action and a Runtime and a
// press runs that action through the person's door; its words, hint and automation id come from the declaration.
using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ouro.Actions;
using Ouro.UI.Hx.Tokens;

namespace Ouro.UI.Windows;

/// <summary>Button heights: every size keeps a 44 epx target.</summary>
public enum OuroSize
{
    /// <summary>Small: tighter padding, same 44 epx target.</summary>
    Small,
    /// <summary>Medium, the default.</summary>
    Medium,
    /// <summary>Large.</summary>
    Large,
}

/// <summary>A control a person presses to do one thing, such as Add to list or Place order.</summary>
public sealed partial class OuroButton : Button
{
    /// <summary>Core's minimum target, in effective pixels.</summary>
    public const double MinTarget = 44;

    // No backplate: under a contrast theme the button already draws the system highlight pair, and WinUI's own backplate
    // put a black box behind the words on the highlight fill (CI, 2026-11-14).
    private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private ProgressRing? spinner;
    private OuroPaintResult paint;
    private bool wasBusy;
    private OuroActionInfo? action;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), typeof(OuroTone), OuroTone.Primary);
    public static readonly DependencyProperty VariantProperty = Register(nameof(Variant), typeof(OuroVariant), OuroVariant.Solid);
    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), typeof(OuroSize), OuroSize.Medium);
    public static readonly DependencyProperty IsBusyProperty = Register(nameof(IsBusy), typeof(bool), false);

    public OuroButton()
    {
        DefaultStyleKey = typeof(OuroButton);
        Content = label;
        CornerRadius = new CornerRadius(OuroUI.Theme.Radius);
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsPointerOverProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Press());
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
        Click += OnClick;
    }

    /// <summary>The words on it, which are also its spoken name. Set by <see cref="Action"/> when it has one.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The declared action it runs: its label, description and id become the words, the hint and the automation id.</summary>
    public OuroActionInfo? Action
    {
        get => action;
        set { action = value; Changed(); }
    }

    /// <summary>The colour role from the theme.</summary>
    public OuroTone Tone { get => (OuroTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>Solid, soft, outline or plain.</summary>
    public OuroVariant Variant { get => (OuroVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>Small, medium or large; all keep a 44 epx target.</summary>
    public OuroSize Size { get => (OuroSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Its action is running: a spinner in place of the words, the same width, and it can't be pressed again.</summary>
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }

    /// <summary>The runtime a press of <see cref="Action"/> goes through, at the person's door.</summary>
    public OuroActionRuntime? Runtime { get; set; }

    /// <summary>The input for a press of <see cref="Action"/>, such as the text in a field. Empty when not set.</summary>
    public Func<JsonObject>? Input { get; set; }

    /// <summary>What a press of <see cref="Action"/> answered.</summary>
    public event EventHandler<OuroOutcome>? Ran;

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroButton), new PropertyMetadata(fallback, (d, _) => ((OuroButton)d).Changed()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        spinner = GetTemplateChild("Spinner") as ProgressRing;
        Refresh();
    }

    private void Changed()
    {
        if (Action is { } a)
        {
            AutomationProperties.SetAutomationId(this, a.AutomationId);
            AutomationProperties.SetHelpText(this, a.Description);
        }
        var words = Action is { } action && string.IsNullOrEmpty(Label) ? action.Label : Label;
        label.Text = words;
        AutomationProperties.SetName(this, words);
        if (IsBusy != wasBusy)
        {
            wasBusy = IsBusy;
            IsEnabled = !IsBusy;
        }
        Refresh();
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (Action is not { } action || Runtime is not { } runtime || IsBusy) return;
        IsBusy = true;
        try
        {
            var outcome = await runtime.InvokeAsync(new OuroCall(action.Id, Input?.Invoke() ?? new JsonObject(), OuroDoor.Person) { Caller = "person" });
            Ran?.Invoke(this, outcome);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Press()
    {
        CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        var s = IsPressed ? OuroUI.PressScale : 1f;
        ScaleTransition = OuroUI.AnimationsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(OuroTokens.Duration.Press) } : null;
        Scale = new Vector3(s, s, 1);
        Refresh();
    }

    private void Refresh()
    {
        paint = OuroUI.Paint(this, Tone, Variant);
        var fill = IsPressed ? paint.Pressed : IsPointerOver ? paint.Hover : paint.Fill;
        Background = OuroUI.Brush(fill);
        Foreground = OuroUI.Brush(paint.Label);
        label.Foreground = Foreground;
        BorderBrush = OuroUI.Brush(paint.Edge);
        BorderThickness = new Thickness(paint.Edge == 0 ? 0 : 1);
        Padding = Size switch
        {
            OuroSize.Small => new Thickness(12, 6, 12, 6),
            OuroSize.Large => new Thickness(24, 14, 24, 14),
            _ => new Thickness(16, 10, 16, 10),
        };
        MinWidth = MinHeight = MinTarget;
        Opacity = IsEnabled || IsBusy ? 1 : 0.5;
        label.Opacity = IsBusy ? 0 : 1;
        if (spinner is not null)
        {
            spinner.IsActive = IsBusy;
            spinner.Visibility = IsBusy ? Visibility.Visible : Visibility.Collapsed;
            spinner.Foreground = Foreground;
        }
        OuroUI.Report(this, IsBusy ? "Busy" : "", "button",
            ("tone", Tone), ("variant", Variant), ("fill", OuroPaint.Hex(paint.Fill)), ("label", OuroPaint.Hex(paint.Label)), ("edge", OuroPaint.Hex(paint.Edge)), ("behind", OuroPaint.Hex(paint.Behind(OuroUI.Surface(this)))),
            ("pressScale", OuroUI.PressScale), ("trimmed", label.IsTextTrimmed), ("textHeight", Math.Round(label.ActualHeight)));
    }
}
