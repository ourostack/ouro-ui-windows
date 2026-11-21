// Core's Table (vendor/core/components/table.spec.json): a caption, column headers and rows from text, sideways scroll inside its own
// region, sortable columns, rows that are one press, and striped, dense and sticky-header looks. UI Automation sees a Table named by its
// caption with the Grid and Table patterns, a Header row of HeaderItems (a sortable one also takes Invoke and carries its direction in
// its name), a DataItem per row of Text cells, and for a linked row one Button named by its row header. The table is not a tab stop;
// each sortable header and each linked row is one, in order. A sort is announced once through UI Automation notifications.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Ouro.UI.Windows;

/// <summary>A data grid: a caption, column headers and rows, with sorting, press rows and a scroll region of its own.</summary>
public sealed partial class OuroTable : Control
{
    /// <summary>Core's metrics, in effective pixels: the sideways padding of a cell, and the narrowest and widest a column grows.</summary>
    public const double CellPaddingX = 16, MinColumnWidth = 96, MaxColumnWidth = 320;

    private readonly TextBlock caption = new() { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly OuroTableBody body = new();
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Auto };
    private readonly List<TextBlock> texts = [];
    private StackPanel? root;
    private IReadOnlyList<string> columns = [], sortable = [];
    private IReadOnlyList<string[]> data = [];
    private string captionText = "", sortedBy = "", sortedLabel = "", emptyText = "", rowsText = "";
    private OuroSortDirection direction = OuroSortDirection.Ascending;
    private bool hideCaption, rowHref, striped, dense, sticky;
    private OuroTableCells? header;

    public OuroTable()
    {
        DefaultStyleKey = typeof(OuroTable);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(caption, AccessibilityView.Raw);
        scroll.Content = body;
        scroll.ViewChanged += (_, _) => Stick();
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the table is about, such as "Recipes". It is shown above the table, and it names the table whether or not it is shown.</summary>
    public string Caption
    {
        get => captionText;
        set { captionText = value; Refresh(); }
    }

    /// <summary>Whether the caption is kept off the screen and read only by a screen reader.</summary>
    public bool HideCaption
    {
        get => hideCaption;
        set { hideCaption = value; Refresh(); }
    }

    /// <summary>The column headers, separated by commas. The first column is the row header: it names each row.</summary>
    public string Columns
    {
        get => string.Join(", ", columns);
        set { columns = OuroTableRules.Words(value); Reparse(); Build(); }
    }

    /// <summary>The headers of the columns a press sorts, separated by commas.</summary>
    public string Sortable
    {
        get => string.Join(", ", sortable);
        set { sortable = OuroTableRules.Words(value); Build(); }
    }

    /// <summary>The header of the column the rows are ordered by. Setting it only marks the header: the app has ordered the rows.</summary>
    public string SortedBy
    {
        get => sortedBy;
        set { sortedBy = value; Build(); }
    }

    /// <summary>Which way the sorted column runs.</summary>
    public OuroSortDirection SortDirection
    {
        get => direction;
        set { direction = value; Build(); }
    }

    /// <summary>What a screen reader hears after a sort, instead of "Sorted by Serves, descending".</summary>
    public string SortedLabel
    {
        get => sortedLabel;
        set => sortedLabel = value;
    }

    /// <summary>The rows: rows apart by semicolons, cells by bars, such as "Herb salad | 10 min; Rye bread | 18 h".</summary>
    public string Rows
    {
        get => rowsText;
        set { rowsText = value; Reparse(); Build(); }
    }

    /// <summary>Whether each row is one press, and <see cref="RowPressed"/> says which. The row is named by its row header.</summary>
    public bool RowHref
    {
        get => rowHref;
        set { rowHref = value; Build(); }
    }

    /// <summary>Whether every other row is shaded.</summary>
    public bool Striped
    {
        get => striped;
        set { striped = value; Build(); }
    }

    /// <summary>Whether rows are 32 tall instead of 40.</summary>
    public bool Dense
    {
        get => dense;
        set { dense = value; Build(); }
    }

    /// <summary>Whether the header stays at the top while the rows scroll under it.</summary>
    public bool StickyHeader
    {
        get => sticky;
        set { sticky = value; Build(); }
    }

    /// <summary>What to show when there are no rows, such as "No recipes yet".</summary>
    public string Empty
    {
        get => emptyText;
        set { emptyText = value; Build(); }
    }

    /// <summary>A header was pressed: the column and the direction the table sorted its rows to.</summary>
    public event Action<string, OuroSortDirection>? Sorted;

    /// <summary>A linked row was pressed: its place among the rows as they now show.</summary>
    public event Action<int>? RowPressed;

    internal bool ShowsEmpty => data.Count == 0;

    internal int ColumnCount => columns.Count;

    internal IReadOnlyList<string[]> Data => data;

    internal OuroTableCells? HeaderRow => header;

    internal OuroTableBody Body => body;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as StackPanel;
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTableAutomationPeer(this);

    private void Reparse() => data = OuroTableRules.ParseRows(rowsText, columns.Count);

    private void Build()
    {
        if (root is null) return;
        texts.Clear();
        body.Children.Clear();
        var sortedColumn = OuroTableRules.ColumnOf(columns, sortedBy);
        header = new OuroTableCells { Kind = OuroTableCellsKind.Header };
        for (var c = 0; c < columns.Count; c++)
        {
            var column = c;
            if (sortable.Any(s => string.Equals(s, columns[c], StringComparison.OrdinalIgnoreCase)))
            {
                var button = new OuroTableHeaderButton { Words = columns[c], SortedHere = c == sortedColumn, Direction = direction };
                button.Click += (_, _) => Sort(column);
                texts.Add(button.Label);
                header.Children.Add(button);
            }
            else
            {
                var cell = new OuroTableHeaderCell { Words = columns[c] };
                texts.Add(cell.Label);
                header.Children.Add(cell);
            }
        }
        body.Children.Add(header);
        foreach (var (row, i) in data.Select((r, i) => (r, i)))
        {
            var cells = new OuroTableCells { Kind = rowHref ? OuroTableCellsKind.Inner : OuroTableCellsKind.Data, Index = i };
            foreach (var text in row)
                cells.Children.Add(new OuroTableCell { Words = text, Raw = rowHref });
            if (rowHref)
            {
                var link = new OuroTableLinkRow { Cells = cells, Index = i };
                link.Click += (_, _) => RowPressed?.Invoke(link.Index);
                body.Children.Add(link);
            }
            else body.Children.Add(cells);
        }
        root.Children.Clear();
        root.Children.Add(caption);
        root.Children.Add(scroll);
        root.Children.Add(message);
        Refresh();
    }

    private void Sort(int column)
    {
        var was = OuroTableRules.ColumnOf(columns, sortedBy) == column;
        var next = OuroTableRules.NextDirection(was, direction);
        sortedBy = columns[column];
        direction = next;
        data = OuroTableRules.Sort(data, column, next);
        Build();
        var words = OuroTableRules.SortedAnnouncement(columns[column], next, sortedLabel);
        FrameworkElementAutomationPeer.CreatePeerForElement(this)?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, words, "ouro-table-sort");
        UpdateLayout();
        var focus = header?.Children.OfType<OuroTableHeaderButton>().FirstOrDefault(h => h.Words == columns[column]);
        focus?.Focus(FocusState.Keyboard);
        Sorted?.Invoke(columns[column], next);
    }

    private void Stick()
    {
        if (header is null) return;
        header.RenderTransform = sticky ? new TranslateTransform { Y = scroll.VerticalOffset } : null;
    }

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        caption.Text = captionText;
        caption.Foreground = OuroUI.Brush(p.Text);
        caption.Margin = new Thickness(0, 0, 0, 8);
        caption.Visibility = captionText.Length > 0 && !hideCaption ? Visibility.Visible : Visibility.Collapsed;
        message.Text = OuroTableRules.EmptyText(emptyText);
        message.Foreground = OuroUI.Brush(p.Muted);
        message.Padding = new Thickness(24);
        message.Visibility = ShowsEmpty ? Visibility.Visible : Visibility.Collapsed;
        scroll.Visibility = ShowsEmpty ? Visibility.Collapsed : Visibility.Visible;
        scroll.MaxHeight = sticky ? 320 : double.PositiveInfinity;
        body.Dense = dense;
        body.Striped = striped;
        body.Pressable = rowHref;
        var stripe = OuroPaint.Mix(p.Behind, p.Text, 0.05);
        var hover = OuroPaint.Mix(p.Behind, p.Text, 0.08);
        var press = OuroPaint.Mix(p.Behind, p.Text, 0.12);
        var index = 0;
        foreach (var child in body.Children)
        {
            if (child is OuroTableCells { Kind: OuroTableCellsKind.Header } h)
            {
                h.Background = OuroUI.Brush(p.Behind);
                Canvas.SetZIndex(h, 1);
                h.RowHeight = OuroTableRules.RowHeight(dense, h.Children.OfType<OuroTableHeaderButton>().Any());
                h.Paint(p.Muted, p.Text, p.Line);
                continue;
            }
            var shade = OuroTableRules.Shaded(striped, index++) ? stripe : 0u;
            if (child is OuroTableLinkRow link)
            {
                link.Rest = shade;
                link.Hover = hover;
                link.Press = press;
                link.Line = p.Line;
                link.Cells!.RowHeight = OuroTableRules.RowHeight(dense, true);
                link.Cells.Paint(p.Text, p.Text, p.Line);
                link.Paint();
            }
            else if (child is OuroTableCells cells)
            {
                cells.Background = shade == 0 ? null : OuroUI.Brush(shade);
                cells.RowHeight = OuroTableRules.RowHeight(dense, false);
                cells.Paint(p.Text, p.Text, p.Line);
            }
        }
        Stick();
        var first = ShowsEmpty ? message : texts.FirstOrDefault();
        var inset = ShowsEmpty ? message.Padding.Top + message.Padding.Bottom : 0;
        var sortedAt = OuroTableRules.ColumnOf(columns, sortedBy);
        OuroUI.Report(this, "", "table",
            ("label", OuroPaint.Hex(p.Text)), ("header", OuroPaint.Hex(p.Muted)), ("empty", OuroPaint.Hex(p.Muted)), ("line", OuroPaint.Hex(p.Line)), ("behind", OuroPaint.Hex(p.Behind)),
            ("stripe", OuroPaint.Hex(stripe)), ("sorted", sortedAt >= 0), ("sortedName", sortedAt < 0 ? "" : OuroTableRules.HeaderName(columns[sortedAt], true, direction)), ("rows", data.Count), ("columns", columns.Count),
            ("linked", rowHref), ("showsEmpty", ShowsEmpty), ("emptyText", OuroTableRules.EmptyText(emptyText)),
            ("trimmed", first?.IsTextTrimmed ?? false), ("textHeight", Math.Round((first?.ActualHeight ?? 0) - inset)));
    }
}

/// <summary>What a row of cells is to UI Automation.</summary>
internal enum OuroTableCellsKind
{
    /// <summary>The header row.</summary>
    Header,
    /// <summary>A body row of Text cells.</summary>
    Data,
    /// <summary>The cells inside a linked row, which the row speaks for.</summary>
    Inner,
}

/// <summary>One row's cells, laid out on the table's shared column widths.</summary>
internal sealed partial class OuroTableCells : Panel
{
    public OuroTableCellsKind Kind { get; set; }

    public int Index { get; set; }

    public double RowHeight { get; set; } = 40;

    public double[] Widths { get; set; } = [];

    internal double[] Wanted { get; private set; } = [];

    public string SpokenName => OuroTableRules.RowName(Children.OfType<OuroTableCell>().Select(c => c.Words));

    internal void Paint(uint words, uint strong, uint line)
    {
        foreach (var child in Children)
            switch (child)
            {
                case OuroTableHeaderButton h: h.Paint(words, strong, line); break;
                case OuroTableHeaderCell c: c.Paint(words, line); break;
                case OuroTableCell c: c.Paint(words, line); break;
            }
        InvalidateMeasure();
    }

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size available)
    {
        var wanted = new double[Children.Count];
        var tall = RowHeight;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new global::Windows.Foundation.Size(OuroTable.MaxColumnWidth, double.PositiveInfinity));
            wanted[i] = Math.Max(OuroTable.MinColumnWidth, Math.Min(OuroTable.MaxColumnWidth, Children[i].DesiredSize.Width));
        }
        Wanted = wanted;
        var widths = Widths.Length == wanted.Length ? Widths : wanted;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new global::Windows.Foundation.Size(widths[i], double.PositiveInfinity));
            tall = Math.Max(tall, Children[i].DesiredSize.Height);
        }
        return new global::Windows.Foundation.Size(widths.Sum(), tall);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size final)
    {
        var widths = Widths.Length == Children.Count ? Widths : Wanted;
        var x = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new global::Windows.Foundation.Rect(x, 0, widths[i], final.Height));
            x += widths[i];
        }
        return final;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTableCellsAutomationPeer(this);
}

/// <summary>A row of cells to UI Automation: the Header row, or a DataItem named by its cells.</summary>
internal sealed partial class OuroTableCellsAutomationPeer(OuroTableCells owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.Kind == OuroTableCellsKind.Header ? AutomationControlType.Header : AutomationControlType.DataItem;

    protected override string GetClassNameCore() => "OuroTableRow";

    protected override string GetNameCore() => owner.Kind == OuroTableCellsKind.Header ? "" : owner.SpokenName;

    protected override bool IsKeyboardFocusableCore() => false;
}

/// <summary>The table's rows, stacked, each on the widest any row needs for each column.</summary>
internal sealed partial class OuroTableBody : Panel
{
    public bool Dense { get; set; }

    public bool Striped { get; set; }

    public bool Pressable { get; set; }

    private IEnumerable<OuroTableCells> Lines => Children.Select(c => c switch { OuroTableLinkRow l => l.Cells, OuroTableCells r => r, _ => null }).OfType<OuroTableCells>();

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size available)
    {
        var lines = Lines.ToList();
        foreach (var line in lines) line.Widths = [];
        foreach (var child in Children) child.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var count = lines.Select(l => l.Wanted.Length).DefaultIfEmpty(0).Max();
        var widths = Enumerable.Range(0, count).Select(i => lines.Where(l => i < l.Wanted.Length).Select(l => l.Wanted[i]).DefaultIfEmpty(OuroTable.MinColumnWidth).Max()).ToArray();
        foreach (var line in lines) line.Widths = widths;
        double width = 0, height = 0;
        var total = widths.Sum();
        foreach (var child in Children)
        {
            child.InvalidateMeasure();
            child.Measure(new global::Windows.Foundation.Size(total + 1, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new global::Windows.Foundation.Size(width, height);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size final)
    {
        var y = 0.0;
        var width = Math.Max(final.Width, Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty(0).Max());
        foreach (var child in Children)
        {
            child.Arrange(new global::Windows.Foundation.Rect(0, y, width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        return new global::Windows.Foundation.Size(width, y);
    }
}

/// <summary>A body cell: its words, a Text to UI Automation, or hidden from it inside a linked row.</summary>
internal sealed partial class OuroTableCell : Border
{
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private string words = "";

    public OuroTableCell()
    {
        Child = text;
        Padding = new Thickness(OuroTable.CellPaddingX, 8, OuroTable.CellPaddingX, 8);
    }

    public string Words
    {
        get => words;
        set { words = value; text.Text = value; }
    }

    public bool Raw
    {
        set => AutomationProperties.SetAccessibilityView(text, value ? AccessibilityView.Raw : AccessibilityView.Content);
    }

    internal void Paint(uint color, uint line)
    {
        text.Foreground = OuroUI.Brush(color);
        BorderBrush = line == 0 ? null : OuroUI.Brush(line);
        BorderThickness = new Thickness(0, 0, 0, line == 0 ? 0 : 1);
    }
}

/// <summary>A header that no press sorts: its words, a HeaderItem to UI Automation.</summary>
internal sealed partial class OuroTableHeaderCell : Border
{
    internal TextBlock Label { get; } = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };

    public OuroTableHeaderCell()
    {
        Child = Label;
        Padding = new Thickness(OuroTable.CellPaddingX, 8, OuroTable.CellPaddingX, 8);
        AutomationProperties.SetAccessibilityView(Label, AccessibilityView.Raw);
    }

    public string Words
    {
        get => Label.Text;
        set => Label.Text = value;
    }

    internal void Paint(uint color, uint line)
    {
        Label.Foreground = OuroUI.Brush(color);
        BorderBrush = OuroUI.Brush(line);
        BorderThickness = new Thickness(0, 0, 0, 1);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTableHeaderCellAutomationPeer(this);
}

internal sealed partial class OuroTableHeaderCellAutomationPeer(OuroTableHeaderCell owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;

    protected override string GetClassNameCore() => "OuroTableHeader";

    protected override string GetNameCore() => owner.Words;

    protected override bool IsKeyboardFocusableCore() => false;
}

/// <summary>A header a press sorts by, at least 44 tall: one tab stop, with an arrow on the sorted one.</summary>
internal sealed partial class OuroTableHeaderButton : Button
{
    internal TextBlock Label { get; } = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly TextBlock arrow = new() { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), IsTextScaleFactorEnabled = false };

    public OuroTableHeaderButton()
    {
        DefaultStyleKey = typeof(OuroTableHeaderButton);
        AutomationProperties.SetAccessibilityView(Label, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(arrow, AccessibilityView.Raw);
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label, arrow } };
        MinHeight = 44;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(OuroTable.CellPaddingX, 8, OuroTable.CellPaddingX, 8);
    }

    public string Words
    {
        get => Label.Text;
        set => Label.Text = value;
    }

    public bool SortedHere { get; set; }

    public OuroSortDirection Direction { get; set; }

    public string SpokenName => OuroTableRules.HeaderName(Words, SortedHere, Direction);

    internal void Paint(uint muted, uint strong, uint line)
    {
        BorderBrush = OuroUI.Brush(line);
        BorderThickness = new Thickness(0, 0, 0, 1);
        var color = SortedHere ? strong : muted;
        Label.Foreground = OuroUI.Brush(color);
        arrow.Foreground = OuroUI.Brush(color);
        arrow.Text = !SortedHere ? "" : Direction == OuroSortDirection.Descending ? "\uE70D" : "\uE70E";
        Background = null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTableHeaderButtonAutomationPeer(this);
}

internal sealed partial class OuroTableHeaderButtonAutomationPeer(OuroTableHeaderButton owner) : ButtonAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;

    protected override string GetClassNameCore() => "OuroTableHeaderButton";

    protected override string GetNameCore() => owner.SpokenName;
}

/// <summary>A row that is one press: the cells inside it, a Button named by the row header, shaded while pointed at or pressed.</summary>
internal sealed partial class OuroTableLinkRow : Button
{
    private bool inside;

    public OuroTableLinkRow()
    {
        DefaultStyleKey = typeof(OuroTableLinkRow);
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        PointerEntered += (_, _) => { inside = true; Paint(); };
        PointerExited += (_, _) => { inside = false; Paint(); };
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Paint());
    }

    public OuroTableCells? Cells
    {
        get => Content as OuroTableCells;
        set => Content = value;
    }

    public int Index { get; set; }

    public uint Rest { get; set; }

    public uint Hover { get; set; }

    public uint Press { get; set; }

    public uint Line { get; set; }

    internal string RowHeader => (Cells?.Children.FirstOrDefault() as OuroTableCell)?.Words ?? "";

    internal void Paint()
    {
        var fill = IsPressed ? Press : inside ? Hover : Rest;
        Background = fill == 0 ? null : OuroUI.Brush(fill);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTableLinkRowAutomationPeer(this);
}

internal sealed partial class OuroTableLinkRowAutomationPeer(OuroTableLinkRow owner) : ButtonAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetClassNameCore() => "OuroTableLinkRow";

    protected override string GetNameCore() => owner.RowHeader;
}

/// <summary>A table to UI Automation: a Table named by its caption, with its columns and rows and the header cells.</summary>
public sealed partial class OuroTableAutomationPeer(OuroTable owner) : FrameworkElementAutomationPeer(owner), IGridProvider, ITableProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Table;

    protected override string GetClassNameCore() => nameof(OuroTable);

    protected override string GetNameCore() => owner.Caption;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.Grid or PatternInterface.Table ? this : base.GetPatternCore(patternInterface);

    public int ColumnCount => owner.ColumnCount;

    public int RowCount => owner.Data.Count;

    public IRawElementProviderSimple GetItem(int row, int column)
    {
        var line = owner.Body.Children.Skip(1).ElementAtOrDefault(row);
        var cells = line switch { OuroTableLinkRow l => l.Cells, OuroTableCells c => c, _ => null };
        var cell = cells?.Children.ElementAtOrDefault(column);
        return cell is null ? null! : ProviderFromPeer(CreatePeerForElement(cell));
    }

    public RowOrColumnMajor RowOrColumnMajor => RowOrColumnMajor.RowMajor;

    public IRawElementProviderSimple[] GetColumnHeaders() =>
        owner.HeaderRow?.Children.Select(c => ProviderFromPeer(CreatePeerForElement(c))).ToArray() ?? [];

    public IRawElementProviderSimple[] GetRowHeaders() =>
        owner.Body.Children.Skip(1).Select(l => (l switch { OuroTableLinkRow r => r.Cells, OuroTableCells c => c, _ => null })?.Children.FirstOrDefault()).OfType<UIElement>().Select(c => ProviderFromPeer(CreatePeerForElement(c))).ToArray();
}
