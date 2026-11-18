// Core's Checkbox (vendor/core/components/checkbox.spec.json) as a templated WinUI CheckBox, so it keeps WinUI's Space
// key, focus ring and Toggle pattern (On, Off, Indeterminate). UI Automation sees one CheckBox named by its label
// (", required" when it is), whose help text is its error then its description. The row is at least 44 epx tall, and
// the box scales to core's press scale while pressed, never with Windows animations off.
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Shapes = Microsoft.UI.Xaml.Shapes;
using Ouro.UI.Hx.Tokens;

namespace Ouro.UI.Windows;

/// <summary>A box and what checking it means: checked, not checked, or mixed (<c>IsChecked = null</c>).</summary>
public sealed partial class OuroCheckbox : CheckBox
{
    /// <summary>Core's metrics, in effective pixels: the box, the row's minimum height and the gap to the words.</summary>
    public const double BoxSize = 20, MinRowHeight = 44, Gap = 12;

    private Border? box;
    private Shapes.Path? tick;
    private Shapes.Rectangle? dash;
    private TextBlock? label, description, error;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ErrorProperty = Register(nameof(Error), typeof(string), "");
    public static readonly DependencyProperty IsRequiredProperty = Register(nameof(IsRequired), typeof(bool), false);
    public static readonly DependencyProperty HideLabelProperty = Register(nameof(HideLabel), typeof(bool), false);

    public OuroCheckbox()
    {
        DefaultStyleKey = typeof(OuroCheckbox);
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Press());
        RegisterPropertyChangedCallback(IsPointerOverProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(IsCheckedProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What checking it means, beside the box, and its spoken name.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Supporting words under the label, read after the name. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>What is wrong: the box's edge turns destructive, and the words show under the label.</summary>
    public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    /// <summary>The box must be checked: the label gets a marker and the name says required.</summary>
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }

    /// <summary>Hides the label's words; the box keeps the label as its name.</summary>
    public bool HideLabel { get => (bool)GetValue(HideLabelProperty); set => SetValue(HideLabelProperty, value); }

    private static DependencyProperty Register(string name, System.Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroCheckbox), new PropertyMetadata(fallback, (d, _) => ((OuroCheckbox)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        box = GetTemplateChild("Box") as Border;
        tick = GetTemplateChild("Tick") as Shapes.Path;
        dash = GetTemplateChild("Dash") as Shapes.Rectangle;
        label = GetTemplateChild("LabelText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        error = GetTemplateChild("ErrorText") as TextBlock;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroCheckboxAutomationPeer(this);

    /// <summary>The spoken name: the label, with ", required" when it is.</summary>
    internal string SpokenName => IsRequired ? $"{Label}, required" : Label;

    private void Press()
    {
        if (box is null) return;
        box.CenterPoint = new Vector3((float)BoxSize / 2, (float)BoxSize / 2, 0);
        var s = IsPressed ? OuroUI.PressScale : 1f;
        box.ScaleTransition = OuroUI.AnimationsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(OuroTokens.Duration.Press) } : null;
        box.Scale = new Vector3(s, s, 1);
        Refresh();
    }

    private void Refresh()
    {
        var p = OuroUI.SurfacePaint(this);
        var on = IsChecked != false;
        var hasError = !string.IsNullOrEmpty(Error);
        var edge = on ? p.Checked : hasError ? p.Error : p.Edge;
        MinHeight = MinRowHeight;
        Opacity = IsEnabled ? 1 : 0.5;
        if (box is not null)
        {
            box.Width = box.Height = BoxSize;
            box.Background = OuroUI.Brush(on ? p.Checked : p.Field);
            box.BorderBrush = OuroUI.Brush(edge);
            box.BorderThickness = new Thickness(2);
            box.CornerRadius = new CornerRadius(Math.Min(4, OuroUI.Theme.Radius / 4));
        }
        if (tick is not null)
        {
            tick.Stroke = OuroUI.Brush(p.Tick);
            tick.Visibility = IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        if (dash is not null)
        {
            dash.Fill = OuroUI.Brush(p.Tick);
            dash.Visibility = IsChecked is null ? Visibility.Visible : Visibility.Collapsed;
        }
        if (label is not null)
        {
            label.Inlines.Clear();
            label.Inlines.Add(new Run { Text = Label });
            if (IsRequired) label.Inlines.Add(new Run { Text = " *", Foreground = OuroUI.Brush(p.Error) });
            label.Foreground = OuroUI.Brush(p.Text);
            label.Visibility = HideLabel || string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
        }
        Show(description, Description, p.Muted);
        Show(error, Error, p.Error);
        AutomationProperties.SetHelpText(this, string.Join(" ", new[] { Error, Description }.Where(s => !string.IsNullOrEmpty(s))));
        AutomationProperties.SetIsRequiredForForm(this, IsRequired);
        OuroUI.Report(this, "", "checkbox", ("label", OuroPaint.Hex(p.Text)), ("description", OuroPaint.Hex(p.Muted)), ("error", OuroPaint.Hex(p.Error)),
            ("edge", OuroPaint.Hex(edge)), ("checked", OuroPaint.Hex(p.Checked)), ("tick", OuroPaint.Hex(p.Tick)), ("behind", OuroPaint.Hex(p.Behind)),
            ("state", IsChecked switch { true => "on", false => "off", _ => "mixed" }), ("pressScale", OuroUI.PressScale),
            ("trimmed", (label?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(label?.ActualHeight ?? 0)));
    }

    private static void Show(TextBlock? block, string text, uint colour)
    {
        if (block is null) return;
        block.Text = text;
        block.Foreground = OuroUI.Brush(colour);
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>A checkbox to UI Automation: WinUI's CheckBox with the Toggle pattern, named by its label.</summary>
public sealed partial class OuroCheckboxAutomationPeer(OuroCheckbox owner) : CheckBoxAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(OuroCheckbox);

    protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.SpokenName;
}
