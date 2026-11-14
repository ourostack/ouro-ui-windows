// The person's answers the runtime asks for: a yes or no before a consequential action an agent prepared, and the
// optional Windows Hello check before a person-only action. Both are off unless the app sets them on its runtime.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ouro.Actions;
using Windows.Security.Credentials.UI;

namespace Ouro.UI.Windows;

/// <summary>Ready-made <see cref="OuroConfirm"/> and <see cref="OuroPersonCheck"/> for WinUI apps.</summary>
public static class OuroPerson
{
    /// <summary>
    /// Asks in the app with a ContentDialog: the action's label as the title, what the agent asked for, and Yes or No,
    /// with No as the default button so Enter never agrees by accident.
    /// </summary>
    public static OuroConfirm Dialog(Func<XamlRoot?> root) => async (action, input, call, cancellation) =>
    {
        if (root() is not { } xamlRoot) return false;
        var details = string.Join(", ", input.Where(p => p.Key != "idempotencyKey").Select(p => $"{p.Key}: {p.Value}"));
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = action.Label,
            Content = $"{(call.Caller is { Length: > 0 } c ? c : "An agent")} asks to {action.Description.TrimEnd('.').ToLowerInvariant()}{(details.Length > 0 ? $" ({details})" : "")}. Do you want this?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Close,
        };
        using var _ = cancellation.Register(dialog.Hide);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    };

    /// <summary>
    /// Windows Hello before a person-only action: a face, a fingerprint or the PIN, in a prompt owned by the app's
    /// window. Off by default: whether person-only requires proof of the person is a decision (docs/person-check.md).
    /// Turn it on with <c>runtime.PersonCheck = OuroPerson.WindowsHello(() => hwnd)</c>.
    /// </summary>
    public static OuroPersonCheck WindowsHello(Func<nint> window) => async (action, _) =>
    {
        var available = await UserConsentVerifier.CheckAvailabilityAsync();
        if (available != UserConsentVerifierAvailability.Available)
            return new OuroPersonCheckResult(false, $"Windows Hello isn't ready on this PC ({available}). Fix: set it up in Settings, Accounts, Sign-in options.");
        var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(window(), $"{action.Label}: confirm it's you.");
        return result == UserConsentVerificationResult.Verified
            ? new OuroPersonCheckResult(true)
            : new OuroPersonCheckResult(false, $"Windows Hello answered {result}.");
    };
}
