<!-- ourostack:brand:start -->
<p align="center">
  <a href="https://github.com/ourostack"><img src="https://raw.githubusercontent.com/ourostack/.github/main/brand/headers/ouro-ui-windows.webp" alt="Ouro basking on a cabin windowsill while a small robot swings open one pane of the window and a person at the desk points at it" width="100%"></a>
</p>
<!-- ourostack:brand:end -->

# Ouro UI for Windows

Ouro UI's native kit for Windows: core's components as WinUI 3 controls, and the agent doors (Windows App Actions and an MCP server for the On-device Agent Registry), all generated from [ouro-ui-core](https://github.com/ourostack/ouro-ui-core)'s one `defineAction` declaration.

**Status: v0.7.0.** Sixteen components (Button, Badge, Card, Item, Checkbox, Switch, TextField, Separator, DescriptionList, Tabs, TabBar, Sidebar, ItemGroup, Dialog, Sheet, ConfirmDialog), the agent side, and agent cards drawn natively, as Ouro's composed tree or as A2UI messages. The plan is in [the decision record](https://github.com/ourostack/ouro-ui-core/blob/main/docs/decisions/2026-11-14-native-kits.md)'s "Windows v0.1.0" section.

## Agent cards

An agent can put a card on a person's screen: a recipe, a shopping list, a form with buttons. Windows draws two formats with one control, `OuroComposedSurface`, and both are limited to your catalog: every button is one of your declared actions, with its label, consent class and confirm sheet taken from the catalog, never from the agent.

| Format | Where it is in an MCP tool result | Draw it |
| --- | --- | --- |
| Ouro's composed tree, the source of truth | `structuredContent.ouro` (core's `mcpAppTreeResult`), or `structuredContent.ouro.update` for a change to the card shown (`mcpAppTreeUpdateResult`) | `surface.Show(catalog, OuroComposedResult.From(toolResult)!)` |
| [A2UI](https://a2ui.org/) v1.0 messages (`createSurface`, `updateComponents`, `updateDataModel`, `deleteSurface`) | `structuredContent.a2ui.messages` (core's A2UI `mcpAppToolResult`) | `OuroA2UI.TryReadToolResult(toolResult, out var messages)`, then `surface.ShowA2UI(catalog, messages)` |

One catalog serves both: generate it from your actions with core's `ouro-a2ui-catalog.mts catalog`. A2UI is an adapter onto Ouro's tree, as on web and Apple ([core's decision](https://github.com/ourostack/ouro-ui-core/blob/main/docs/decisions/2026-11-14-composed-ui-own-tree.md)): `OuroA2UI` (in `Ouro.Actions`) is core's `a2ui.mts` in C#. It validates the messages, resolves them into the composed tree and its data model, and the tree must then pass the same checks as one the agent sends itself.

```csharp
var catalog = OuroComposedCatalog.Load(JsonNode.Parse(File.ReadAllText("catalog.json")));
var surface = new OuroComposedSurface
{
    OnPress = (press, _) => runtime.InvokeAsync(new OuroCall(press.Action, press.Input, OuroDoor.Person) { Caller = "person" }),
    // A card that came as A2UI sends each press as A2UI's action message instead.
    OnA2UIAction = (message, _) => runtime.InvokeAsync(new OuroCall(message["action"]!["name"]!.GetValue<string>(), (JsonObject)message["action"]!["context"]!.DeepClone(), OuroDoor.Person) { Caller = "person" }),
    OnHandoff = handoff => OpenScreenFor(handoff.Action),
};
if (OuroA2UI.TryReadToolResult(toolResult, out var messages))
    agent.Tell(surface.ShowA2UI(catalog, messages));   // empty, or A2UI's validation errors, for the agent to correct
else
    agent.Tell(surface.Show(catalog, OuroComposedResult.From(toolResult)!));
```

A tree or a message stream that fails the checks shows one short alert, "This card can't be shown", in place of the card, and returns the problems. A2UI's come in A2UI's own error format (`VALIDATION_FAILED` and the rest, with a JSON pointer into the stream), word for word as core writes them. CI runs core's 13 A2UI conformance cases and Ouro Shop's card in both formats (How it's checked, below).

What's still missing:

- **One A2UI surface per control.** `ShowA2UI` draws the first surface the messages leave, or the one you name; a stream that makes several needs one `OuroComposedSurface` each.
- **The whole stream each time.** Pass every message so far, again as more arrive. While the surface is the one the same `createSurface` made, what the person entered stays.
- **No A2UI functions, themes or other catalogs.** As in core: `callRendererFunction` and `agentFunctionResponse` are refused (agents act through their own doors), a `theme` is refused (your HX theme styles the card), and `catalogId` must be your app's catalog, so A2UI's basic catalog isn't drawn.
- **Only `@accessibility.label` is read aloud.** `description` and `hidden` are checked but not drawn, as in core's resolver.
- **Limits core doesn't have yet.** A surface's tree may go at most 24 components deep, resolve to at most 5,000 nodes, and copy at most a million characters out of its data (`OuroA2UI.MaxDepth`, `MaxNodes`, `MaxSize`), as on Android. A stream names children by id, so nothing else stops a long chain from exhausting the stack, or shared children and templates from multiplying a large data model past what memory holds; past any of them, the stream is refused like any other.
- **Two edges differ from JavaScript.** A component id's letters are checked by Unicode category, so a rare letter .NET and a browser's engine classify differently could pass on one and fail on the other. A data write past the end of a list writes nothing here; in JavaScript it leaves a gap.

## What's here

| Package | What it is |
| --- | --- |
| `Ouro.Actions` (`src/Ouro.Actions`, plain .NET) | The runtime every door goes through: input checked against the schema, idempotency keys, an audit of every call, a yes from the person before a consequential action an agent prepared, and core's typed refusal for a person-only action at every agent door. Also the App Action URI reader, the MCP server, Ouro's composed tree (`OuroComposed`: core's `composed.mts` in C#, which validates an agent's tree against the app's catalog) and the A2UI adapter (`OuroA2UI`: core's `a2ui.mts` in C#). |
| `Ouro.UI.Windows` (`src/Ouro.UI.Windows`, WinUI 3) | `OuroComposedSurface`, which draws an agent's card, as a composed tree or as A2UI messages; and `OuroButton`, `OuroBadge`, `OuroCard`, `OuroItem`, `OuroCheckbox`, `OuroSwitch`, `OuroTextField`, `OuroSeparator`, `OuroDescriptionList`, `OuroTabs`, `OuroTabBar`, `OuroSidebar`, `OuroItemGroup`, `OuroDialog`, `OuroSheet` and `OuroConfirmDialog`, from core's specs (`vendor/core/components`) and tokens. Each follows light and dark mode, a Windows contrast theme, the text size and the animation setting. An `OuroButton` with an `Action` runs it through `Ouro.Actions`. |
| Ouro Shop (`samples/OuroShop`) | A shopping list whose buttons people press and agents call, with a gallery of every tone and variant, and of the fields and lists, and a card its agent composed (`--page composed`), and the same card arriving as A2UI messages (`--page a2ui`). The MSIX registers its App Actions and its MCP server. |

## Use it

Each release publishes the packages to GitHub Packages, ourostack's NuGet feed. GitHub's NuGet feed needs a sign-in even for public packages: a token with `read:packages` (for example `gh auth token`, after `gh auth refresh -s read:packages`).

```sh
dotnet nuget add source https://nuget.pkg.github.com/ourostack/index.json --name ourostack --username <your GitHub login> --password <token> --store-password-in-clear-text
dotnet add package Ouro.UI.Windows --source ourostack
```

The `.nupkg` files are also attached to each [release](https://github.com/ourostack/ouro-ui-windows/releases), for a local feed with no sign-in. nuget.org comes later (RELEASING.md).

From v0.4.1 on, [the release workflow](.github/workflows/release.yml) gives each file it attaches a GitHub build attestation, so you can check that a package was built from its tag: `gh attestation verify Ouro.UI.Windows.0.4.1.nupkg --repo ourostack/ouro-ui-windows`.

Declare actions once in TypeScript with core's `defineAction` and generate the Windows files:

```sh
node vendor/core/ouro-actions-windows.mts generate my.actions.mts Generated
```

That writes `OuroActions.g.cs` (the catalog), `app-actions.json` (App Actions), `mcpb.manifest.json` (the MCP bundle for the Agent Registry) and `doors.json` (which door carries which action, and why not). Then:

```csharp
var runtime = new OuroActionRuntime(OuroActions.Catalog, MyHandler.Handle, new OuroJsonlAudit(auditPath))
{
    Confirm = OuroPerson.Dialog(() => window.Content.XamlRoot),
};
var add = new OuroButton { Action = OuroActions.AddShoppingListItem, Runtime = runtime, Input = () => new() { ["name"] = field.Text } };
```

### Draw an agent's card: Ouro's composed tree

Windows draws Ouro's composed tree natively, as web and Apple do: the interface an agent composes for a person, as validated data, limited to your catalog, where every button is one of your declared actions with its consent class. Generate your catalog from your actions with core's `ouro-a2ui-catalog.mts catalog`. The agent sends the tree in an MCP tool result's `structuredContent.ouro` (core's `mcpAppTreeResult`). `OuroComposedSurface` draws it only once it validates against that catalog:

```csharp
var catalog = OuroComposedCatalog.Load(JsonNode.Parse(File.ReadAllText("catalog.json")));
var surface = new OuroComposedSurface
{
    OnPress = (press, _) => runtime.InvokeAsync(new OuroCall(press.Action, press.Input, OuroDoor.Person) { Caller = "person" }),
    OnHandoff = handoff => OpenScreenFor(handoff.Action),
};
var problems = surface.Show(catalog, OuroComposedResult.From(toolResult)!);   // empty, or why nothing was drawn
```

- Read and write buttons press at once.
- A consequential button opens a dialog with each prepared value under its label, and the id beside a name. Only an id field shows a name; every other row shows exactly what will be sent, and the surface refuses a tree whose sheet doesn't, or whose button carries an input the catalog doesn't declare. It presses only when the person confirms; Cancel is the default.
- A person-only button calls `OnHandoff` and never presses.
- Fields (TextField, Checkbox, Select) start from the tree's data model and write the person's answers back. A button input bound to a field (`{"@path": "/x"}`) sends what they entered.
- Every component is drawn with Ouro HX's look and follows light, dark, a contrast theme, the text size and the animation setting. TextField, Checkbox, Separator, DescriptionList, Card, Badge and Button are the kit's own controls. Heading, Text, Image, List, Alert, Stack and Select are drawn in the surface from core's tokens. UI Automation reads headings with their level, images by their description and lists as lists.

To change a card it already showed, the agent sends only what changed: an update `{ nodes?, data? }` in `structuredContent.ouro.update` (core's `mcpAppTreeUpdateResult`). `surface.Show(catalog, OuroComposedResult.From(toolResult)!)` applies it to the card drawn now, and `surface.Update(update)` does the same from code.

- Each node replaces the card's one node with its id. Each `{ path, value }` sets the data model at a JSON pointer, and `null` deletes.
- It applies all or nothing. An id the card lacks, an id a template's instances share, an unsafe path, or a result that fails the checks gives the same short alert as an invalid tree. The last card stays hidden with what the person entered, so a corrected update applies to it.
- What the person entered stays, except where the update set the value or changed the list item it sits in.

`OuroComposed` in `Ouro.Actions` is core's `composed.mts` in C#: `ValidateTree`, `ActionButtonProps`, `ResolveButton`, `PersonPress`, `ApplyTreeUpdate`, `KeepAnswers` and JSON Pointer reads and writes (`OuroPointer`), for code off Windows too.

Its limits:

- **A2UI** has its own limits, in Agent cards above.
- **The surface is the person's.** It has no way for an agent to press a button; agents run actions through their own doors (App Actions, the MCP server). Like any Windows button, UI Automation can still press one, as with person-only buttons below.
- **Pictures load only from `https` URLs**, so an agent's card never shows a file from the person's machine.
- **No pointer names `__proto__`, `constructor` or `prototype`.** C#'s JSON objects have no prototype, but the surface refuses such a field or bound input, and `OuroPointer.Set` writes nothing through one, so Windows refuses the same trees every other kit does.

An unpackaged app needs `<EnableMsixTooling>true</EnableMsixTooling>` in its project so the build writes its `resources.pri`; without it the controls' templates don't load and they draw nothing, with no error.

The MCP server is three lines (`samples/OuroShop/OuroShop.Mcp/Program.cs`). An App Action launch is `OuroAppActionUri.Read(catalog, uri)` passed to the same runtime.

## How it's checked

CI runs on the free `windows-latest` runner (`.github/workflows/ci.yml`):

- `actions` (Ubuntu): the runtime and both doors; the A2UI adapter against core's 13 A2UI conformance cases, each giving exactly its expected trees or problems; the composed tree against core's conformance cases, which every valid tree must pass and every forged one (a relabelled button, a loosened consent, a dropped confirm sheet, input on a person-only action, an unknown component) must fail, and core's tree update cases, each giving its tree and data or its problems; and the colour rules over every theme core ships: every tone and variant reads at 4.5:1 or better in all 6 themes, light and dark.
- `windows`: builds, packages and installs the MSIX, then reads the window through UI Automation in light, dark, a contrast theme, with animations off and with text at 225%. It checks every claim core makes for each component (`tools/claims.mts`), draws Ouro Shop's composed card and presses it as a person would (`tools/composed.ps1` and `tools/composed.mts`: what UI Automation reads for every component, fields writing the data model, a run, a confirm and its Cancel, a handoff that sends nothing, and the agent's updates: one that keeps what the person typed, one refused with the short alert, and the good one again), then draws the same card from A2UI messages and checks that UI Automation reads it exactly as the composed card and that its presses send A2UI's action message (`tools/a2ui.ps1`), samples pixels against what each control reports it drew, runs the X-ray (`tools/xray.mts`) that agents and people see the same actions, answers the MCP server's confirmation dialog, and measures launch time, memory and press latency. The run's `windows-evidence` artifact holds the screenshots and tables.

**What the free runner can't show.** It is Windows Server: no App Actions runtime, no On-device Agent Registry and no Mica. CI proves the package registers both doors and that they behave; it can't prove Windows lists them to agents. A Windows 11 client check (build 26220.7262 or later) would add the system App Actions catalog, `odr.exe mcp list`, an agent calling through the registry, and Mica in the screenshots.

## Where Windows differs from the spec

- Card and Item call core's `size` `Density` (Medium, Small), and their `Variant` is an `OuroFrame` (Outline, Plain), so the names stay clear of WinUI's own `Frame` control.
- Under a contrast theme the controls draw the person's system colours, not the theme's (as Windows asks). Status tones then look alike, so their meaning must also be in words.
- A busy Button is disabled while its action runs, so it leaves the tab order until the action finishes.
- TextField: no password type, Clear button or large size yet. `IsMultiline` (several lines) and the `Number` type are Windows' own, for the composed tree's fields.
- Switch: a Button with the Toggle pattern, as WinUI's ToggleSwitch is. Its thumb stretches while pressed, never with Windows animations off. Checkbox: mixed is WinUI's `IsChecked = null`. Separator: the default tone, Secondary, draws the border colour; another tone colours the line and its label.
- DescriptionList: details are words, not views.
- Tabs: a templated control that follows WinUI's TabView and Pivot semantics (a Tab of TabItems), with no close or add buttons. Every tab is at least 44 epx tall, not the spec's 36 for a mouse, because the claim check runs with a mouse and holds every control to 44.
- TabBar: a bottom bar is a phone pattern, so on a desktop use a sidebar; the bar is a List of ListItems with SelectionItem, as the spec says. Its items are at least 64 epx tall, and the accessory is a 48 epx circle.
- Sidebar: built on NavigationView's meaning, with the 64 epx rail as its compact pane; the list is a List of ListItems with SelectionItem, a heading is a Header item and a group has ExpandCollapse, as the spec says. Every row is at least 44 epx tall, not the spec's 36, because the touch claim holds each control to 44.
- ItemGroup: a List named by its label with a ListItem per row, so a screen reader reads each row once; an ordered group adds each row's place ("Lemon orzo, 2 of 3"). With no rows and an empty message, the message replaces the list and is a Text. Focus stays on the same control when the rows are rebuilt, and moves to the row that took the place of a removed one.
- Person-only actions: UI Automation can press their buttons. Requiring Windows Hello first is built and off; [docs/person-check.md](docs/person-check.md) is the open decision.

## How the kits fit together

- **Web** (`ourostack/ouro-ui`) is the build-once layer that runs everywhere.
- **Native** is one kit per platform in that platform's own toolkit: SwiftUI on Apple (`ourostack/ouro-ui-apple`), this kit on Windows, Compose on Android.
- The kits share **only the core**: tokens, `defineAction` with its consent classes, the claims and the component specs. They never share rendering.

## License

MIT
