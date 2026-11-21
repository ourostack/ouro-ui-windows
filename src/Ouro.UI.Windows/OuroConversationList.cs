// Core's ConversationList (vendor/core/components/conversation-list.spec.json): a long list of conversations that builds only the rows in view. It is a WinUI
// ListView, virtualised by its ItemsStackPanel, whose items are rows of data: a row's content is built when the ListView asks for its container. Each row is a
// ListItem with the SelectionItem pattern, the open one selected, named by its title, unread label, time and preview; each has a more button. The list is one tab
// stop that lands on the open row; Home and End go to the ends, Right reaches a row's more button and Left comes back. A heading row opens and closes its section.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Ouro.UI.Windows;

/// <summary>How much of a conversation shows: a preview line under the title, or the title and time alone.</summary>
public enum OuroConversationDensity
{
    /// <summary>A preview line under the title; 72 epx tall.</summary>
    Comfortable,

    /// <summary>The title and time only; 48 epx tall.</summary>
    Compact,
}

/// <summary>One line of a conversation list: a conversation, or a heading that starts a section the person can open and close.</summary>
public sealed class OuroConversationRow
{
    /// <summary>A conversation: an id the app keeps, its title and time, an optional preview, and whether it has new messages.</summary>
    public OuroConversationRow(string id, string title, string time = "", string preview = "", bool unread = false)
    {
        Id = id;
        Title = title;
        Time = time;
        Preview = preview;
        Unread = unread;
    }

    private OuroConversationRow(string id, string title) : this(id, title, "", "", false) => IsSection = true;

    /// <summary>A heading that starts a section: its header opens and closes the conversations after it, up to the next heading.</summary>
    public static OuroConversationRow Section(string id, string title) => new(id, title);

    public string Id { get; }

    public string Title { get; }

    public string Time { get; }

    public string Preview { get; }

    public bool Unread { get; }

    /// <summary>Whether this line is a section heading rather than a conversation.</summary>
    public bool IsSection { get; }
}

/// <summary>A section opened or closed: its id and whether it is now open.</summary>
public sealed class OuroSectionOpenEventArgs : EventArgs
{
    public OuroSectionOpenEventArgs(string id, bool open)
    {
        Id = id;
        Open = open;
    }

    public string Id { get; }

    public bool Open { get; }
}

/// <summary>A long list of conversations that builds only the rows in view, with the open one marked and an unread dot on the ones with new messages.</summary>
public sealed partial class OuroConversationList : Control
{
    /// <summary>Core's metrics, in effective pixels.</summary>
    public const double RowMinHeight = 44, RowHeightComfortable = 72, RowHeightCompact = 48, UnreadDot = 8, EmptyPadding = 24;

    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly HashSet<string> closed = [];
    private Grid? root;
    private IReadOnlyList<OuroConversationRow> rows = [];
    private List<OuroConversationRow> shown = [];
    private string label = "", selectedId = "", empty = "", moreLabel = "", unreadLabel = "", look = "";
    private OuroConversationDensity density;
    private bool quiet, pending;
    private int tries;

    public OuroConversationList()
    {
        DefaultStyleKey = typeof(OuroConversationList);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(message, AccessibilityView.Raw);
        list.ContainerContentChanging += (_, e) =>
        {
            if (e.InRecycleQueue || e.Item is not OuroConversationRow row || e.ItemContainer is not ListViewItem item) return;
            Fill(item, row);
            e.Handled = true;
            Later();
        };
        list.SelectionChanged += (_, _) => Chosen();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && FocusedRow() is { } row)
            {
                e.Handled = true;
                Open(row);
            }
            else if (e.Key == VirtualKey.Right && e.OriginalSource is ListViewItem item && FindMore(item) is { } more)
            {
                e.Handled = true;
                more.Focus(FocusState.Keyboard);
            }
        };
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>Raised when a conversation is chosen by a press, Enter or Space; carries its id.</summary>
    public event EventHandler<string>? Selected;

    /// <summary>Raised when a section's header opens or closes it.</summary>
    public event EventHandler<OuroSectionOpenEventArgs>? SectionOpenChanged;

    /// <summary>Raised when a conversation's more button is pressed; carries the conversation's id, for the app to show its menu.</summary>
    public event EventHandler<string>? MoreRequested;

    /// <summary>What the list is, such as "Conversations". It names the list.</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>The id of the open conversation; its row is marked current.</summary>
    public string SelectedId
    {
        get => selectedId;
        set
        {
            selectedId = value;
            Mark();
            Refresh();
        }
    }

    /// <summary>Comfortable shows a preview line under the title; compact shows the title and time only.</summary>
    public OuroConversationDensity Density
    {
        get => density;
        set { density = value; Rebuild(); }
    }

    /// <summary>What shows in place of the list when there are no conversations; "Nothing here yet" when empty.</summary>
    public string Empty
    {
        get => empty;
        set { empty = value; Refresh(); }
    }

    /// <summary>Names each row's more button: this, then the row's title. "More options for" when empty.</summary>
    public string MoreLabel
    {
        get => moreLabel;
        set { moreLabel = value; Rebuild(); }
    }

    /// <summary>The word read out for a row with new messages. "Unread" when empty.</summary>
    public string UnreadLabel
    {
        get => unreadLabel;
        set { unreadLabel = value; Rebuild(); }
    }

    /// <summary>The conversations and section headings, in order.</summary>
    public IReadOnlyList<OuroConversationRow> Rows
    {
        get => rows;
        set { rows = value; Rebuild(); }
    }

    internal bool ShowsEmpty => !rows.Any(r => !r.IsSection);

    internal string SpokenName => ShowsEmpty ? OuroListRules.EmptyText(empty) : label;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is null) return;
        root.Children.Clear();
        root.Children.Add(list);
        root.Children.Add(message);
        Rebuild();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroConversationListAutomationPeer(this);

    private OuroConversationRow? Current => shown.FirstOrDefault(r => !r.IsSection && r.Id == selectedId);

    private bool IsOpen(OuroConversationRow row) => !closed.Contains(row.Id);

    private OuroConversationRow? FocusedRow()
    {
        if (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject found) return null;
        for (DependencyObject? node = found; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem item) return list.ItemFromContainer(item) as OuroConversationRow;
        return null;
    }

    private void Mark()
    {
        quiet = true;
        list.SelectedItem = Current;
        quiet = false;
        foreach (var row in shown)
            if (list.ContainerFromItem(row) is ListViewItem item) Speak(item, row);
    }

    private void Chosen()
    {
        if (quiet) return;
        if (list.SelectedItem is not OuroConversationRow row) return;
        if (row.IsSection)
        {
            Mark();
            Toggle(row);
            return;
        }
        selectedId = row.Id;
        Mark();
        Refresh();
        Selected?.Invoke(this, row.Id);
    }

    private void Open(OuroConversationRow row)
    {
        if (row.IsSection) Toggle(row);
        else if (selectedId != row.Id)
        {
            selectedId = row.Id;
            Mark();
            Refresh();
            Selected?.Invoke(this, row.Id);
        }
    }

    private void Toggle(OuroConversationRow section)
    {
        var open = closed.Remove(section.Id);
        if (!open) closed.Add(section.Id);
        Rebuild();
        SectionOpenChanged?.Invoke(this, new OuroSectionOpenEventArgs(section.Id, open));
    }

    private void Speak(ListViewItem item, OuroConversationRow row) =>
        AutomationProperties.SetName(item, row.IsSection
            ? $"{row.Title}, {(IsOpen(row) ? "expanded" : "collapsed")}"
            : OuroListRules.ConversationName(row.Title, row.Unread, unreadLabel, row.Time, row.Preview, row.Id == selectedId));

    // A rebuilt list keeps focus: the row that had it takes it again, or when it left, the one that took its place.
    private void FocusRow(OuroConversationRow row)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            list.ScrollIntoView(row);
            (list.ContainerFromItem(row) as Control)?.Focus(FocusState.Keyboard);
        });
    }

    private void Rebuild()
    {
        if (root is null) return;
        var focused = FocusedRow();
        var at = focused is null ? -1 : shown.IndexOf(focused);
        var next = new List<OuroConversationRow>();
        var hidden = false;
        foreach (var row in rows)
        {
            if (row.IsSection)
            {
                hidden = closed.Contains(row.Id);
                next.Add(row);
            }
            else if (!hidden) next.Add(row);
        }
        shown = next;
        look = "";
        tries = 0;
        quiet = true;
        list.ItemsSource = null;
        list.ItemsSource = shown;
        list.SelectedItem = Current;
        quiet = false;
        Refresh();
        if (focused is null) return;
        if (shown.Contains(focused)) FocusRow(focused);
        else if (OuroListRules.FocusAfterRemoval(at, shown.Count) is var to and >= 0) FocusRow(shown[to]);
    }

    private static Button More(string name)
    {
        var button = new Button { Width = OuroConversationList.RowMinHeight, Height = OuroConversationList.RowMinHeight, Padding = new Thickness(0), BorderThickness = new Thickness(0), IsTabStop = false, VerticalAlignment = VerticalAlignment.Center, UseSystemFocusVisuals = true };
        var icon = new FontIcon { Glyph = "\uE712", FontSize = 20, FontFamily = new FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        button.Content = icon;
        AutomationProperties.SetName(button, name);
        return button;
    }

    private TextBlock Words(string text, uint ink, bool bold = false, bool wrap = true)
    {
        var words = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None, Foreground = OuroUI.Brush(ink) };
        if (bold) words.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        AutomationProperties.SetAccessibilityView(words, AccessibilityView.Raw);
        return words;
    }

    private void Fill(ListViewItem item, OuroConversationRow row)
    {
        var p = OuroUI.SurfacePaint(this);
        item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        item.Padding = new Thickness(12, 0, 4, 0);
        item.MinHeight = row.IsSection ? RowMinHeight : density == OuroConversationDensity.Compact ? RowHeightCompact : RowHeightComfortable;
        Speak(item, row);
        var grid = new Grid { MinHeight = item.MinHeight, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        if (row.IsSection)
        {
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var chevron = new FontIcon { Glyph = IsOpen(row) ? "\uE70D" : "\uE76C", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false, Foreground = OuroUI.Brush(p.Muted) };
            AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
            heading.Children.Add(chevron);
            heading.Children.Add(Words(row.Title, p.Muted, bold: true));
            grid.Children.Add(heading);
            item.Content = grid;
            return;
        }
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 8, 0, 8) };
        var top = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        if (row.Unread)
        {
            var dot = new Ellipse { Width = UnreadDot, Height = UnreadDot, Fill = OuroUI.Brush(OuroUI.Paint(this, OuroTone.Primary, OuroVariant.Solid).Fill), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);
            top.Children.Add(dot);
        }
        var title = Words(row.Title, p.Text, row.Unread);
        title.Tag = "title";
        Grid.SetColumn(title, 1);
        var time = Words(row.Time, p.Muted, wrap: false);
        Grid.SetColumn(time, 2);
        top.Children.Add(title);
        top.Children.Add(time);
        lines.Children.Add(top);
        if (density == OuroConversationDensity.Comfortable && row.Preview.Length > 0) lines.Children.Add(Words(row.Preview, p.Muted, wrap: false));
        grid.Children.Add(lines);
        var more = More(OuroListRules.MoreName(row.Title, moreLabel));
        more.Foreground = OuroUI.Brush(p.Muted);
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" }) more.Resources[key] = OuroUI.Brush(0);
        foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed" }) more.Resources[key] = OuroUI.Brush(p.Muted);
        more.Background = OuroUI.Brush(0);
        more.Click += (_, _) => MoreRequested?.Invoke(this, row.Id);
        more.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Left) return;
            e.Handled = true;
            item.Focus(FocusState.Keyboard);
        };
        Grid.SetColumn(more, 1);
        grid.Children.Add(more);
        item.Content = grid;
    }

    private static Button? FindMore(DependencyObject node)
    {
        if (node is Button button) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindMore(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private TextBlock? FirstTitle()
    {
        foreach (var row in shown)
            if (!row.IsSection && list.ContainerFromItem(row) is ListViewItem item && Find(item) is { } found) return found;
        return null;
    }

    private static TextBlock? Find(DependencyObject node)
    {
        if (node is TextBlock { Tag: "title" } title) return title;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        var c = OuroUI.Colors(this);
        var showsEmpty = ShowsEmpty;
        message.Text = OuroListRules.EmptyText(empty);
        message.Foreground = OuroUI.Brush(p.Muted);
        message.Padding = new Thickness(EmptyPadding);
        message.Visibility = showsEmpty ? Visibility.Visible : Visibility.Collapsed;
        list.Visibility = showsEmpty ? Visibility.Collapsed : Visibility.Visible;
        MinHeight = RowMinHeight;
        var accent = OuroUI.HighContrast ? p.Text : OuroUI.Argb(c.Accent);
        var wash = OuroNav.Wash(p.Behind, accent);
        list.Background = OuroUI.Brush(0);
        var states = new (string Key, uint Fill)[]
        {
            ("ListViewItemBackground", 0), ("ListViewItemBackgroundPointerOver", OuroPaint.Mix(p.Behind, p.Text, OuroPaint.HoverStep)), ("ListViewItemBackgroundPressed", OuroPaint.Mix(p.Behind, p.Text, OuroPaint.PressedStep)),
            ("ListViewItemBackgroundSelected", wash), ("ListViewItemBackgroundSelectedPointerOver", wash), ("ListViewItemBackgroundSelectedPressed", OuroPaint.Mix(wash, p.Text, OuroPaint.HoverStep)),
        };
        foreach (var (key, fill) in states) list.Resources[key] = OuroUI.Brush(fill);
        // Rows are drawn again only when what they are drawn with changed, so a size change never takes focus from a more button.
        var now = $"{p.Text}{p.Muted}{p.Behind}{density}{selectedId}{moreLabel}{unreadLabel}";
        if (now != look)
        {
            look = now;
            foreach (var row in shown)
                if (list.ContainerFromItem(row) is ListViewItem item && FocusedRow() is null) Fill(item, row);
        }
        var title = showsEmpty ? message : FirstTitle();
        var dot = OuroUI.Paint(this, OuroTone.Primary, OuroVariant.Solid).Fill;
        var conversations = rows.Where(r => !r.IsSection).ToList();
        OuroUI.Report(this, "", "conversation-list",
            ("label", label), ("title", OuroPaint.Hex(p.Text)), ("muted", OuroPaint.Hex(p.Muted)), ("dot", OuroPaint.Hex(dot)), ("behind", OuroPaint.Hex(p.Behind)),
            ("showsEmpty", showsEmpty), ("rows", conversations.Count), ("unread", conversations.Count(r => r.Unread)), ("selected", selectedId),
            ("rowNames", string.Join("|", conversations.Select(r => OuroListRules.ConversationName(r.Title, r.Unread, unreadLabel, r.Time, r.Preview, r.Id == selectedId)))),
            ("moreNames", string.Join("|", conversations.Select(r => OuroListRules.MoreName(r.Title, moreLabel)))),
            ("trimmed", title?.IsTextTrimmed ?? false), ("textHeight", Math.Round(Math.Max(0, (title?.ActualHeight ?? 0) - (showsEmpty ? 2 * EmptyPadding : 0)))));
        if (!showsEmpty && (title is null || title.ActualHeight == 0) && tries++ < 20) Later();
    }

    // The rows are built when the ListView lays out, after the list asked for them; the report is made again once they exist.
    private void Later()
    {
        if (pending) return;
        pending = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            pending = false;
            if (list.ActualHeight > 0 || ActualHeight > 0) Refresh();
        });
    }
}

/// <summary>A conversation list to UI Automation: a Group named by its label, not a tab stop, or its empty message as a Text when it has no rows.</summary>
public sealed partial class OuroConversationListAutomationPeer(OuroConversationList owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.ShowsEmpty ? AutomationControlType.Text : AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroConversationList);

    protected override string GetNameCore() => owner.SpokenName;

    protected override bool IsKeyboardFocusableCore() => false;
}
