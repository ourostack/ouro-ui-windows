// The parts OuroComposedSurface draws for the composed tree's components that core has no component spec for yet (Stack's
// wrapping row, Image, List, Alert, Select) and its confirm sheet's rows: drawn with core's tokens through OuroUI, each
// with its UI Automation shape (an Image named by its description, a List of ListItems, an Alert as a Group named by its
// title, a ComboBox named by its label).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Ouro.UI.Windows;

/// <summary>A row that wraps: each child at its natural width, a row's spare width shared among its children.</summary>
internal sealed partial class OuroWrapPanel : Panel
{
    private readonly List<(UIElement Child, Rect Place)> places = [];

    public double Spacing { get; set; }

    protected override Size MeasureOverride(Size available)
    {
        places.Clear();
        var width = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
        var rows = new List<List<(UIElement Child, double Width)>>();
        var row = new List<(UIElement Child, double Width)>();
        double used = 0;
        foreach (var child in Children.Where(c => c.Visibility == Visibility.Visible))
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var w = Math.Min(child.DesiredSize.Width, width);
            if (row.Count > 0 && used + Spacing + w > width)
            {
                rows.Add(row);
                row = [];
                used = 0;
            }
            used += (row.Count > 0 ? Spacing : 0) + w;
            row.Add((child, w));
        }
        if (row.Count > 0) rows.Add(row);
        double y = 0, widest = 0;
        foreach (var r in rows)
        {
            var natural = r.Sum(c => c.Width) + Spacing * (r.Count - 1);
            var grow = width == double.MaxValue ? 0 : (width - natural) / r.Count;
            double x = 0, tallest = 0;
            foreach (var (child, w) in r)
            {
                var final = w + Math.Max(0, grow);
                child.Measure(new Size(final, double.PositiveInfinity));
                places.Add((child, new Rect(x, y, final, child.DesiredSize.Height)));
                tallest = Math.Max(tallest, child.DesiredSize.Height);
                x += final + Spacing;
            }
            widest = Math.Max(widest, x - Spacing);
            y += tallest + Spacing;
        }
        return new Size(widest, Math.Max(0, y - Spacing));
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var (child, place) in places) child.Arrange(place);
        foreach (var child in Children.Where(c => places.All(p => p.Child != c))) child.Arrange(new Rect(0, 0, 0, 0));
        return final;
    }
}

/// <summary>An Image: a picture filling a frame of its aspect, in the muted colour until it loads. Only https pictures load.</summary>
internal sealed partial class OuroComposedImage : Grid
{
    private readonly Border frame = new();
    private readonly double aspect;

    public OuroComposedImage(string url, string description, double aspect)
    {
        this.aspect = aspect;
        Description = description;
        var picture = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // An agent composed this tree: it may show a picture from the web, never a file from the person's machine.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) picture.Source = new BitmapImage(uri);
        AutomationProperties.SetAccessibilityView(picture, AccessibilityView.Raw);
        frame.Child = picture;
        Children.Add(frame);
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Height = e.NewSize.Width / this.aspect;
        };
    }

    public string Description { get; }

    public void Paint()
    {
        frame.Background = OuroUI.Brush(OuroUI.HighContrast ? OuroUI.System(UIElementType.Window) : OuroUI.Argb(OuroUI.Colors(this).Muted));
        frame.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposedImagePeer(this);
}

internal sealed partial class OuroComposedImagePeer(OuroComposedImage owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

    protected override string GetClassNameCore() => "OuroComposedImage";

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Description;
}

/// <summary>A List: short items, each a ListItem named by its words, after a bullet or its number.</summary>
internal sealed partial class OuroComposedList : StackPanel
{
    private readonly List<(TextBlock Marker, TextBlock Words)> rows = [];

    public OuroComposedList(IReadOnlyList<string> items, bool ordered)
    {
        Spacing = 6;
        for (var i = 0; i < items.Count; i++)
        {
            var marker = new TextBlock { Text = ordered ? $"{i + 1}." : "•", MinWidth = 20 };
            var words = new TextBlock { Text = items[i], TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAccessibilityView(marker, AccessibilityView.Raw);
            AutomationProperties.SetAccessibilityView(words, AccessibilityView.Raw);
            var row = new OuroComposedListItem(items[i]) { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(words, 1);
            row.Children.Add(marker);
            row.Children.Add(words);
            Children.Add(row);
            rows.Add((marker, words));
        }
    }

    public void Paint()
    {
        var p = OuroUI.SurfacePaint(this);
        foreach (var (marker, words) in rows)
        {
            marker.Foreground = OuroUI.Brush(p.Muted);
            words.Foreground = OuroUI.Brush(p.Text);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposedListPeer(this);
}

internal sealed partial class OuroComposedListPeer(OuroComposedList owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => "OuroComposedList";

    protected override bool IsKeyboardFocusableCore() => false;
}

/// <summary>One item of a List, named by its words.</summary>
internal sealed partial class OuroComposedListItem(string words) : Grid
{
    public string Words { get; } = words;

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposedListItemPeer(this);
}

internal sealed partial class OuroComposedListItemPeer(OuroComposedListItem owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => "OuroComposedListItem";

    protected override string GetNameCore() => owner.Words;
}

/// <summary>An Alert: a title and what to do about it, on the tone's soft fill. A Group named by its title, its words as help text.</summary>
internal sealed partial class OuroComposedAlert : Grid
{
    private readonly Border frame = new();
    private readonly TextBlock title, words;
    private readonly OuroTone tone;

    public OuroComposedAlert(string title, string text, OuroTone tone)
    {
        this.tone = tone;
        Title = title;
        this.title = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        words = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetAccessibilityView(this.title, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(words, AccessibilityView.Raw);
        AutomationProperties.SetHelpText(this, text);
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(this.title);
        stack.Children.Add(words);
        frame.Child = stack;
        frame.Padding = new Thickness(16, 12, 16, 12);
        Children.Add(frame);
    }

    public string Title { get; }

    public void Paint()
    {
        var paint = OuroUI.Paint(this, tone, OuroVariant.Soft);
        var surface = OuroUI.SurfacePaint(this);
        frame.Background = OuroUI.Brush(paint.Fill);
        frame.BorderBrush = OuroUI.Brush(paint.Edge);
        frame.BorderThickness = new Thickness(OuroUI.HighContrast ? 1 : 0);
        frame.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
        title.Foreground = OuroUI.Brush(paint.Label);
        words.Foreground = OuroUI.Brush(OuroUI.HighContrast ? paint.Label : OuroPaint.Readable(surface.Text, paint.Fill, paint.Label));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposedAlertPeer(this);
}

internal sealed partial class OuroComposedAlertPeer(OuroComposedAlert owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => "OuroComposedAlert";

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Title;
}

/// <summary>A Select: a label, WinUI's ComboBox named by it with the hint as help text, and the hint under it.</summary>
internal sealed partial class OuroComposedSelect : StackPanel
{
    // The ComboBox's own colours, by its theme resource keys, so it draws the kit's colours in its rest, hover, pressed and
    // focused states and in its list; each brush is the kit's, recoloured when the theme changes.
    private static readonly (string Key, Func<OuroSurfacePaint, uint> Colour)[] Brushes =
    [
        ("ComboBoxBackground", p => p.Field), ("ComboBoxBackgroundPointerOver", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.HoverStep)),
        ("ComboBoxBackgroundPressed", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.PressedStep)), ("ComboBoxBackgroundFocused", p => p.Field),
        ("ComboBoxBackgroundUnfocused", p => p.Field), ("ComboBoxBackgroundBorderBrushFocused", p => p.Focus), ("ComboBoxBackgroundBorderBrushUnfocused", p => p.Edge),
        ("ComboBoxBorderBrush", p => p.Edge), ("ComboBoxBorderBrushPointerOver", p => p.Edge), ("ComboBoxBorderBrushPressed", p => p.Edge),
        ("ComboBoxForeground", p => p.Text), ("ComboBoxForegroundPointerOver", p => p.Text), ("ComboBoxForegroundPressed", p => p.Text),
        ("ComboBoxForegroundFocused", p => p.Text), ("ComboBoxForegroundFocusedPressed", p => p.Text), ("ComboBoxPlaceHolderForeground", p => p.Placeholder),
        ("ComboBoxDropDownGlyphForeground", p => p.Muted), ("ComboBoxDropDownGlyphForegroundFocused", p => p.Muted), ("ComboBoxDropDownGlyphForegroundFocusedPressed", p => p.Muted),
        ("ComboBoxDropDownBackground", p => p.Field), ("ComboBoxDropDownBorderBrush", p => p.Edge), ("ComboBoxDropDownForeground", p => p.Text),
        ("ComboBoxItemForeground", p => p.Text), ("ComboBoxItemForegroundPointerOver", p => p.Text), ("ComboBoxItemForegroundPressed", p => p.Text),
        ("ComboBoxItemForegroundSelected", p => p.Text), ("ComboBoxItemForegroundSelectedPointerOver", p => p.Text), ("ComboBoxItemForegroundSelectedPressed", p => p.Text),
        ("ComboBoxItemForegroundSelectedUnfocused", p => p.Text), ("ComboBoxItemBackgroundPointerOver", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.HoverStep)),
        ("ComboBoxItemBackgroundPressed", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.PressedStep)), ("ComboBoxItemBackgroundSelected", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.HoverStep)),
        ("ComboBoxItemBackgroundSelectedPointerOver", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.PressedStep)), ("ComboBoxItemBackgroundSelectedPressed", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.PressedStep)),
        ("ComboBoxItemBackgroundSelectedUnfocused", p => OuroPaint.Mix(p.Field, p.Text, OuroPaint.HoverStep)), ("ComboBoxItemPillFillBrush", p => p.Checked),
    ];

    private readonly TextBlock label, hint;
    private readonly Dictionary<string, SolidColorBrush> brushes = [];

    public OuroComposedSelect(string label, string hint, IReadOnlyList<string> options, string chosen)
    {
        Spacing = 6;
        this.label = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        this.hint = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetAccessibilityView(this.label, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(this.hint, AccessibilityView.Raw);
        Box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = OuroTextField.MinBoxHeight };
        foreach (var (key, _) in Brushes)
        {
            var brush = new SolidColorBrush();
            brushes[key] = brush;
            Box.Resources[key] = brush;
        }
        foreach (var option in options) Box.Items.Add(option);
        Box.SelectedIndex = options.ToList().IndexOf(chosen);
        AutomationProperties.SetName(Box, label);
        AutomationProperties.SetHelpText(Box, hint);
        Box.SelectionChanged += (_, _) =>
        {
            if (Box.SelectedItem is string option) Chosen?.Invoke(this, option);
        };
        Children.Add(this.label);
        Children.Add(Box);
        Children.Add(this.hint);
    }

    /// <summary>The ComboBox the person chooses with.</summary>
    public ComboBox Box { get; }

    /// <summary>The person chose an option.</summary>
    public event EventHandler<string>? Chosen;

    public void Paint()
    {
        var p = OuroUI.SurfacePaint(this);
        label.Foreground = OuroUI.Brush(p.Text);
        hint.Foreground = OuroUI.Brush(p.Muted);
        foreach (var (key, colour) in Brushes) brushes[key].Color = ((SolidColorBrush)OuroUI.Brush(colour(p))).Color;
        Box.CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
    }
}

/// <summary>A row of the confirm sheet: a prepared value under its label, and the id small beside a name. Read as one Text.</summary>
internal sealed partial class OuroConfirmRow : StackPanel
{
    public OuroConfirmRow(string label, string value, string? raw)
    {
        Spoken = string.Join(", ", new[] { label, value, raw }.Where(s => !string.IsNullOrEmpty(s)));
        Spacing = 2;
        var heading = new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = value });
        if (raw is not null) line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"  {raw}", FontSize = 12 });
        AutomationProperties.SetAccessibilityView(heading, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(line, AccessibilityView.Raw);
        Children.Add(heading);
        Children.Add(line);
    }

    /// <summary>The row as a screen reader says it: the label, the value, then the id the value stands for.</summary>
    public string Spoken { get; }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroConfirmRowPeer(this);
}

internal sealed partial class OuroConfirmRowPeer(OuroConfirmRow owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => "OuroConfirmRow";

    protected override string GetNameCore() => owner.Spoken;
}
