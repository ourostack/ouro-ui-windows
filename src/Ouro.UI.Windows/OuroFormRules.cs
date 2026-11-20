// The rules core's form specs state in words, as plain functions so the tests run them off Windows: a control's spoken
// name and help text, and how a Select's keys move its choice.
namespace Ouro.UI.Windows;

/// <summary>What a Select, Switch and Field say to assistive technology, and how a Select's arrow keys move.</summary>
public static class OuroFormRules
{
    /// <summary>The spoken name: the label, with ", required" when it is.</summary>
    public static string Name(string label, bool required) => required ? $"{label}, required" : label;

    /// <summary>The help text: the error first, then the description, so what is wrong is read before what is nice to know.</summary>
    public static string Help(string? error, string? description) =>
        string.Join(" ", new[] { error, description }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>The index an arrow key moves a choice to: it stops at either end and never wraps. With nothing chosen, Down picks the first and Up the last.</summary>
    public static int Move(int index, int count, int delta)
    {
        if (count <= 0) return -1;
        if (index < 0) return delta >= 0 ? 0 : count - 1;
        return Math.Clamp(index + delta, 0, count - 1);
    }

    /// <summary>What the closed Select shows: the chosen option's words, else the placeholder.</summary>
    public static string Shown(string? chosen, string? placeholder) => !string.IsNullOrEmpty(chosen) ? chosen : placeholder ?? "";
}
