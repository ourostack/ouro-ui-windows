// Core's DescriptionList (vendor/core/components/description-list.spec.json): one thing's details as pairs of a term and
// its details, such as Prep time and 20 minutes. Below its stack width, or at large text sizes, each term sits above its
// details; above it the terms form a column beside them. UI Automation sees a List of ListItems, each named by its term
// then its details.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Ouro.UI.Windows;

/// <summary>One pair in a description list: a term and its details, in words.</summary>
public sealed record OuroDescriptionItem(string Term, string Details);

/// <summary>Below which width of the list itself each term sits above its details.</summary>
public enum OuroStackBelow
{
    /// <summary>448 epx, the default.</summary>
    Small,
    /// <summary>512 epx.</summary>
    Medium,
}

/// <summary>One thing's details as pairs of a term and its value, such as a recipe's prep time and servings.</summary>
public sealed partial class OuroDescriptionList : Control
{
    /// <summary>Core's metrics, in effective pixels.</summary>
    public const double StackWidthSmall = 448, StackWidthMedium = 512, TermColumnMax = 224, TermColumnFraction = 0.4, PairPadding = 12;

    private StackPanel? rows;
    private IReadOnlyList<OuroDescriptionItem> items = [];

    public static readonly DependencyProperty StackBelowProperty = DependencyProperty.Register(nameof(StackBelow), typeof(OuroStackBelow), typeof(OuroDescriptionList),
        new PropertyMetadata(OuroStackBelow.Small, (d, _) => ((OuroDescriptionList)d).Refresh()));

    public OuroDescriptionList()
    {
        DefaultStyleKey = typeof(OuroDescriptionList);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>The pairs, in order.</summary>
    public IReadOnlyList<OuroDescriptionItem> Items
    {
        get => items;
        set
        {
            items = value;
            Build();
        }
    }

    /// <summary>Below this width of the list itself each term sits above its details.</summary>
    public OuroStackBelow StackBelow { get => (OuroStackBelow)GetValue(StackBelowProperty); set => SetValue(StackBelowProperty, value); }

    /// <summary>Whether each term sits above its details now: below the stack width, or at large text sizes.</summary>
    public bool IsStacked => OuroUI.TextScale >= OuroUI.StackAtTextScale || ActualWidth < (StackBelow == OuroStackBelow.Medium ? StackWidthMedium : StackWidthSmall);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        rows = GetTemplateChild("Rows") as StackPanel;
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroDescriptionListAutomationPeer(this);

    private void Build()
    {
        if (rows is null) return;
        rows.Children.Clear();
        foreach (var item in items) rows.Children.Add(new OuroDescriptionRow(item));
        Refresh();
    }

    private void Refresh()
    {
        if (rows is null) return;
        var p = OuroUI.SurfacePaint(this);
        var stacked = IsStacked;
        var term = Math.Min(TermColumnMax, ActualWidth * TermColumnFraction);
        var first = true;
        foreach (var row in rows.Children.OfType<OuroDescriptionRow>())
        {
            row.Lay(stacked, term, first, p);
            first = false;
        }
        var firstRow = rows.Children.OfType<OuroDescriptionRow>().FirstOrDefault();
        OuroUI.Report(this, "", "description-list", ("term", OuroPaint.Hex(p.Muted)), ("details", OuroPaint.Hex(p.Text)), ("line", OuroPaint.Hex(p.Line)), ("behind", OuroPaint.Hex(p.Behind)),
            ("items", items.Count), ("stacked", stacked), ("trimmed", rows.Children.OfType<OuroDescriptionRow>().Any(r => r.Trimmed)), ("textHeight", Math.Round(firstRow?.TermHeight ?? 0)));
    }
}

/// <summary>One pair: its term and details side by side or stacked, with a line above every pair but the first.</summary>
internal sealed partial class OuroDescriptionRow : Grid
{
    private readonly TextBlock term, details;

    public OuroDescriptionRow(OuroDescriptionItem item)
    {
        Item = item;
        term = new TextBlock { Text = item.Term, TextWrapping = TextWrapping.Wrap };
        details = new TextBlock { Text = item.Details, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAccessibilityView(term, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(details, AccessibilityView.Raw);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(term);
        Children.Add(details);
    }

    public OuroDescriptionItem Item { get; }

    public bool Trimmed => term.IsTextTrimmed || details.IsTextTrimmed;

    public double TermHeight => term.ActualHeight;

    public void Lay(bool stacked, double termWidth, bool first, OuroSurfacePaint p)
    {
        term.Foreground = OuroUI.Brush(p.Muted);
        details.Foreground = OuroUI.Brush(p.Text);
        BorderBrush = OuroUI.Brush(p.Line);
        BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0);
        Padding = new Thickness(0, OuroDescriptionList.PairPadding, 0, OuroDescriptionList.PairPadding);
        ColumnDefinitions[0].Width = stacked ? GridLength.Auto : new GridLength(Math.Max(0, termWidth));
        ColumnSpacing = stacked ? 0 : 16;
        RowSpacing = stacked ? 4 : 0;
        SetColumnSpan(term, stacked ? 2 : 1);
        SetColumn(details, stacked ? 0 : 1);
        SetColumnSpan(details, stacked ? 2 : 1);
        SetRow(details, stacked ? 1 : 0);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroDescriptionRowAutomationPeer(this);
}

/// <summary>A pair to UI Automation: a ListItem named by its term, then its details.</summary>
internal sealed partial class OuroDescriptionRowAutomationPeer(OuroDescriptionRow owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => "OuroDescriptionItem";

    protected override string GetNameCore() => $"{owner.Item.Term}, {owner.Item.Details}";
}

/// <summary>A description list to UI Automation: a List of its pairs.</summary>
public sealed partial class OuroDescriptionListAutomationPeer(OuroDescriptionList owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(OuroDescriptionList);

    protected override bool IsKeyboardFocusableCore() => false;
}
