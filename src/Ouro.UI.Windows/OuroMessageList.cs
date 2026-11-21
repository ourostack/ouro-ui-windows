// Core's MessageList (vendor/core/components/message-list.spec.json): a conversation, the messages in order in a labelled,
// scrolling region that opens at the newest one. UI Automation sees a List named by its label with a ListItem per message; a
// message from someone else that arrives after the list is in use raises one polite notification, "author: text", and the
// person's own messages and the history the list opened with raise none. A "Jump to latest" button appears once the reader
// scrolls up and a "Load older messages" button sits above the first message when older ones can be loaded; both are at least
// 44 by 44 epx and reachable by Tab.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Ouro.UI.Windows;

/// <summary>A conversation of <see cref="OuroMessage"/>s that opens at the newest. Use <see cref="OuroMessage"/> alone for a single note.</summary>
public sealed partial class OuroMessageList : Control
{
    private readonly StackPanel stack = new() { Spacing = OuroChatRules.MessageGap };
    private readonly ScrollViewer scroller = new() { IsTabStop = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
    private readonly OuroButton older = new() { Tone = OuroTone.Secondary, Variant = OuroVariant.Outline, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly OuroButton jump = new() { Tone = OuroTone.Primary, Variant = OuroVariant.Solid, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8) };
    private readonly Dictionary<string, OuroMessage> made = [];
    private Grid? root;
    private string label = "", jumpLabel = "Jump to latest", olderLabel = "Load older messages", slot = "";
    private bool hasOlder;
    private int notified;
    private IReadOnlyList<OuroListMessage> messages = [];

    public OuroMessageList()
    {
        DefaultStyleKey = typeof(OuroMessageList);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        scroller.Content = stack;
        older.Click += (_, _) => LoadOlder?.Invoke(this, EventArgs.Empty);
        jump.Click += (_, _) => ToNewest();
        scroller.ViewChanged += (_, _) => Refresh();
        stack.SizeChanged += (_, _) => Refresh();
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the conversation is, spoken: "Conversation with Ouro". Required.</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>The jump button's name, for other languages.</summary>
    public string JumpLabel
    {
        get => jumpLabel;
        set { jumpLabel = value; Refresh(); }
    }

    /// <summary>Older messages can be loaded: a button above the first message raises <see cref="LoadOlder"/>.</summary>
    public bool HasOlder
    {
        get => hasOlder;
        set { hasOlder = value; Refresh(); }
    }

    /// <summary>The load-older button's name, for other languages.</summary>
    public string OlderLabel
    {
        get => olderLabel;
        set { olderLabel = value; Refresh(); }
    }

    /// <summary>The messages, one per line as "id: author | time | text | mine" separated by semicolons or new lines.</summary>
    public string Messages
    {
        get => slot;
        set => Update(value);
    }

    /// <summary>The load-older button was pressed.</summary>
    public event EventHandler? LoadOlder;

    /// <summary>A message from someone else arrived while the list is in use; the event argument is the words announced, "author: text".</summary>
    public event EventHandler<string>? NewMessage;

    internal bool Trimmed => stack.Children.OfType<OuroMessage>().Any(m => m.Trimmed);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is not null && !root.Children.Contains(scroller))
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(scroller, 1);
            Grid.SetRow(jump, 1);
            root.Children.Add(older);
            root.Children.Add(scroller);
            root.Children.Add(jump);
        }
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroMessageListAutomationPeer(this);

    private void Update(string value)
    {
        var before = messages;
        var wasAtBottom = OuroChatRules.AtBottom(scroller.VerticalOffset, scroller.ScrollableHeight);
        slot = value;
        messages = OuroChatRules.ParseMessages(value);
        stack.Children.Clear();
        foreach (var m in messages)
        {
            if (!made.TryGetValue(m.Id, out var view)) made[m.Id] = view = new OuroMessage();
            view.Author = m.Author;
            view.Time = m.Time;
            view.Text = m.Text;
            view.Role = m.Mine ? OuroMessageRole.User : OuroMessageRole.Assistant;
            stack.Children.Add(view);
        }
        foreach (var gone in made.Keys.Where(k => messages.All(m => m.Id != k)).ToList()) made.Remove(gone);
        foreach (var m in OuroChatRules.ToAnnounce(before, messages)) Announce(m);
        if (before.Count == 0 || wasAtBottom)
        {
            scroller.Loaded -= OpenAtNewest;
            scroller.Loaded += OpenAtNewest;
        }
        scroller.UpdateLayout();
        if (before.Count > 0 && wasAtBottom) ToNewest();
        Refresh();
    }

    private void OpenAtNewest(object sender, RoutedEventArgs e)
    {
        scroller.Loaded -= OpenAtNewest;
        scroller.UpdateLayout();
        ToNewest();
    }

    private void ToNewest() => scroller.ChangeView(null, scroller.ScrollableHeight, null, true);

    private void Announce(OuroListMessage m)
    {
        var words = OuroChatRules.Announcement(m);
        notified++;
        if (FrameworkElementAutomationPeer.CreatePeerForElement(this) is { } peer)
            peer.RaiseNotificationEvent(AutomationNotificationKind.ItemAdded, AutomationNotificationProcessing.All, words, "ouro-message");
        NewMessage?.Invoke(this, words);
    }

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        older.Label = olderLabel;
        older.Visibility = hasOlder ? Visibility.Visible : Visibility.Collapsed;
        jump.Label = jumpLabel;
        jump.Visibility = OuroChatRules.AtBottom(scroller.VerticalOffset, scroller.ScrollableHeight) ? Visibility.Collapsed : Visibility.Visible;
        OuroUI.Report(this, "", "message-list",
            ("words", OuroPaint.Hex(p.Text)), ("behind", OuroPaint.Hex(p.Behind)),
            ("messages", messages.Count), ("hasOlder", hasOlder), ("olderName", olderLabel),
            ("announce", "polite"), ("notified", notified),
            ("trimmed", Trimmed), ("textHeight", Math.Round(stack.Children.OfType<OuroMessage>().Sum(m => m.TextHeight))));
    }
}

/// <summary>A message list to UI Automation: a List named by its label, never a tab stop. Its messages are ListItems in order.</summary>
public sealed partial class OuroMessageListAutomationPeer(OuroMessageList owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(OuroMessageList);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;
}
