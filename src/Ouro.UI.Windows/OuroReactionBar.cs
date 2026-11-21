// Core's ReactionBar (vendor/core/components/reaction-bar.spec.json): the emoji reactions on a message as a row of pills, each with its
// count, and optionally a plus button to add one. The person's own reactions are tinted with the accent. UI Automation sees a Group named
// by the label, which is not a tab stop, and in it one Button per pill with the Toggle pattern (On when it is the person's own) named
// "Like, 3", then the add button; each is its own tab stop and at least 44 by 44 epx, though a pill is drawn 28 epx tall. The pills wrap
// onto more rows when the row or the text size leaves no room. A press changes nothing here: the bar raises React, and the app adds or
// removes the person's reaction and sends the new list.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Ouro.UI.Windows;

/// <summary>
/// The reactions on a message. Put it under an <see cref="OuroMessage"/>; to react in a form, use <see cref="OuroSwitch"/> or <see cref="OuroButton"/>.
/// </summary>
public sealed partial class OuroReactionBar : Control
{
    private readonly OuroWrapRow row = new() { Gap = OuroChatRules.PillGap };
    private Grid? root;
    private string label = "Reactions", moreLabel = "Add reaction", reactions = "";
    private bool showMore;
    private IReadOnlyList<OuroReaction> parsed = [];

    public OuroReactionBar()
    {
        DefaultStyleKey = typeof(OuroReactionBar);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>The group's spoken name. Defaults to "Reactions".</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>The reactions, one per line of "key: emoji | name | count | mine", separated by semicolons or new lines.</summary>
    public string Reactions
    {
        get => reactions;
        set { reactions = value; parsed = OuroChatRules.ParseReactions(value); Rebuild(); }
    }

    /// <summary>Shows a plus button after the pills that raises <see cref="More"/>.</summary>
    public bool ShowMore
    {
        get => showMore;
        set { showMore = value; Rebuild(); }
    }

    /// <summary>The plus button's spoken name. Defaults to "Add reaction".</summary>
    public string MoreLabel
    {
        get => moreLabel;
        set { moreLabel = value; Rebuild(); }
    }

    /// <summary>A pill was pressed; carries its key. The app adds or removes the person's own reaction.</summary>
    public event EventHandler<string>? React;

    /// <summary>The plus button was pressed.</summary>
    public event EventHandler? More;

    /// <summary>The spoken name of each button, in order.</summary>
    internal IEnumerable<string> ButtonNames => row.Children.OfType<OuroReactionPill>().Select(p => p.SpokenName);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is not null && !root.Children.Contains(row)) root.Children.Add(row);
        Rebuild();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroReactionBarAutomationPeer(this);

    private void Rebuild()
    {
        row.Children.Clear();
        foreach (var reaction in parsed)
        {
            var pill = new OuroReactionPill { Reaction = reaction };
            pill.Click += (_, _) => React?.Invoke(this, reaction.Key);
            row.Children.Add(pill);
        }
        if (showMore)
        {
            var add = new OuroReactionPill { MoreName = moreLabel };
            add.Click += (_, _) => More?.Invoke(this, EventArgs.Empty);
            row.Children.Add(add);
        }
        Refresh();
    }

    internal string SpokenLabel => label;

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        var c = OuroUI.Colors(this);
        var primary = OuroUI.Argb(c.Primary);
        uint fill, words, ownFill, ownWords, edge;
        if (OuroUI.HighContrast)
        {
            fill = p.Behind;
            words = p.Text;
            edge = p.Text;
            ownFill = p.Checked;
            ownWords = p.Tick;
        }
        else
        {
            fill = OuroUI.Argb(c.Muted);
            words = p.Text;
            ownFill = OuroNav.Wash(p.Behind, primary);
            ownWords = OuroNav.CurrentWords(primary, ownFill, p.Text);
            edge = primary;
        }
        if (!IsEnabled && !OuroUI.HighContrast)
        {
            words = OuroPaint.Readable(OuroPaint.Mix(fill, words, 0.75), fill, words);
            ownWords = OuroPaint.Readable(OuroPaint.Mix(ownFill, ownWords, 0.75), ownFill, ownWords);
        }
        var trimmed = false;
        double textHeight = 0;
        foreach (var pill in row.Children.OfType<OuroReactionPill>())
        {
            pill.IsEnabled = IsEnabled;
            var own = pill.Reaction?.Mine == true;
            pill.Apply(own ? ownFill : fill, own ? ownWords : words, own || OuroUI.HighContrast ? edge : 0);
            trimmed |= pill.Trimmed;
            textHeight = Math.Max(textHeight, pill.TextHeight);
        }
        OuroUI.Report(this, IsEnabled ? "" : "Disabled", "reaction-bar",
            ("words", OuroPaint.Hex(words)), ("pill", OuroPaint.Hex(fill)), ("ownWords", OuroPaint.Hex(ownWords)), ("ownFill", OuroPaint.Hex(ownFill)),
            ("behind", OuroPaint.Hex(p.Behind)), ("pills", row.Children.Count), ("buttonNames", string.Join("|", ButtonNames)),
            ("trimmed", trimmed), ("textHeight", Math.Round(textHeight)));
    }
}

/// <summary>One pill of a <see cref="OuroReactionBar"/>, or its plus button. Made by the bar.</summary>
public sealed partial class OuroReactionPill : ToggleButton
{
    private readonly TextBlock emoji = new() { IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly TextBlock count = new() { IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private uint fill, words, edge;
    private OuroReaction? reaction;
    private string? moreName;

    public OuroReactionPill()
    {
        DefaultStyleKey = typeof(OuroReactionPill);
        UseSystemFocusVisuals = true;
        MinHeight = OuroChatRules.PillTouchHeight;
        MinWidth = OuroChatRules.PillTouchHeight;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = OuroChatRules.PillGap };
        foreach (var part in new[] { emoji, count })
        {
            AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
            content.Children.Add(part);
        }
        Content = content;
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Redraw());
        RegisterPropertyChangedCallback(IsPointerOverProperty, (_, _) => Redraw());
    }

    /// <summary>The reaction this pill shows; null for the plus button.</summary>
    internal OuroReaction? Reaction
    {
        get => reaction;
        set
        {
            reaction = value;
            emoji.Text = value?.Emoji ?? "";
            count.Text = value is { Count: > 0 } ? value.Count.ToString() : "";
            IsChecked = value?.Mine == true;
        }
    }

    /// <summary>The plus button's name; set only on that button.</summary>
    internal string? MoreName
    {
        get => moreName;
        set
        {
            moreName = value;
            emoji.Text = "+";
            emoji.FontSize = 18;
            count.Text = "";
        }
    }

    internal bool IsAdd => moreName is not null;

    internal string SpokenName => reaction is { } r ? OuroChatRules.ReactionName(r.Name, r.Count) : moreName ?? "";

    internal bool Trimmed => emoji.IsTextTrimmed || count.IsTextTrimmed;

    internal double TextHeight => Math.Max(emoji.ActualHeight, count.ActualHeight);

    /// <summary>The bar's paint: the fill, the words and the edge (0 for none). Hover and press move the fill toward the words.</summary>
    internal void Apply(uint fillColour, uint wordsColour, uint edgeColour)
    {
        fill = fillColour;
        words = wordsColour;
        edge = edgeColour;
        Redraw();
    }

    /// <summary>The bar raises React; a press never flips the pill itself, so the app stays the one source of whose reaction it is.</summary>
    protected override void OnToggle()
    {
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroReactionPillAutomationPeer(this);

    private void Redraw()
    {
        var shown = !IsEnabled ? fill : IsPressed ? OuroPaint.Mix(fill, words, 0.16) : IsPointerOver ? OuroPaint.Mix(fill, words, 0.08) : fill;
        Background = OuroUI.Brush(shown);
        Foreground = OuroUI.Brush(words);
        emoji.Foreground = OuroUI.Brush(words);
        count.Foreground = OuroUI.Brush(words);
        BorderBrush = OuroUI.Brush(edge);
        BorderThickness = new Thickness(edge == 0 ? 0 : 1);
        CornerRadius = new CornerRadius(OuroChatRules.PillHeight / 2);
    }
}

/// <summary>A reaction pill to UI Automation: a Button named "Like, 3" with the Toggle pattern, and the plus button a plain Button.</summary>
public sealed partial class OuroReactionPillAutomationPeer(OuroReactionPill owner) : ToggleButtonAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(OuroReactionPill);

    protected override string GetNameCore() => owner.SpokenName;

    protected override object? GetPatternCore(PatternInterface pattern) =>
        pattern == PatternInterface.Toggle && owner.IsAdd ? null : base.GetPatternCore(pattern);
}

/// <summary>A reaction bar to UI Automation: a Group named by its label, never a tab stop.</summary>
public sealed partial class OuroReactionBarAutomationPeer(OuroReactionBar owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroReactionBar);

    protected override string GetNameCore() => owner.SpokenLabel;

    protected override bool IsKeyboardFocusableCore() => false;
}

/// <summary>Lays children out in rows that wrap, leaving <see cref="Gap"/> between them.</summary>
internal sealed class OuroWrapRow : Panel
{
    public double Gap { get; set; }

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > available.Width)
            {
                y += rowHeight + Gap;
                x = 0;
                rowHeight = 0;
            }
            x += size.Width + Gap;
            rowHeight = Math.Max(rowHeight, size.Height);
            widest = Math.Max(widest, x - Gap);
        }
        return new Size(widest, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > final.Width)
            {
                y += rowHeight + Gap;
                x = 0;
                rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Gap;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return final;
    }
}
