// Core's Select (vendor/core/components/select.spec.json): a label, a button that shows the choice, and a list of options.
// The button is a WinUI ComboBox, so it keeps WinUI's keys (Space, Enter or Down opens, arrows move and stop at the ends,
// Enter chooses, Escape closes and returns focus), its flyout over the button and its ExpandCollapse and Value patterns.
// UI Automation sees one ComboBox named by the label (", required" when it is), help text the error then the
// description; the words drawn around it are out of the control view, so nothing is read twice. Every option is at least
// 44 epx tall.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Ouro.UI.Windows;

/// <summary>One choice in a <see cref="OuroSelect"/>: the value the app reads and the words that name it.</summary>
public sealed record OuroSelectOption(string Value, string Label);

/// <summary>A labelled list of choices of which one is chosen. For two or three choices that should all show, use radio buttons; for a yes or no, use <see cref="OuroSwitch"/> or <see cref="OuroCheckbox"/>.</summary>
public sealed partial class OuroSelect : Control
{
    /// <summary>Core's metrics, in effective pixels: the button's and an option's minimum height, and the side padding.</summary>
    public const double MinButtonHeight = 44, OptionMinHeight = 44, PaddingX = 12;

    private TextBlock? label, description, error;
    private ComboBox? box;
    private bool syncing;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(OuroSelect), new PropertyMetadata("", (d, _) => ((OuroSelect)d).ValueSet()));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ErrorProperty = Register(nameof(Error), typeof(string), "");
    public static readonly DependencyProperty IsRequiredProperty = Register(nameof(IsRequired), typeof(bool), false);
    public static readonly DependencyProperty HideLabelProperty = Register(nameof(HideLabel), typeof(bool), false);

    public OuroSelect()
    {
        DefaultStyleKey = typeof(OuroSelect);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the choice is for: shown above it, and its spoken name. A placeholder is never the name.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The value of the chosen option; empty until one is chosen.</summary>
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Shown in the button while nothing is chosen.</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <summary>Supporting words under the label, read after the name. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>What is wrong. The edge turns destructive and the words show under the button; read before the description.</summary>
    public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    /// <summary>A choice is required: the label gets a marker and the name says required.</summary>
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }

    /// <summary>Hides the label's words; the select keeps the label as its name.</summary>
    public bool HideLabel { get => (bool)GetValue(HideLabelProperty); set => SetValue(HideLabelProperty, value); }

    /// <summary>The choices, in order. Set it before the first choice is made.</summary>
    public IReadOnlyList<OuroSelectOption> Options
    {
        get => options;
        set
        {
            options = value;
            Fill();
        }
    }

    private IReadOnlyList<OuroSelectOption> options = Array.Empty<OuroSelectOption>();

    /// <summary>An option was chosen; the argument is its value.</summary>
    public event EventHandler<string>? Changed;

    private static DependencyProperty Register(string name, System.Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroSelect), new PropertyMetadata(fallback, (d, _) => ((OuroSelect)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        label = GetTemplateChild("LabelText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        error = GetTemplateChild("ErrorText") as TextBlock;
        box = GetTemplateChild("Box") as ComboBox;
        if (box is not null)
        {
            box.SelectionChanged += (_, _) =>
            {
                if (syncing) return;
                var chosen = (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (chosen != Value) Value = chosen;
            };
            box.GotFocus += (_, _) => Refresh();
            box.LostFocus += (_, _) => Refresh();
            box.DropDownOpened += (_, _) => Refresh();
            box.DropDownClosed += (_, _) => Refresh();
        }
        Fill();
    }

    private void Fill()
    {
        if (box is null) return;
        syncing = true;
        box.Items.Clear();
        foreach (var o in options) box.Items.Add(new ComboBoxItem { Content = o.Label, Tag = o.Value, MinHeight = OptionMinHeight });
        syncing = false;
        ValueSet();
    }

    private void ValueSet()
    {
        if (box is not null)
        {
            syncing = true;
            box.SelectedIndex = options.ToList().FindIndex(o => o.Value == Value);
            syncing = false;
        }
        if (!string.IsNullOrEmpty(Value)) Changed?.Invoke(this, Value);
        Refresh();
    }

    private void Refresh()
    {
        if (box is null) return;
        var p = OuroUI.SurfacePaint(this);
        var focused = box.FocusState != FocusState.Unfocused;
        var hasError = !string.IsNullOrEmpty(Error);
        var edge = focused ? p.Focus : hasError ? p.Error : p.Edge;
        var thickness = focused ? 2 : 1;

        if (label is not null)
        {
            label.Inlines.Clear();
            label.Inlines.Add(new Run { Text = Label });
            // The marker is drawn, not spoken: the name says "required" itself.
            if (IsRequired) label.Inlines.Add(new Run { Text = " *", Foreground = OuroUI.Brush(p.Error) });
            label.Foreground = OuroUI.Brush(p.Text);
            label.Visibility = HideLabel || string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
        }
        Show(description, Description, p.Muted);
        Show(error, Error, p.Error);

        box.PlaceholderText = Placeholder;
        box.PlaceholderForeground = OuroUI.Brush(p.Placeholder);
        box.Background = OuroUI.Brush(p.Field);
        box.Foreground = OuroUI.Brush(p.Text);
        box.BorderBrush = OuroUI.Brush(edge);
        box.BorderThickness = new Thickness(thickness);
        box.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
        box.MinHeight = MinButtonHeight;
        box.Padding = new Thickness(PaddingX - thickness, 0, PaddingX - thickness, 0);
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.IsEnabled = IsEnabled;
        // Pressed takes the accent fill and never scales; the state brushes are the kit's, so a contrast theme keeps its own.
        foreach (var state in new[] { "PointerOver", "Pressed", "Focused" })
        {
            box.Resources[$"ComboBoxBackground{state}"] = OuroUI.Brush(state == "Pressed" ? p.Checked : p.Field);
            box.Resources[$"ComboBoxForeground{state}"] = OuroUI.Brush(state == "Pressed" ? p.Tick : p.Text);
            box.Resources[$"ComboBoxBorderBrush{state}"] = OuroUI.Brush(edge);
        }
        Opacity = IsEnabled ? 1 : 0.5;

        var help = OuroFormRules.Help(Error, Description);
        AutomationProperties.SetName(box, OuroFormRules.Name(Label, IsRequired));
        AutomationProperties.SetHelpText(box, help);
        AutomationProperties.SetIsRequiredForForm(box, IsRequired);
        if (AutomationProperties.GetAutomationId(this) is { Length: > 0 } id) AutomationProperties.SetAutomationId(box, id);
        OuroUI.Report(box, "", "select", ("label", OuroPaint.Hex(p.Text)), ("text", OuroPaint.Hex(p.Text)), ("description", OuroPaint.Hex(p.Muted)),
            ("placeholder", OuroPaint.Hex(p.Placeholder)), ("error", OuroPaint.Hex(p.Error)), ("edge", OuroPaint.Hex(edge)), ("behind", OuroPaint.Hex(p.Behind)),
            ("field", OuroPaint.Hex(p.Field)), ("pressedFill", OuroPaint.Hex(p.Checked)), ("pressedText", OuroPaint.Hex(p.Tick)),
            // The select never scales: pressed is a fill, so with animations on or off the scale is 1.
            ("pressScale", 1f), ("required", IsRequired), ("described", help.Length > 0), ("chosen", !string.IsNullOrEmpty(Value)),
            ("trimmed", (label?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false) || (error?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(label?.ActualHeight ?? 0)));
    }

    /// <summary>Puts keyboard focus on the select's button.</summary>
    public bool FocusInput(FocusState state) => box?.Focus(state) ?? false;

    private static void Show(TextBlock? block, string text, uint colour)
    {
        if (block is null) return;
        block.Text = text;
        block.Foreground = OuroUI.Brush(colour);
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
