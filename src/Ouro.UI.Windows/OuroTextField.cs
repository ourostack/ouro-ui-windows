// Core's TextField (vendor/core/components/text-field.spec.json): a label, a box to type in, a description and an error.
// UI Automation sees one Edit with the Value pattern, named by the label (", required" when it is), whose help text is
// the error then the description; the words drawn around the box are out of the control view, so nothing is read
// twice. The box is a TextBox (a PasswordBox for a password) with a bare template (Themes/Generic.xaml), so every
// colour is the kit's. Clear and Show password are buttons of their own, each its own tab stop.
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
    /// <summary>A secret: the text is masked, and a Show password button reveals it.</summary>
    Password,
}

/// <summary>How big a field is: medium is 44 epx tall, large is 56 with larger text, for a page's main search.</summary>
public enum OuroTextFieldSize
{
    /// <summary>44 epx tall.</summary>
    Medium,
    /// <summary>56 epx tall, with larger text.</summary>
    Large,
}

/// <summary>A labelled field for a line of text, such as an email address, or several lines.</summary>
public sealed partial class OuroTextField : Control
{
    /// <summary>Core's metrics, in effective pixels: the box's minimum height and its side padding.</summary>
    public const double MinBoxHeight = 44, PaddingX = 12, LargeBoxHeight = 56;

    private TextBlock? label, description, error, placeholder;
    private TextBox? input;
    private PasswordBox? secret;
    private Button? clear, reveal;
    private FontIcon? revealGlyph;
    private bool revealed;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(OuroTextField), new PropertyMetadata("", (d, _) => ((OuroTextField)d).TextSet()));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ErrorProperty = Register(nameof(Error), typeof(string), "");
    public static readonly DependencyProperty TypeProperty = Register(nameof(Type), typeof(OuroTextFieldType), OuroTextFieldType.Text);
    public static readonly DependencyProperty VariantProperty = Register(nameof(Variant), typeof(OuroFrame), OuroFrame.Outline);
    public static readonly DependencyProperty IsRequiredProperty = Register(nameof(IsRequired), typeof(bool), false);
    public static readonly DependencyProperty HideLabelProperty = Register(nameof(HideLabel), typeof(bool), false);
    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), typeof(OuroTextFieldSize), OuroTextFieldSize.Medium);
    public static readonly DependencyProperty IsClearableProperty = Register(nameof(IsClearable), typeof(bool), false);
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

    /// <summary>Medium is 44 epx tall; large is 56 with larger text.</summary>
    public OuroTextFieldSize Size { get => (OuroTextFieldSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Adds a Clear button while there is text.</summary>
    public bool IsClearable { get => (bool)GetValue(IsClearableProperty); set => SetValue(IsClearableProperty, value); }

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
        secret = GetTemplateChild("Secret") as PasswordBox;
        clear = GetTemplateChild("ClearButton") as Button;
        reveal = GetTemplateChild("RevealButton") as Button;
        revealGlyph = GetTemplateChild("RevealGlyph") as FontIcon;
        if (input is not null)
        {
            input.Text = Text;
            input.TextChanged += (_, _) => Text = input.Text;
            Watch(input);
        }
        if (secret is not null)
        {
            secret.Password = Text;
            secret.PasswordChanged += (_, _) => Text = secret.Password;
            Watch(secret);
        }
        if (clear is not null)
        {
            clear.Click += (_, _) =>
            {
                Text = "";
                Box?.Focus(FocusState.Programmatic);
            };
        }
        if (reveal is not null)
        {
            reveal.Click += (_, _) =>
            {
                revealed = !revealed;
                Refresh();
            };
        }
        Refresh();
    }

    private void Watch(Control box)
    {
        box.GotFocus += (_, _) => Refresh();
        box.LostFocus += (_, _) => Refresh();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && !IsMultiline) Submitted?.Invoke(this, Text);
        };
    }

    private bool IsPassword => Type == OuroTextFieldType.Password;

    /// <summary>The box the person types in: the password box for a password, the text box for every other type.</summary>
    private Control? Box => IsPassword ? secret : input;

    private void TextSet()
    {
        // The box and Text stay one: a person's keystroke sets Text to what the box holds already, and an app that
        // changes Text, even from its own TextChanged handler, puts its words in the box.
        if (input is not null && input.Text != Text) input.Text = Text;
        if (secret is not null && secret.Password != Text) secret.Password = Text;
        TextChanged?.Invoke(this, Text);
        Refresh();
    }

    private void Refresh()
    {
        if (Box is not { } box) return;
        var p = OuroUI.SurfacePaint(this);
        var focused = box.FocusState != FocusState.Unfocused;
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

        var large = Size == OuroTextFieldSize.Large;
        var height = IsMultiline ? MinBoxHeight * 2 : large ? LargeBoxHeight : MinBoxHeight;
        var showClear = IsClearable && !string.IsNullOrEmpty(Text) && IsEnabled;
        var showReveal = IsPassword;
        // The buttons sit inside the box's end, so the words stop short of them.
        var endRoom = (showClear ? height : 0) + (showReveal ? height : 0);
        if (input is not null) input.Visibility = IsPassword ? Visibility.Collapsed : Visibility.Visible;
        if (secret is not null) secret.Visibility = IsPassword ? Visibility.Visible : Visibility.Collapsed;
        if (secret is not null) secret.PasswordRevealMode = revealed ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        foreach (Control c in new Control?[] { input, secret }.OfType<Control>())
        {
            c.Background = OuroUI.Brush(fill);
            c.Foreground = OuroUI.Brush(p.Text);
            c.BorderBrush = OuroUI.Brush(edge);
            c.BorderThickness = new Thickness(thickness);
            c.FontSize = large ? 20 : 14;
            c.MinHeight = height;
            // The words don't move when the edge thickens on focus.
            c.Padding = new Thickness(PaddingX - thickness, IsMultiline ? 10 - thickness : 0, PaddingX - thickness + endRoom, IsMultiline ? 10 - thickness : 0);
            c.VerticalContentAlignment = IsMultiline ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            c.IsEnabled = IsEnabled;
            if (c is TextBox t)
            {
                t.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
                t.AcceptsReturn = IsMultiline;
                t.TextWrapping = IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
                t.SelectionHighlightColor = (SolidColorBrush)OuroUI.Brush(p.Checked);
                t.InputScope = Scope();
            }
            else if (c is PasswordBox pw)
            {
                pw.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
                pw.SelectionHighlightColor = (SolidColorBrush)OuroUI.Brush(p.Checked);
            }
        }
        Adorn(clear, showClear, height, "Clear", p.Muted);
        Adorn(reveal, showReveal && IsEnabled, height, "Show password", p.Muted);
        if (revealGlyph is not null) revealGlyph.Glyph = revealed ? "\uED1A" : "\uE7B3";
        Opacity = IsEnabled ? 1 : 0.5;

        if (placeholder is not null)
        {
            placeholder.Text = Placeholder;
            placeholder.Foreground = OuroUI.Brush(p.Placeholder);
            placeholder.Margin = new Thickness(PaddingX, IsMultiline ? 10 : 0, PaddingX + endRoom, IsMultiline ? 10 : 0);
            placeholder.FontSize = large ? 20 : 14;
            placeholder.VerticalAlignment = IsMultiline ? VerticalAlignment.Top : VerticalAlignment.Center;
            placeholder.Visibility = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Placeholder) ? Visibility.Visible : Visibility.Collapsed;
        }

        var help = string.Join(" ", new[] { Error, Description }.Where(s => !string.IsNullOrEmpty(s)));
        AutomationProperties.SetName(box, IsRequired ? $"{Label}, required" : Label);
        AutomationProperties.SetHelpText(box, help);
        AutomationProperties.SetIsRequiredForForm(box, IsRequired);
        if (AutomationProperties.GetAutomationId(this) is { Length: > 0 } id)
        {
            AutomationProperties.SetAutomationId(box, id);
            if (clear is not null) AutomationProperties.SetAutomationId(clear, $"{id}.clear");
            if (reveal is not null) AutomationProperties.SetAutomationId(reveal, $"{id}.reveal");
        }
        OuroUI.Report(box, "", "text-field", ("label", OuroPaint.Hex(p.Text)), ("text", OuroPaint.Hex(p.Text)), ("description", OuroPaint.Hex(p.Muted)),
            ("placeholder", OuroPaint.Hex(p.Placeholder)), ("error", OuroPaint.Hex(p.Error)), ("edge", OuroPaint.Hex(edge)), ("behind", OuroPaint.Hex(p.Behind)),
            ("field", OuroPaint.Hex(p.Field)), ("fill", OuroPaint.Hex(fill)), ("required", IsRequired), ("type", Type.ToString().ToLowerInvariant()), ("described", help.Length > 0),
            ("trimmed", (label?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false) || (error?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(label?.ActualHeight ?? 0)));
    }

    /// <summary>Puts keyboard focus in the field's box.</summary>
    public bool FocusInput(FocusState state) => Box?.Focus(state) ?? false;

    private InputScope Scope()
    {
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
        return scope;
    }

    /// <summary>A button at the box's end: a square as tall as the box, named by its words, in the muted colour.</summary>
    private static void Adorn(Button? button, bool visible, double size, string name, uint colour)
    {
        if (button is null) return;
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        button.Width = button.Height = size;
        if (button.Content is FontIcon icon) icon.Foreground = OuroUI.Brush(colour);
        AutomationProperties.SetName(button, name);
    }

    private static void Show(TextBlock? block, string text, uint colour)
    {
        if (block is null) return;
        block.Text = text;
        block.Foreground = OuroUI.Brush(colour);
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
