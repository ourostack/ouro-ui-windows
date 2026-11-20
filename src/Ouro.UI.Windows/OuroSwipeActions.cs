// Core's SwipeActions (vendor/core/components/swipe-actions.spec.json): a list row whose actions a swipe reveals, and the same actions in a menu always one
// press away, so a swipe is never the only way. UI Automation sees a Group named by the row's label, with a Button ("Actions for {label}") that opens a
// MenuFlyout; the swipe is WinUI's SwipeControl, a touch gesture only. A chosen action that removes the row moves focus to the next row, or the one before
// when it was last.
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ouro.UI.Hx.Tokens;

namespace Ouro.UI.Windows;

/// <summary>A list row whose actions a swipe reveals; the same actions are always one press away in a menu.</summary>
public sealed partial class OuroSwipeActions : Control
{
    /// <summary>Core's metric, in effective pixels: the menu button's target.</summary>
    public const double MenuTarget = 44;

    private readonly SwipeControl swipe = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = MenuTarget };
    private readonly Button button = new() { Width = MenuTarget, Height = MenuTarget, Padding = new Thickness(0), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon icon = new() { Glyph = "\uE712", FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false };
    private Grid? root;
    private string label = "", actionsLabel = "", leading = "", trailing = "";
    private OuroTone tone = OuroTone.Secondary;
    private UIElement? content;
    private bool touched;

    public OuroSwipeActions()
    {
        DefaultStyleKey = typeof(OuroSwipeActions);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        button.Content = icon;
        button.Flyout = Menu();
        swipe.ContextFlyout = Menu();
        button.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase.IsPressedProperty, (_, _) => Press());
        button.GotFocus += (_, _) => Show(true);
        button.LostFocus += (_, _) => Show(!touched);
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            touched = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
            Show(!touched);
        }), true);
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
        swipe.SizeChanged += (_, _) => Refresh();
    }

    /// <summary>Raised when the actions menu opens, from the button or by a long press or right click on the row.</summary>
    public event EventHandler? MenuOpened;

    /// <summary>Raised when an action is chosen, by a press on an action a swipe revealed, a swipe past the end of a side with one action, or from the menu, with its label. The app does what it names.</summary>
    public event EventHandler<string>? ActionInvoked;

    /// <summary>What the row is, such as a message's subject. It names the row and the actions button.</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>The name of the button that opens the actions menu. Default "Actions for {label}".</summary>
    public string ActionsLabel
    {
        get => actionsLabel;
        set { actionsLabel = value; Refresh(); }
    }

    /// <summary>The actions a swipe toward the end reveals, by label, such as "Pin, Mute"; the first is the primary one.</summary>
    public string Leading
    {
        get => leading;
        set { leading = value; Build(); }
    }

    /// <summary>The actions a swipe toward the start reveals, by label, such as "Delete, Archive"; the first is the primary one.</summary>
    public string Trailing
    {
        get => trailing;
        set { trailing = value; Build(); }
    }

    /// <summary>The colour behind each side's first action, the primary one, such as destructive for Delete. The other actions are secondary.</summary>
    public OuroTone Tone
    {
        get => tone;
        set { tone = value; Build(); }
    }

    /// <summary>The row the person sees at rest, usually an Item.</summary>
    public UIElement? Content
    {
        get => content;
        set { content = value; swipe.Content = value; Refresh(); }
    }

    /// <summary>The name of the actions button.</summary>
    internal string ButtonName => OuroListRules.ActionsButtonName(label, actionsLabel);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is null) return;
        root.Children.Clear();
        root.Children.Add(swipe);
        Grid.SetColumn(button, 1);
        root.Children.Add(button);
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroSwipeActionsAutomationPeer(this);

    private static FrameworkElement? FirstText(DependencyObject? node)
    {
        if (node is TextBlock) return (FrameworkElement)node;
        if (node is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FirstText(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            Fill(menu);
            MenuOpened?.Invoke(this, EventArgs.Empty);
        };
        return menu;
    }

    private void Fill(MenuFlyout menu)
    {
        menu.Items.Clear();
        foreach (var action in OuroListRules.Actions(leading).Concat(OuroListRules.Actions(trailing)))
        {
            var item = new MenuFlyoutItem { Text = action };
            item.Click += (_, _) => Choose(action);
            menu.Items.Add(item);
        }
    }

    private SwipeItems Items(string labels, OuroPaintResult primary, OuroPaintResult rest)
    {
        var names = OuroListRules.Actions(labels);
        var items = new SwipeItems { Mode = names.Count == 1 ? SwipeMode.Execute : SwipeMode.Reveal };
        foreach (var (name, i) in names.Select((name, i) => (name, i)))
        {
            var paint = i == 0 ? primary : rest;
            var item = new SwipeItem { Text = name, Background = OuroUI.Brush(paint.Fill), Foreground = OuroUI.Brush(paint.Label), BehaviorOnInvoked = SwipeBehaviorOnInvoked.Close };
            item.Invoked += (_, _) => Choose(name);
            items.Add(item);
        }
        return items;
    }

    // The row's place is read before the app acts, because the app removes the row from its parent as it handles the action; once the menu has closed, focus goes to
    // the row that took its place.
    private void Choose(string action)
    {
        var parent = Parent as Panel;
        var at = parent?.Children.IndexOf(this) ?? -1;
        ActionInvoked?.Invoke(this, action);
        if (parent is null || at < 0) return;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (parent.Children.Contains(this)) return;
            var next = OuroListRules.FocusAfterRemoval(at, parent.Children.Count);
            if (next < 0) return;
            (FocusManager.FindFirstFocusableElement(parent.Children[next]) as Control)?.Focus(FocusState.Keyboard);
        });
    }

    // Under touch the button is drawn clear and takes no taps, so it never covers the row; it stays in the UI Automation tree and keeps its tab stop.
    private void Show(bool shown)
    {
        button.Opacity = shown ? 1 : 0;
        button.IsHitTestVisible = shown;
    }

    private void Press()
    {
        button.CenterPoint = new Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);
        var s = button.IsPressed ? OuroUI.PressScale : 1f;
        button.ScaleTransition = OuroUI.AnimationsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(OuroTokens.Duration.Press) } : null;
        button.Scale = new Vector3(s, s, 1);
    }

    private void Build()
    {
        if (root is null) return;
        var primary = OuroUI.Paint(this, tone, OuroVariant.Solid);
        var rest = OuroUI.Paint(this, OuroTone.Secondary, OuroVariant.Solid);
        swipe.LeftItems = OuroListRules.Actions(leading).Count == 0 ? null : Items(leading, primary, rest);
        swipe.RightItems = OuroListRules.Actions(trailing).Count == 0 ? null : Items(trailing, primary, rest);
        Refresh();
    }

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        var primary = OuroUI.Paint(this, tone, OuroVariant.Solid);
        MinHeight = MenuTarget;
        button.Background = OuroUI.Brush(0);
        button.Foreground = OuroUI.Brush(p.Muted);
        AutomationProperties.SetName(button, ButtonName);
        var words = FirstText(content);
        OuroUI.Report(this, "", "swipe-actions",
            ("action", OuroPaint.Hex(primary.Label)), ("actionFill", OuroPaint.Hex(primary.Fill)), ("menu", OuroPaint.Hex(p.Muted)), ("behind", OuroPaint.Hex(p.Behind)),
            ("tone", tone), ("actions", OuroListRules.Actions(leading).Count + OuroListRules.Actions(trailing).Count), ("button", ButtonName),
            ("pressScale", OuroUI.PressScale), ("trimmed", (words as TextBlock)?.IsTextTrimmed ?? false), ("textHeight", Math.Round(words?.ActualHeight ?? 0)));
    }
}

/// <summary>A swipe row to UI Automation: a Group named by its label, not a tab stop; its menu button is the one control inside.</summary>
public sealed partial class OuroSwipeActionsAutomationPeer(OuroSwipeActions owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroSwipeActions);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;
}
