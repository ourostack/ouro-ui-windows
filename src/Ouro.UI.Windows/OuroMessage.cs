// Core's Message (vendor/core/components/message.spec.json): one message in a conversation, in a bubble on the side of whoever wrote it,
// with the author's name and the time above it. The person's own messages sit at the end in the primary colour, others' at the start on
// the muted fill, the app's notes centred with no bubble. UI Automation sees one ListItem named "Author, time, text, state", which is not
// a tab stop; the words inside it are folded into that name. A message that failed says "Not sent" in the destructive colour.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Ouro.UI.Windows;

/// <summary>One message in a conversation. Stack them in a column, or use one alone for a single note.</summary>
public sealed partial class OuroMessage : Control
{
    private readonly TextBlock header = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly Border bubble = new();
    private readonly StackPanel stack = new() { Spacing = 4 };
    private Grid? root;
    private string author = "", time = "", body = "";
    private OuroMessageRole role = OuroMessageRole.Assistant;
    private bool continued, streaming, failed, pending;

    public OuroMessage()
    {
        DefaultStyleKey = typeof(OuroMessage);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var part in new[] { header, text, state })
            AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
        bubble.Child = text;
        stack.Children.Add(header);
        stack.Children.Add(bubble);
        stack.Children.Add(state);
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>Who wrote it, such as "Ana" or "You". Always part of the spoken name, even when <see cref="Continued"/> hides it.</summary>
    public string Author
    {
        get => author;
        set { author = value; Refresh(); }
    }

    /// <summary>When it was sent, such as "9:41". Empty for a note with no time.</summary>
    public string Time
    {
        get => time;
        set { time = value; Refresh(); }
    }

    /// <summary>What was written.</summary>
    public string Text
    {
        get => body;
        set { body = value; Refresh(); }
    }

    /// <summary>Whose message it is: others' at the start, the person's own at the end, or a centred note from the app.</summary>
    public OuroMessageRole Role
    {
        get => role;
        set { role = value; Refresh(); }
    }

    /// <summary>It follows another message by the same author: the name and time leave the picture but stay in the spoken name.</summary>
    public bool Continued
    {
        get => continued;
        set { continued = value; Refresh(); }
    }

    /// <summary>The reply is still being written. A list announces it once, when it finishes, never while it grows.</summary>
    public bool Streaming
    {
        get => streaming;
        set { streaming = value; Refresh(); }
    }

    /// <summary>It was not sent: a destructive-coloured bubble marked "Not sent". The app offers any retry; the message has no buttons.</summary>
    public bool Failed
    {
        get => failed;
        set { failed = value; Refresh(); }
    }

    /// <summary>It is being sent: spoken as "Sending".</summary>
    public bool Pending
    {
        get => pending;
        set { pending = value; Refresh(); }
    }

    /// <summary>The words a screen reader reads for this message.</summary>
    internal string SpokenName => OuroChatRules.MessageName(author, time, body, failed, pending);

    /// <summary>The message's words are cut off.</summary>
    internal bool Trimmed => text.IsTextTrimmed;

    /// <summary>The height of the message's words.</summary>
    internal double TextHeight => text.ActualHeight;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is not null && !root.Children.Contains(stack)) root.Children.Add(stack);
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroMessageAutomationPeer(this);

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        var c = OuroUI.Colors(this);
        var own = role == OuroMessageRole.User;
        var note = role == OuroMessageRole.System;
        uint fill, words;
        if (OuroUI.HighContrast)
        {
            var paint = OuroUI.Paint(this, OuroTone.Primary, own ? OuroVariant.Solid : OuroVariant.Plain);
            fill = own ? paint.Fill : p.Behind;
            words = own ? paint.Label : p.Text;
        }
        else if (own)
        {
            var paint = OuroUI.Paint(this, OuroTone.Primary, OuroVariant.Solid);
            fill = paint.Fill;
            words = paint.Label;
        }
        else
        {
            fill = OuroUI.Argb(c.Muted);
            words = p.Text;
        }
        if (failed)
        {
            fill = OuroPaint.Mix(p.Behind, p.Error, 0.16);
            words = OuroPaint.Readable(p.Error, fill, p.Text);
        }
        if (note)
        {
            fill = 0;
            words = p.Muted;
        }
        var behind = fill == 0 ? p.Behind : fill;
        var align = role switch { OuroMessageRole.User => HorizontalAlignment.Right, OuroMessageRole.System => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
        stack.HorizontalAlignment = align;
        header.Text = time.Length > 0 && !note ? $"{author}  {time}" : note ? "" : author;
        header.Foreground = OuroUI.Brush(p.Muted);
        header.Visibility = continued || note || header.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        header.TextAlignment = own ? TextAlignment.Right : TextAlignment.Left;
        text.Text = body;
        text.Foreground = OuroUI.Brush(words);
        text.TextAlignment = note ? TextAlignment.Center : TextAlignment.Left;
        bubble.Background = OuroUI.Brush(fill);
        bubble.BorderBrush = OuroUI.Brush(OuroUI.HighContrast && !own && !note ? p.Edge : 0);
        bubble.BorderThickness = new Thickness(OuroUI.HighContrast && !own && !note ? 1 : 0);
        bubble.CornerRadius = new CornerRadius(OuroUI.Theme.Radius);
        bubble.Padding = note ? new Thickness(0) : new Thickness(OuroChatRules.BubblePaddingX, OuroChatRules.BubblePaddingY, OuroChatRules.BubblePaddingX, OuroChatRules.BubblePaddingY);
        bubble.HorizontalAlignment = align;
        stack.MaxWidth = note || ActualWidth <= 0 ? double.PositiveInfinity : OuroChatRules.BubbleMaxWidth(ActualWidth);
        state.Text = failed ? "Not sent" : pending ? "Sending" : "";
        state.Foreground = OuroUI.Brush(failed ? p.Error : p.Muted);
        state.TextAlignment = own ? TextAlignment.Right : TextAlignment.Left;
        state.Visibility = state.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OuroUI.Report(this, streaming ? "Busy" : "", "message",
            ("label", OuroPaint.Hex(words)), ("bubble", OuroPaint.Hex(behind)), ("muted", OuroPaint.Hex(p.Muted)), ("error", OuroPaint.Hex(p.Error)), ("behind", OuroPaint.Hex(p.Behind)),
            ("role", role), ("failed", failed), ("note", note), ("spokenName", SpokenName),
            ("trimmed", text.IsTextTrimmed), ("textHeight", Math.Round(text.ActualHeight)));
    }
}

/// <summary>A message to UI Automation: a ListItem named by its author, time, words and state, never a tab stop.</summary>
public sealed partial class OuroMessageAutomationPeer(OuroMessage owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(OuroMessage);

    protected override string GetNameCore() => owner.SpokenName;

    protected override bool IsKeyboardFocusableCore() => false;
}
