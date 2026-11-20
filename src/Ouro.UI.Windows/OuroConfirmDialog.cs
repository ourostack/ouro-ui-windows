// Core's ConfirmDialog (vendor/core/components/confirm-dialog.spec.json): the question before a consequential step. UI
// Automation sees a Window named by its title, with Cancel and the confirm button. Focus starts on Cancel, Escape and
// Cancel close it, and a press outside never does. When it is for a declared action, its words are the ones the action
// declares as its confirm text (core's actionText(action).confirm), the same the consent prompt and the agent doors ask.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Ouro.Actions;

namespace Ouro.UI.Windows;

/// <summary>A centered panel that asks before an action runs, with Cancel focused first.</summary>
public sealed partial class OuroConfirmDialog : OuroPanel
{
    private readonly OuroButton cancel = new() { Tone = OuroTone.Secondary, Variant = OuroVariant.Outline };
    private readonly OuroButton confirm = new();

    public static readonly DependencyProperty ConfirmLabelProperty = Register(nameof(ConfirmLabel), typeof(string), "");
    public static readonly DependencyProperty CancelLabelProperty = Register(nameof(CancelLabel), typeof(string), "Cancel");
    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), typeof(OuroTone), OuroTone.Primary);
    public static readonly DependencyProperty IsBusyProperty = Register(nameof(IsBusy), typeof(bool), false);

    public OuroConfirmDialog()
    {
        cancel.Click += (_, _) => Close(OuroCloseReason.Button);
        confirm.Click += (_, _) =>
        {
            if (IsBusy) return;
            Confirmed?.Invoke(this, EventArgs.Empty);
        };
        FooterButtons.Children.Add(cancel);
        FooterButtons.Children.Add(confirm);
    }

    /// <summary>The confirm button's words, such as Share. Required.</summary>
    public string ConfirmLabel { get => (string)GetValue(ConfirmLabelProperty); set => SetValue(ConfirmLabelProperty, value); }

    /// <summary>Cancel's words. Default Cancel.</summary>
    public string CancelLabel { get => (string)GetValue(CancelLabelProperty); set => SetValue(CancelLabelProperty, value); }

    /// <summary>The confirm button's colour role: primary, or destructive for a step that can't be undone.</summary>
    public OuroTone Tone { get => (OuroTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>The confirmed action is running: the confirm button keeps its width and can't be pressed again; Cancel still closes.</summary>
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }

    /// <summary>The declared action this asks about. Its confirm text, from <see cref="Catalog"/>, becomes the description.</summary>
    public OuroActionInfo? Action { get; set; }

    /// <summary>The catalog <see cref="Action"/> belongs to, which holds its confirm text.</summary>
    public OuroActionCatalog? Catalog { get; set; }

    /// <summary>The person confirmed.</summary>
    public event EventHandler? Confirmed;

    /// <summary>What it asks: the action's confirm text when it has an action, else <see cref="OuroPanel.Description"/>.</summary>
    public string Question => OuroPanelRules.Question(Action is { } a && Catalog is { } c ? c.ConfirmText(a) : null, Description);

    /// <summary>An outside press never closes it.</summary>
    public override bool ClosesOnOutsidePress => false;

    protected override string DescriptionShown => Question;

    protected override string Kind => "confirm-dialog";

    protected override bool HasClose => false;

    protected override AutomationControlType Role => AutomationControlType.Window;

    protected override Control InitialFocus() => cancel;

    internal override (string, object?)[] Facts()
    {
        cancel.Label = CancelLabel;
        confirm.Label = ConfirmLabel;
        confirm.Tone = Tone;
        confirm.IsBusy = IsBusy;
        return [("cancel", CancelLabel), ("confirm", ConfirmLabel), ("tone", Tone), ("busy", IsBusy), ("action", Action?.Id ?? "none"), ("question", Question.Length)];
    }

    internal override void Place(FrameworkElement scrim, double hostWidth, double hostHeight)
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(16);
        Frame.MaxWidth = OuroPanelRules.DialogMaxWidth;
    }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroConfirmDialog), new PropertyMetadata(fallback, (d, _) => ((OuroConfirmDialog)d).Refresh()));
}
