// Core's Composer (vendor/core/components/composer.spec.json): where the person writes a message. One frame, 44 epx at least,
// holds a box that grows up to MaxRows and then scrolls, and a Send button of at least 44 by 44 epx; one focus ring goes around
// the whole frame. UI Automation sees a Group named by its label holding an Edit (Value pattern) named by the label and a
// Button named by the send label, which keeps its name while sending. Enter sends and Shift+Enter adds a line, or with
// SendOn "modenter" Ctrl+Enter sends and Enter adds a line; nothing sends while an input method is composing, while sending, with
// a reason set, or when the composer is disabled. A reason shows under the box and is the Edit's help text.
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Ouro.UI.Windows;

/// <summary>Where a message is written and sent. Use <see cref="OuroTextField"/> for a form field instead.</summary>
public sealed partial class OuroComposer : Control
{
    private const double PadY = 10;

    private Grid? root;
    private Border? frame;
    private TextBox? box;
    private TextBlock? placeholderText, reasonText;
    private OuroButton? send;
    private string label = "", placeholder = "", sendLabel = "Send", sendingLabel = "Sending", sendOn = "enter", reason = "", value = "";
    private int maxRows = OuroChatRules.DefaultMaxRows;
    private bool sending, composing;

    public OuroComposer()
    {
        DefaultStyleKey = typeof(OuroComposer);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
        IsEnabledChanged += (_, _) => Refresh();
    }

    /// <summary>What the box is for, spoken: "Message Mia". Required.</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>Words shown in the empty box.</summary>
    public string Placeholder
    {
        get => placeholder;
        set { placeholder = value; Refresh(); }
    }

    /// <summary>The send button's name, for other languages. It stays the same while sending.</summary>
    public string SendLabel
    {
        get => sendLabel;
        set { sendLabel = value; Refresh(); }
    }

    /// <summary>The words announced once when sending starts, for other languages.</summary>
    public string SendingLabel
    {
        get => sendingLabel;
        set => sendingLabel = value;
    }

    /// <summary>"enter" (the default) or "modenter": what sends the message.</summary>
    public string SendOn
    {
        get => sendOn;
        set { sendOn = OuroChatRules.NormalizeSendOn(value); Refresh(); }
    }

    /// <summary>A send is in flight: the button shows progress, keeps its name and cannot be pressed, and "Sending" is announced once.</summary>
    public bool Sending
    {
        get => sending;
        set
        {
            if (sending == value) return;
            sending = value;
            if (value) Announce(sendingLabel);
            Refresh();
        }
    }

    /// <summary>Why sending is off right now ("You're offline."). Only the send button is disabled; the reason shows under the box.</summary>
    public string SendDisabledReason
    {
        get => reason;
        set { reason = value ?? ""; Refresh(); }
    }

    /// <summary>The most lines the box shows before it scrolls. The default is 10.</summary>
    public int MaxRows
    {
        get => maxRows;
        set { maxRows = value; Refresh(); }
    }

    /// <summary>What is written. The app clears it, by setting it to "", after <see cref="Send"/>.</summary>
    public string Value
    {
        get => box?.Text ?? value;
        set
        {
            this.value = value ?? "";
            if (box is not null && box.Text != this.value) box.Text = this.value;
            Refresh();
        }
    }

    /// <summary>The message was sent; the event argument is its text. The composer keeps the text until the app clears <see cref="Value"/>.</summary>
    public event EventHandler<string>? Send;

    /// <summary>The text changed; the event argument is the new text.</summary>
    public event EventHandler<string>? ValueChange;

    internal bool Trimmed => (placeholderText?.IsTextTrimmed ?? false) || (reasonText?.IsTextTrimmed ?? false);

    internal double TextHeight
    {
        get
        {
            var h = (placeholderText?.ActualHeight ?? 0) + (reasonText?.ActualHeight ?? 0);
            return h > 0 ? h : box?.ActualHeight ?? 0;
        }
    }

    internal string SpokenSend => sendLabel;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        frame = GetTemplateChild("Frame") as Border;
        placeholderText = GetTemplateChild("PlaceholderText") as TextBlock;
        reasonText = GetTemplateChild("ReasonText") as TextBlock;
        send = GetTemplateChild("SendButton") as OuroButton;
        if (GetTemplateChild("Input") is TextBox b && !ReferenceEquals(b, box))
        {
            box = b;
            box.Text = value;
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.GotFocus += (_, _) => Refresh();
            box.LostFocus += (_, _) => Refresh();
            box.TextChanged += (_, _) =>
            {
                value = box.Text;
                ValueChange?.Invoke(this, box.Text);
                Refresh();
            };
            box.TextCompositionStarted += (_, _) => composing = true;
            box.TextCompositionEnded += (_, _) => composing = false;
            box.PreviewKeyDown += OnKey;
        }
        if (send is not null) send.Click += (_, _) => Submit();
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposerAutomationPeer(this);

    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || composing || !OuroChatRules.IsSendKey(Down(VirtualKey.Shift), Down(VirtualKey.Control), sendOn)) return;
        e.Handled = true;
        Submit();
    }

    private void Submit()
    {
        if (box is null || !OuroChatRules.CanSend(box.Text, composing, sending, reason.Length > 0, !IsEnabled)) return;
        Send?.Invoke(this, box.Text);
    }

    private void Announce(string words)
    {
        if (words.Length > 0 && FrameworkElementAutomationPeer.CreatePeerForElement(this) is { } peer)
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, words, "ouro-composer-sending");
    }

    private void Refresh()
    {
        if (root is null || frame is null || box is null || send is null || placeholderText is null || reasonText is null) return;
        var p = OuroUI.SurfacePaint(this);
        var sendPaint = OuroUI.Paint(this, OuroTone.Primary, OuroVariant.Solid);
        var focused = box.FocusState != FocusState.Unfocused;
        var thickness = focused ? 2 : 1;
        frame.Background = OuroUI.Brush(p.Field);
        frame.BorderBrush = OuroUI.Brush(focused ? p.Focus : p.Edge);
        frame.BorderThickness = new Thickness(thickness);
        frame.CornerRadius = new CornerRadius(OuroUI.Theme.Radius);
        frame.MinHeight = OuroButton.MinTarget;
        box.Background = OuroUI.Brush(0);
        box.BorderThickness = new Thickness(0);
        box.Foreground = OuroUI.Brush(p.Text);
        box.Padding = new Thickness(OuroTextField.PaddingX - thickness, PadY - thickness, 4, PadY - thickness);
        box.VerticalAlignment = VerticalAlignment.Center;
        box.MaxHeight = OuroChatRules.BoxMaxHeight(maxRows, box.FontSize * 1.4, (PadY - thickness) * 2);
        AutomationProperties.SetName(box, label);
        AutomationProperties.SetHelpText(box, reason);
        placeholderText.Text = placeholder;
        placeholderText.Foreground = OuroUI.Brush(p.Placeholder);
        placeholderText.Margin = new Thickness(OuroTextField.PaddingX, PadY, 4, PadY);
        placeholderText.Visibility = box.Text.Length == 0 && placeholder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        reasonText.Text = reason;
        reasonText.Foreground = OuroUI.Brush(p.Muted);
        reasonText.Visibility = reason.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        send.Label = sendLabel;
        send.Tone = OuroTone.Primary;
        send.Variant = OuroVariant.Solid;
        send.IsBusy = sending;
        send.IsEnabled = !sending && reason.Length == 0;
        send.MinWidth = send.MinHeight = OuroButton.MinTarget;
        Opacity = IsEnabled ? 1 : 0.5;
        OuroUI.Report(this, "", "composer",
            ("text", OuroPaint.Hex(p.Text)), ("placeholder", OuroPaint.Hex(p.Placeholder)), ("muted", OuroPaint.Hex(p.Muted)), ("field", OuroPaint.Hex(p.Field)),
            ("behind", OuroPaint.Hex(p.Behind)), ("edge", OuroPaint.Hex(p.Edge)), ("sendText", OuroPaint.Hex(sendPaint.Label)), ("send", OuroPaint.Hex(sendPaint.Fill)),
            ("spokenName", label), ("sendName", sendLabel), ("reasoned", reason.Length > 0), ("reason", reason),
            ("trimmed", Trimmed), ("textHeight", Math.Round(TextHeight)));
    }
}

/// <summary>A composer to UI Automation: a Group named by its label, never a tab stop. The box (an Edit) and the send button are its children.</summary>
public sealed partial class OuroComposerAutomationPeer(OuroComposer owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroComposer);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;
}
