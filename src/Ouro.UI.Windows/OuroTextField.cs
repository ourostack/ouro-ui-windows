// Core's TextField (vendor/core/components/text-field.spec.json): a label, a box to type in, a description and an error.
// UI Automation sees one Edit with the Value pattern, named by the label (", required" when it is), whose help text is
// the error then the description; the words drawn around the box are out of the control view, so nothing is read
// twice. The box is a TextBox with a bare template (Themes/Generic.xaml), so every colour is the kit's.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Ouro.UI.Windows;

/// <summary>What kind of text a field holds; each sets the touch keyboard. Windows adds Number, for the composed tree.</summary>
public enum OuroTextFieldType
{
    /// <summary>Words.</summary>
    Text,
    /// <summary>An email address.</summary>
    Email,
    /// <summary>A phone number.</summary>
    Tel,
    /// <summary>A web address.</summary>
    Url,
    /// <summary>A search.</summary>
    Search,
    /// <summary>A number.</summary>
    Number,
}

/// <summary>A labelled field for a line of text, such as an email address, or several lines.</summary>
public sealed partial class OuroTextField : Control
{
    /// <summary>Core's metrics, in effective pixels: the box's minimum height and its side padding.</summary>
    public const double MinBoxHeight = 44, PaddingX = 12;

    private TextBlock? label, description, error, placeholder;
    private TextBox? input;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(OuroTextField), new PropertyMetadata("", (d, _) => ((OuroTextField)d).TextSet()));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ErrorProperty = Register(nameof(Error), typeof(string), "");
    public static readonly DependencyProperty TypeProperty = Register(nameof(Type), typeof(OuroTextFieldType), OuroTextFieldType.Text);
    public static readonly DependencyProperty VariantProperty = Register(nameof(Variant), typeof(OuroFrame), OuroFrame.Outline);
    public static readonly DependencyProperty IsRequiredProperty = Register(nameof(IsRequired), typeof(bool), false);
    public static readonly DependencyProperty HideLabelProperty = Register(nameof(HideLabel), typeof(bool), false);
    public static readonly DependencyProperty IsMultilineProperty = Register(nameof(IsMultiline), typeof(bool), false);

    public OuroTextField()
    {
        DefaultStyleKey = typeof(OuroTextField);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the field is for: shown above it, and its spoken name. A placeholder is never the name.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The text in the field.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>An example of what to type, shown while the field is empty.</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <summary>Supporting words under the label, read after the name. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>What is wrong. The edge turns destructive and the words show under the box; read before the description.</summary>
    public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    /// <summary>What kind of text it holds, which sets the touch keyboard.</summary>
    public OuroTextFieldType Type { get => (OuroTextFieldType)GetValue(TypeProperty); set => SetValue(TypeProperty, value); }

    /// <summary>Outline draws the box; plain draws none, for a field in a frame the app draws.</summary>
    public OuroFrame Variant { get => (OuroFrame)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>The field must be filled: the label gets a marker and the name says required.</summary>
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }

    /// <summary>Hides the label's words; the field keeps the label as its name.</summary>
    public bool HideLabel { get => (bool)GetValue(HideLabelProperty); set => SetValue(HideLabelProperty, value); }

    /// <summary>Several lines, for a longer answer such as a note: Enter starts a new line.</summary>
    public bool IsMultiline { get => (bool)GetValue(IsMultilineProperty); set => SetValue(IsMultilineProperty, value); }

    /// <summary>The text changed, by the person or by the app.</summary>
    public event EventHandler<string>? TextChanged;

    /// <summary>Enter in a one-line field.</summary>
    public event EventHandler<string>? Submitted;

    private static DependencyProperty Register(string name, System.Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroTextField), new PropertyMetadata(fallback, (d, _) => ((OuroTextField)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        label = GetTemplateChild("LabelText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        error = GetTemplateChild("ErrorText") as TextBlock;
        placeholder = GetTemplateChild("PlaceholderText") as TextBlock;
        input = GetTemplateChild("Input") as TextBox;
        if (input is not null)
        {
            input.Text = Text;
            input.TextChanged += (_, _) => Text = input.Text;
            input.GotFocus += (_, _) => Refresh();
            input.LostFocus += (_, _) => Refresh();
            input.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Enter && !IsMultiline) Submitted?.Invoke(this, Text);
            };
        }
        Refresh();
    }

    private void TextSet()
    {
        // The box and Text stay one: a person's keystroke sets Text to what the box holds already, and an app that
        // changes Text, even from its own TextChanged handler, puts its words in the box.
        if (input is not null && input.Text != Text) input.Text = Text;
        TextChanged?.Invoke(this, Text);
        Refresh();
    }

    private void Refresh()
    {
        if (input is null) return;
        var p = OuroUI.SurfacePaint(this);
        var focused = input.FocusState != FocusState.Unfocused;
        var hasError = !string.IsNullOrEmpty(Error);
        var outline = Variant == OuroFrame.Outline;
        var edge = focused ? p.Focus : hasError ? p.Error : p.Edge;
        var fill = outline ? p.Field : 0u;
        var thickness = focused ? 2 : outline || hasError ? 1 : 0;

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

        input.Background = OuroUI.Brush(fill);
        input.Foreground = OuroUI.Brush(p.Text);
        input.BorderBrush = OuroUI.Brush(edge);
        input.BorderThickness = new Thickness(thickness);
        input.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
        // The words don't move when the edge thickens on focus.
        input.Padding = new Thickness(PaddingX - thickness, IsMultiline ? 10 - thickness : 0, PaddingX - thickness, IsMultiline ? 10 - thickness : 0);
        input.MinHeight = IsMultiline ? MinBoxHeight * 2 : MinBoxHeight;
        input.AcceptsReturn = IsMultiline;
        input.TextWrapping = IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        input.VerticalContentAlignment = IsMultiline ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        input.SelectionHighlightColor = (SolidColorBrush)OuroUI.Brush(p.Checked);
        input.IsEnabled = IsEnabled;
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName(Type switch
        {
            OuroTextFieldType.Email => InputScopeNameValue.EmailSmtpAddress,
            OuroTextFieldType.Tel => InputScopeNameValue.TelephoneNumber,
            OuroTextFieldType.Url => InputScopeNameValue.Url,
            OuroTextFieldType.Search => InputScopeNameValue.Search,
            OuroTextFieldType.Number => InputScopeNameValue.Number,
            _ => InputScopeNameValue.Default,
        }));
        input.InputScope = scope;
        Opacity = IsEnabled ? 1 : 0.5;

        if (placeholder is not null)
        {
            placeholder.Text = Placeholder;
            placeholder.Foreground = OuroUI.Brush(p.Placeholder);
            placeholder.Margin = new Thickness(PaddingX, IsMultiline ? 10 : 0, PaddingX, IsMultiline ? 10 : 0);
            placeholder.VerticalAlignment = IsMultiline ? VerticalAlignment.Top : VerticalAlignment.Center;
            placeholder.Visibility = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Placeholder) ? Visibility.Visible : Visibility.Collapsed;
        }

        var help = string.Join(" ", new[] { Error, Description }.Where(s => !string.IsNullOrEmpty(s)));
        AutomationProperties.SetName(input, IsRequired ? $"{Label}, required" : Label);
        AutomationProperties.SetHelpText(input, help);
        AutomationProperties.SetIsRequiredForForm(input, IsRequired);
        if (AutomationProperties.GetAutomationId(this) is { Length: > 0 } id) AutomationProperties.SetAutomationId(input, id);
        OuroUI.Report(input, "", "text-field", ("label", OuroPaint.Hex(p.Text)), ("text", OuroPaint.Hex(p.Text)), ("description", OuroPaint.Hex(p.Muted)),
            ("placeholder", OuroPaint.Hex(p.Placeholder)), ("error", OuroPaint.Hex(p.Error)), ("edge", OuroPaint.Hex(edge)), ("behind", OuroPaint.Hex(p.Behind)),
            ("field", OuroPaint.Hex(p.Field)), ("fill", OuroPaint.Hex(fill)), ("required", IsRequired), ("described", help.Length > 0),
            ("trimmed", (label?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false) || (error?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(label?.ActualHeight ?? 0)));
    }

    /// <summary>Puts keyboard focus in the field's box.</summary>
    public bool FocusInput(FocusState state) => input?.Focus(state) ?? false;

    private static void Show(TextBlock? block, string text, uint colour)
    {
        if (block is null) return;
        block.Text = text;
        block.Foreground = OuroUI.Brush(colour);
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
