// Core's Field (vendor/core/components/field.spec.json): one control with its label, description and error wired to it.
// The field adds no element UI Automation sees. The control it holds gets the label as its LabeledBy and, so a name is
// spoken whatever the control is, as its name (", required" when it is), its help text is the error then the
// description, and IsRequiredForForm says required. The words drawn around the control are out of the control view, so
// nothing is read twice. The field adds no tab stop; pressing the label focuses the control.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;

namespace Ouro.UI.Windows;

/// <summary>A form control with its label, description and error wired to it. For a control that has them of its own, such as <see cref="OuroTextField"/> or <see cref="OuroSelect"/>, use that control instead; use a field to name a control that has none, such as a plain TextBox.</summary>
public sealed partial class OuroField : ContentControl
{
    /// <summary>Core's metrics, in effective pixels: the gap between the parts, and the least height of a control.</summary>
    public const double Gap = 8, MinControlHeight = 44;

    private TextBlock? label, description, error;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ErrorProperty = Register(nameof(Error), typeof(string), "");
    public static readonly DependencyProperty IsRequiredProperty = Register(nameof(IsRequired), typeof(bool), false);
    public static readonly DependencyProperty HideLabelProperty = Register(nameof(HideLabel), typeof(bool), false);

    public OuroField()
    {
        DefaultStyleKey = typeof(OuroField);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(ContentProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the control is for: shown above it, and its spoken name.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Supporting words under the label, read after the name. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>What is wrong, shown under the control in the destructive colour and read before the description.</summary>
    public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    /// <summary>The control must be filled: the label gets a marker and the name says required.</summary>
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }

    /// <summary>Hides the label's words; the control keeps the label as its name.</summary>
    public bool HideLabel { get => (bool)GetValue(HideLabelProperty); set => SetValue(HideLabelProperty, value); }

    private static DependencyProperty Register(string name, System.Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroField), new PropertyMetadata(fallback, (d, _) => ((OuroField)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        label = GetTemplateChild("LabelText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        error = GetTemplateChild("ErrorText") as TextBlock;
        if (label is not null) label.Tapped += (_, _) => (Content as Control)?.Focus(FocusState.Pointer);
        Refresh();
    }

    private void Refresh()
    {
        if (label is null) return;
        var p = OuroUI.SurfacePaint(this);
        label.Inlines.Clear();
        label.Inlines.Add(new Run { Text = Label });
        // The marker is drawn, not spoken: the name says "required" itself.
        if (IsRequired) label.Inlines.Add(new Run { Text = " *", Foreground = OuroUI.Brush(p.Error) });
        label.Foreground = OuroUI.Brush(p.Text);
        label.Visibility = HideLabel || string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
        Show(description, Description, p.Muted);
        Show(error, Error, p.Error);
        Opacity = IsEnabled ? 1 : 0.5;

        if (Content is not FrameworkElement control) return;
        if (control is Control c && c.MinHeight < MinControlHeight) c.MinHeight = MinControlHeight;
        var help = OuroFormRules.Help(Error, Description);
        AutomationProperties.SetLabeledBy(control, label);
        AutomationProperties.SetName(control, OuroFormRules.Name(Label, IsRequired));
        AutomationProperties.SetHelpText(control, help);
        AutomationProperties.SetIsRequiredForForm(control, IsRequired);
        OuroUI.Report(control, "", "field", ("label", OuroPaint.Hex(p.Text)), ("description", OuroPaint.Hex(p.Muted)), ("error", OuroPaint.Hex(p.Error)),
            ("behind", OuroPaint.Hex(p.Behind)), ("required", IsRequired), ("described", help.Length > 0),
            ("trimmed", (label.IsTextTrimmed) || (description?.IsTextTrimmed ?? false) || (error?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(label.ActualHeight)));
    }

    private static void Show(TextBlock? block, string text, uint colour)
    {
        if (block is null) return;
        block.Text = text;
        block.Foreground = OuroUI.Brush(colour);
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
