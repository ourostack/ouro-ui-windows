// Core's Reorderable (vendor/core/components/reorderable.spec.json): a list the person puts in order. It is a WinUI ListView with CanReorderItems and
// CanDragItems, so a drag works; each row's handle is a Button that opens a MenuFlyout of moves, so dragging is never the only way; and a handle takes Space to
// pick the row up, the arrows to move it, Space to drop it and Escape to put it back. Every move is announced through UI Automation notifications with its
// place ("Position 2 of 5"), and focus stays on the moved row's handle.
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ouro.UI.Hx.Tokens;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Ouro.UI.Windows;

/// <summary>One row of a reorderable list: an id the app keeps, the label the handle and the announcements use, and what the row shows.</summary>
public sealed class OuroReorderableRow
{
    public OuroReorderableRow(string id, string label, UIElement? content = null)
    {
        Id = id;
        Label = label;
        Content = content;
    }

    /// <summary>The row's id, which the app keeps across moves.</summary>
    public string Id { get; }

    /// <summary>The row's words: the handle is "Reorder {label}", and a move says it.</summary>
    public string Label { get; }

    /// <summary>What the row shows; with none, its label.</summary>
    public UIElement? Content { get; }
}

/// <summary>A row moved: the new order of ids, and the move itself.</summary>
public sealed class OuroReorderedEventArgs : EventArgs
{
    public OuroReorderedEventArgs(IReadOnlyList<string> order, string id, int from, int to)
    {
        Order = order;
        Id = id;
        From = from;
        To = to;
    }

    /// <summary>The ids in their new order.</summary>
    public IReadOnlyList<string> Order { get; }

    /// <summary>The row that moved.</summary>
    public string Id { get; }

    /// <summary>Where it was, counting from 0.</summary>
    public int From { get; }

    /// <summary>Where it is, counting from 0.</summary>
    public int To { get; }
}

/// <summary>A list the person puts in order: a drag handle on each row and a menu of moves, each move announced.</summary>
public sealed partial class OuroReorderable : Control
{
    /// <summary>Core's metrics, in effective pixels: the handle's target and its icon.</summary>
    public const double HandleTarget = 44, HandleIcon = 20;

    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false, AllowDrop = true, CanReorderItems = true, CanDragItems = true };
    private readonly Dictionary<string, (Grid Row, OuroReorderHandle Handle, OuroReorderableRow Source)> parts = new();
    private Grid? root;
    private string label = "", reason = "";
    private bool disabled;
    private IReadOnlyList<OuroReorderableRow> rows = [];
    private string? picked;
    private int pickedFrom;
    private int dragFrom = -1;
    private bool moving, space;

    public OuroReorderable()
    {
        DefaultStyleKey = typeof(OuroReorderable);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Hidden);
        list.ContainerContentChanging += (_, e) =>
        {
            e.ItemContainer.IsTabStop = false;
            e.ItemContainer.Padding = new Thickness(0);
            e.ItemContainer.MinHeight = 0;
            e.ItemContainer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        };
        list.DragItemsStarting += (_, e) => dragFrom = e.Items.Count == 0 ? -1 : list.Items.IndexOf(e.Items[0]);
        list.DragItemsCompleted += (_, e) => Dropped(e.Items.Count == 0 ? null : e.Items[0] as Grid);
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>Raised when a row moves, by a drag, the menu or the keyboard: once per move, and for a pick-up once when the row is dropped.</summary>
    public event EventHandler<OuroReorderedEventArgs>? Reordered;

    /// <summary>What the list is, such as "Playlist". It names the list and the move announcements.</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>Whether the order is locked: handles stay in the tab order but do nothing, and are marked unavailable.</summary>
    public bool Disabled
    {
        get => disabled;
        set { disabled = value; Refresh(); }
    }

    /// <summary>Why the order is locked, such as "Sorted by date"; read out as part of each handle's name. Setting it alone locks the order.</summary>
    public string DisabledReason
    {
        get => reason;
        set { reason = value; Refresh(); }
    }

    /// <summary>The rows, in order.</summary>
    public IReadOnlyList<OuroReorderableRow> Rows
    {
        get => rows;
        set { rows = value; Build(); }
    }

    internal bool Locked => OuroListRules.Locked(disabled, reason);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is null) return;
        root.Children.Clear();
        root.Children.Add(list);
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroReorderableAutomationPeer(this);

    private IReadOnlyList<string> Order => list.Items.OfType<Grid>().Select(g => (string)g.Tag).ToList();

    private static FrameworkElement? FirstText(DependencyObject? node)
    {
        if (node is TextBlock text) return text;
        if (node is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FirstText(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private void Announce(string words)
    {
        if (FrameworkElementAutomationPeer.CreatePeerForElement(this) is { } peer)
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, words, "ouro-reorder");
    }

    private string Moved(string id) => $"{label}: {parts[id].Source.Label}. {OuroListRules.Position(list.Items.IndexOf(parts[id].Row), list.Items.Count)}";

    // A move takes the row out of the list and puts it back, which drops focus from its handle; the handle takes it again once the row is laid out, and a pick-up
    // is not ended by the focus that leaves in between.
    private void FocusHandle(string id)
    {
        moving = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (parts.TryGetValue(id, out var part)) part.Handle.Focus(FocusState.Keyboard);
            moving = false;
        });
    }

    private void Place(string id, int to, bool raise)
    {
        var part = parts[id];
        var from = list.Items.IndexOf(part.Row);
        to = Math.Clamp(to, 0, list.Items.Count - 1);
        if (from < 0 || from == to) return;
        moving = true;
        list.Items.RemoveAt(from);
        list.Items.Insert(to, part.Row);
        Refresh();
        Announce(Moved(id));
        if (raise) Reordered?.Invoke(this, new OuroReorderedEventArgs(Order, id, from, to));
        FocusHandle(id);
    }

    private void Dropped(Grid? row)
    {
        var from = dragFrom;
        dragFrom = -1;
        if (row?.Tag is not string id || from < 0) return;
        var to = list.Items.IndexOf(row);
        if (to < 0 || to == from) return;
        Refresh();
        Announce(Moved(id));
        Reordered?.Invoke(this, new OuroReorderedEventArgs(Order, id, from, to));
        FocusHandle(id);
    }

    private void Release(string id, bool keep)
    {
        if (picked != id) return;
        picked = null;
        var at = list.Items.IndexOf(parts[id].Row);
        if (!keep) Place(id, pickedFrom, false);
        else if (at != pickedFrom) Reordered?.Invoke(this, new OuroReorderedEventArgs(Order, id, pickedFrom, at));
        Announce(keep ? $"{parts[id].Source.Label} dropped. {OuroListRules.Position(at, list.Items.Count)}" : $"{parts[id].Source.Label} put back. {OuroListRules.Position(pickedFrom, list.Items.Count)}");
    }

    private void Keys(string id, KeyRoutedEventArgs e)
    {
        if (Locked) return;
        var at = list.Items.IndexOf(parts[id].Row);
        switch (e.Key)
        {
            case VirtualKey.Space:
                space = true;
                e.Handled = true;
                if (picked == id) Release(id, true);
                else
                {
                    picked = id;
                    pickedFrom = at;
                    Announce($"{parts[id].Source.Label} picked up. {OuroListRules.Position(at, list.Items.Count)}");
                }
                break;
            case VirtualKey.Escape when picked == id:
                e.Handled = true;
                Release(id, false);
                break;
            case VirtualKey.Up or VirtualKey.Down when picked == id:
                e.Handled = true;
                Place(id, OuroListRules.MoveTarget(at, list.Items.Count, e.Key == VirtualKey.Up ? OuroMove.Up : OuroMove.Down), false);
                break;
        }
    }

    private MenuFlyout Menu(string id)
    {
        var menu = new MenuFlyout();
        var at = list.Items.IndexOf(parts[id].Row);
        foreach (var (text, move) in new[] { ("Move up", OuroMove.Up), ("Move down", OuroMove.Down), ("Move to top", OuroMove.Top), ("Move to bottom", OuroMove.Bottom) })
        {
            var target = OuroListRules.MoveTarget(at, list.Items.Count, move);
            var item = new MenuFlyoutItem { Text = text, IsEnabled = target != at };
            item.Click += (_, _) => Place(id, target, true);
            menu.Items.Add(item);
        }
        return menu;
    }

    private OuroReorderHandle Handle(string id)
    {
        var handle = new OuroReorderHandle { Width = HandleTarget, Height = HandleTarget, Padding = new Thickness(0), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, UseSystemFocusVisuals = true };
        var icon = new FontIcon { Glyph = "\uE76F", FontSize = HandleIcon, FontFamily = new FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        handle.Content = icon;
        handle.Click += (_, _) =>
        {
            if (space || Locked) return;
            Menu(id).ShowAt(handle);
        };
        handle.KeyDown += (_, e) => Keys(id, e);
        handle.AddHandler(KeyUpEvent, new KeyEventHandler((_, _) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => space = false)), true);
        handle.LostFocus += (_, _) =>
        {
            if (!moving) Release(id, true);
        };
        handle.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase.IsPressedProperty, (_, _) =>
        {
            handle.CenterPoint = new Vector3((float)handle.ActualWidth / 2, (float)handle.ActualHeight / 2, 0);
            var s = handle.IsPressed && !Locked ? OuroUI.PressScale : 1f;
            handle.ScaleTransition = OuroUI.AnimationsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(OuroTokens.Duration.Press) } : null;
            handle.Scale = new Vector3(s, s, 1);
        });
        return handle;
    }

    private void Build()
    {
        if (root is null) return;
        var focused = parts.Where(p => p.Value.Handle.FocusState != FocusState.Unfocused).Select(p => p.Key).FirstOrDefault();
        moving = true;
        picked = null;
        parts.Clear();
        list.Items.Clear();
        foreach (var source in rows)
        {
            var handle = Handle(source.Id);
            var row = new Grid { Tag = source.Id, MinHeight = HandleTarget, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
            var words = source.Content ?? new TextBlock { Text = source.Label, TextWrapping = TextWrapping.Wrap, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
            if (words is FrameworkElement shown) shown.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(words);
            Grid.SetColumn(handle, 1);
            row.Children.Add(handle);
            parts[source.Id] = (row, handle, source);
            list.Items.Add(row);
        }
        moving = false;
        Refresh();
        if (focused is not null && parts.ContainsKey(focused)) FocusHandle(focused);
    }

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        var locked = Locked;
        MinHeight = HandleTarget;
        list.CanReorderItems = !locked;
        list.CanDragItems = !locked;
        list.Background = OuroUI.Brush(0);
        foreach (var key in new[] { "ListViewItemBackground", "ListViewItemBackgroundPointerOver", "ListViewItemBackgroundPressed", "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed", "ListViewItemBackgroundDisabled" })
            list.Resources[key] = OuroUI.Brush(0);
        foreach (var (row, handle, source) in list.Items.OfType<Grid>().Select(g => parts[(string)g.Tag]))
        {
            var index = list.Items.IndexOf(row);
            row.BorderBrush = OuroUI.Brush(p.Line);
            row.BorderThickness = new Thickness(0, OuroListRules.LineAbove(OuroItemGroupVariant.Divided, index) ? 1 : 0, 0, 0);
            handle.Locked = locked;
            handle.Opacity = locked ? 0.5 : 1;
            handle.Background = OuroUI.Brush(0);
            handle.Foreground = OuroUI.Brush(p.Muted);
            foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" }) handle.Resources[key] = OuroUI.Brush(0);
            foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed" }) handle.Resources[key] = OuroUI.Brush(p.Muted);
            AutomationProperties.SetName(handle, OuroListRules.HandleName(source.Label, reason));
            if (row.Children[0] is TextBlock words) words.Foreground = OuroUI.Brush(p.Text);
        }
        var firstWords = list.Items.OfType<Grid>().FirstOrDefault() is { } first ? FirstText(first.Children[0]) : null;
        var names = list.Items.OfType<Grid>().Select(g => parts[(string)g.Tag]).ToList();
        OuroUI.Report(this, "", "reorderable",
            ("label", label), ("words", OuroPaint.Hex(p.Text)), ("handle", OuroPaint.Hex(p.Muted)), ("behind", OuroPaint.Hex(p.Behind)), ("locked", locked), ("rows", names.Count),
            ("rowLabels", string.Join("|", names.Select(n => n.Source.Label))), ("handleNames", string.Join("|", names.Select(n => OuroListRules.HandleName(n.Source.Label, reason)))),
            ("pressScale", OuroUI.PressScale), ("trimmed", (firstWords as TextBlock)?.IsTextTrimmed ?? false), ("textHeight", Math.Round(firstWords?.ActualHeight ?? 0)));
    }
}

/// <summary>A row's handle: a Button that stays in the tab order while the order is locked, and says it is unavailable.</summary>
internal sealed class OuroReorderHandle : Button
{
    /// <summary>Whether the order is locked, so the handle is marked unavailable.</summary>
    internal bool Locked { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroReorderHandleAutomationPeer(this);
}

internal sealed class OuroReorderHandleAutomationPeer : ButtonAutomationPeer
{
    private readonly OuroReorderHandle owner;

    public OuroReorderHandleAutomationPeer(OuroReorderHandle owner) : base(owner) => this.owner = owner;

    protected override bool IsEnabledCore() => !owner.Locked && base.IsEnabledCore();
}

/// <summary>A reorderable list to UI Automation: a Group named by its label, not a tab stop; each row's handle is a tab stop.</summary>
public sealed partial class OuroReorderableAutomationPeer(OuroReorderable owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroReorderable);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;
}
