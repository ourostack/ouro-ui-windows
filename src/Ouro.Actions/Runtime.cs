// The one entry point every door goes through: a person's press, an MCP call and an App Action launch. It holds the
// runtime contract from the 2026-11-14 native-kits decision, once, so no door can skip a step:
//   1. the action is declared;
//   2. a person-only action is refused at every agent door with core's typed refusal, byte for byte, and at the
//      person's door asks the optional person check (Windows Hello; off unless the app turns it on);
//   3. the input fits the action's schema, after the door's own decoding;
//   4. a call with an idempotency key that was seen before answers what it answered then, without running again;
//   5. a consequential action from an agent door runs only after the person says yes;
//   6. the handler runs, with the caller's cancellation;
//   7. every call is recorded in the audit, whatever its outcome.
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Ouro.Actions;

/// <summary>How a call ended.</summary>
public enum OuroOutcomeState
{
    /// <summary>The handler ran; <see cref="OuroOutcome.Body"/> is its output.</summary>
    Done,
    /// <summary>Not run: a person-only action at an agent door, a failed person check, or no one to confirm.</summary>
    Refused,
    /// <summary>Not run: the person said no to a consequential action.</summary>
    Declined,
    /// <summary>Not run: an undeclared action, input that doesn't fit, or an idempotency key reused with other input.</summary>
    Invalid,
    /// <summary>The handler threw.</summary>
    Failed,
    /// <summary>The caller cancelled it.</summary>
    Cancelled,
}

/// <summary>What a call answered. For anything but <see cref="OuroOutcomeState.Done"/>, the body is an outcome/1 with an error.</summary>
public sealed record OuroOutcome(OuroOutcomeState State, JsonObject Body, bool Replayed = false)
{
    /// <summary>The error code, when it didn't run.</summary>
    public string? Code => Body["error"]?["code"]?.GetValue<string>();

    /// <summary>One sentence for a person: the refusal's or error's summary, or that it was done.</summary>
    public string Summary => Body["summary"]?.GetValue<string>() ?? (State == OuroOutcomeState.Done ? "Done." : State.ToString());
}

/// <summary>One call through a door.</summary>
/// <param name="ActionId">The action's id.</param>
/// <param name="Input">The input, already decoded by the door.</param>
/// <param name="Door">Which door it came through.</param>
public sealed record OuroCall(string ActionId, JsonObject Input, OuroDoor Door)
{
    /// <summary>Who called, as the door knows it: an MCP client's name, "app-action", "person".</summary>
    public string? Caller { get; init; }

    /// <summary>A key the caller sends again when it retries this exact call. The input's own <c>idempotencyKey</c> counts too.</summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>The app's one handler: runs a declared action and returns its output.</summary>
public delegate Task<JsonObject> OuroHandler(OuroActionInfo action, JsonObject input, CancellationToken cancellation);

/// <summary>Asks the person whether a consequential action an agent prepared may run. True means yes.</summary>
public delegate Task<bool> OuroConfirm(OuroActionInfo action, JsonObject input, OuroCall call, CancellationToken cancellation);

/// <summary>Asks the operating system to verify that the person is there, before a person-only action runs.</summary>
public delegate Task<OuroPersonCheckResult> OuroPersonCheck(OuroActionInfo action, CancellationToken cancellation);

/// <summary>What a person check found. <paramref name="Why"/> says, to the person, why it wasn't verified.</summary>
public sealed record OuroPersonCheckResult(bool Verified, string? Why = null);

/// <summary>One call, as the audit records it. No input values are recorded, only their names.</summary>
public sealed record OuroAuditEntry(DateTimeOffset At, string Action, OuroDoor Door, string? Caller, string? IdempotencyKey, OuroOutcomeState State, string? Code, bool Replayed, IReadOnlyList<string> InputNames);

/// <summary>Where the audit goes.</summary>
public interface IOuroAudit
{
    void Record(OuroAuditEntry entry);
}

/// <summary>The audit in memory: the last <c>capacity</c> calls.</summary>
public sealed class OuroMemoryAudit(int capacity = 1000) : IOuroAudit
{
    private readonly ConcurrentQueue<OuroAuditEntry> entries = new();

    public IReadOnlyList<OuroAuditEntry> Entries => entries.ToArray();

    public void Record(OuroAuditEntry entry)
    {
        entries.Enqueue(entry);
        while (entries.Count > capacity) entries.TryDequeue(out _);
    }
}

/// <summary>The audit as JSON lines in a file, such as the app's local data folder.</summary>
public sealed class OuroJsonlAudit(string path) : IOuroAudit
{
    private readonly object gate = new();

    public void Record(OuroAuditEntry e)
    {
        var line = new JsonObject
        {
            ["at"] = e.At.ToString("O"),
            ["action"] = e.Action,
            ["door"] = e.Door.ToString(),
            ["caller"] = e.Caller,
            ["idempotencyKey"] = e.IdempotencyKey,
            ["state"] = e.State.ToString(),
            ["code"] = e.Code,
            ["replayed"] = e.Replayed,
            ["inputNames"] = new JsonArray(e.InputNames.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()),
        }.ToJsonString(OuroJson.Options);
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, line + "\n");
        }
    }
}

/// <summary>Runs an app's declared actions through the runtime contract. One per process; every door shares it.</summary>
public sealed class OuroActionRuntime(OuroActionCatalog catalog, OuroHandler handler, IOuroAudit? audit = null)
{
    private readonly ConcurrentDictionary<(string Action, string Key), (string Input, OuroOutcome Outcome, DateTimeOffset At)> seen = new();

    public OuroActionCatalog Catalog { get; } = catalog;

    /// <summary>The audit every call is recorded in. In memory unless the app gives another.</summary>
    public IOuroAudit Audit { get; } = audit ?? new OuroMemoryAudit();

    /// <summary>
    /// Asks the person about a consequential action from an agent door. Without one, such a call is refused with
    /// code <c>needs-confirmation</c>: nothing consequential runs that no person agreed to.
    /// </summary>
    public OuroConfirm? Confirm { get; set; }

    /// <summary>
    /// Verifies the person before a person-only action runs from their own press. Off (null) by default: whether
    /// "person-only" requires proof of the person is Ari's decision, recorded in docs/person-check.md.
    /// </summary>
    public OuroPersonCheck? PersonCheck { get; set; }

    /// <summary>How long an idempotency key is remembered.</summary>
    public TimeSpan IdempotencyWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The clock, for tests.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Runs one call through the contract; never throws for the caller's mistakes, only answers.</summary>
    public async Task<OuroOutcome> InvokeAsync(OuroCall call, CancellationToken cancellation = default)
    {
        var action = Catalog.Find(call.ActionId);
        var key = call.IdempotencyKey ?? (call.Input["idempotencyKey"] is JsonValue k && k.TryGetValue<string>(out var s) ? s : null);
        OuroOutcome outcome;
        var replayed = false;
        if (action is null)
        {
            outcome = Problem(OuroOutcomeState.Invalid, call.ActionId, "unknown-action", $"{call.ActionId}: no action has this id. Fix: call one of {string.Join(", ", Catalog.Actions.Where(a => call.Door == OuroDoor.Person || a.AgentsMayCall).Select(a => a.Id))}.");
        }
        else
        {
            (outcome, replayed) = await RunAsync(action, call, key, cancellation).ConfigureAwait(false);
        }
        Audit.Record(new OuroAuditEntry(Now(), call.ActionId, call.Door, call.Caller, key, outcome.State, outcome.Code, replayed, call.Input.Select(p => p.Key).ToArray()));
        return replayed ? outcome with { Replayed = true } : outcome;
    }

    private async Task<(OuroOutcome, bool)> RunAsync(OuroActionInfo action, OuroCall call, string? key, CancellationToken cancellation)
    {
        if (!action.AgentsMayCall && call.Door != OuroDoor.Person)
            return (new OuroOutcome(OuroOutcomeState.Refused, JsonNode.Parse(action.RefusalJson!)!.AsObject()), false);

        var problems = OuroValidation.Problems(action.InputSchema, call.Input);
        if (problems.Count > 0)
        {
            var body = Problem(OuroOutcomeState.Invalid, action.Id, "invalid-input", $"{action.Id}: the input doesn't fit. {string.Join(" ", problems)}").Body;
            body["error"]!["problems"] = new JsonArray(problems.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
            return (new OuroOutcome(OuroOutcomeState.Invalid, body), false);
        }

        var input = OuroJson.Write(call.Input);
        if (key is not null)
        {
            Forget();
            if (seen.TryGetValue((action.Id, key), out var before))
                return before.Input == input
                    ? (before.Outcome, true)
                    : (Problem(OuroOutcomeState.Invalid, action.Id, "idempotency-conflict", $"{action.Id}: idempotency key \"{key}\" was used with other input. Fix: send a new key for a new call, or the same input to retry."), false);
        }

        if (action.Consent == OuroConsent.PersonOnly && PersonCheck is { } check)
        {
            var verified = await check(action, cancellation).ConfigureAwait(false);
            if (!verified.Verified)
                return (Problem(OuroOutcomeState.Refused, action.Id, "person-not-verified", $"{action.Label}: Windows couldn't verify it's you, so nothing happened. {verified.Why}".TrimEnd()), false);
        }

        if (action.Consent == OuroConsent.Consequential && call.Door != OuroDoor.Person)
        {
            if (Confirm is null)
                return (Problem(OuroOutcomeState.Refused, action.Id, "needs-confirmation", $"{action.Id}: it reaches other people, so the person says yes first, and no one can be asked here. Fix: ask the person to do it in the app."), false);
            if (!await Confirm(action, call.Input, call, cancellation).ConfigureAwait(false))
                return (Problem(OuroOutcomeState.Declined, action.Id, "declined", $"{action.Label}: the person said no, so nothing happened."), false);
        }

        OuroOutcome outcome;
        try
        {
            var output = await handler(action, (JsonObject)call.Input.DeepClone(), cancellation).ConfigureAwait(false);
            outcome = new OuroOutcome(OuroOutcomeState.Done, output);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return (Problem(OuroOutcomeState.Cancelled, action.Id, "cancelled", $"{action.Id}: cancelled before it finished."), false);
        }
        catch (Exception e)
        {
            outcome = Problem(OuroOutcomeState.Failed, action.Id, "failed", $"{action.Id}: {e.Message}");
        }
        if (key is not null) seen[(action.Id, key)] = (input, outcome, Now());
        return (outcome, false);
    }

    private void Forget()
    {
        var oldest = Now() - IdempotencyWindow;
        foreach (var (k, v) in seen) if (v.At < oldest) seen.TryRemove(k, out _);
    }

    /// <summary>An outcome/1 for a call that didn't run, in the refusal's shape, so agents read every door's answers the same way.</summary>
    public static OuroOutcome Problem(OuroOutcomeState state, string action, string code, string message) => new(state, new JsonObject
    {
        ["ax"] = "outcome/1",
        ["action"] = action,
        ["state"] = state.ToString().ToLowerInvariant(),
        ["summary"] = message,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    });
}
