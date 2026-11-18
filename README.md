<!-- ourostack:brand:start -->
<p align="center">
  <a href="https://github.com/ourostack"><img src="https://raw.githubusercontent.com/ourostack/.github/main/brand/headers/ouro-ui-windows.webp" alt="Ouro basking on a cabin windowsill while a small robot swings open one pane of the window and a person at the desk points at it" width="100%"></a>
</p>
<!-- ourostack:brand:end -->

# Ouro UI for Windows

Ouro UI's native kit for Windows: core's components as WinUI 3 controls, and the agent doors (Windows App Actions and an MCP server for the On-device Agent Registry), all generated from [ouro-ui-core](https://github.com/ourostack/ouro-ui-core)'s one `defineAction` declaration.

**Status: v0.1.0.** Four components (Button, Badge, Card, Item) and the agent side. The plan is in [the decision record](https://github.com/ourostack/ouro-ui-core/blob/main/docs/decisions/2026-11-14-native-kits.md)'s "Windows v0.1.0" section.

## What's here

| Package | What it is |
| --- | --- |
| `Ouro.Actions` (`src/Ouro.Actions`, plain .NET) | The runtime every door goes through: input checked against the schema, idempotency keys, an audit of every call, a yes from the person before a consequential action an agent prepared, and core's typed refusal for a person-only action at every agent door. Also the App Action URI reader, the MCP server, and Ouro's composed tree (`OuroComposed`: core's `composed.mts` in C#, which validates an agent's tree against the app's catalog). |
| `Ouro.UI.Windows` (`src/Ouro.UI.Windows`, WinUI 3) | `OuroButton`, `OuroBadge`, `OuroCard` and `OuroItem`, from core's specs (`vendor/core/components`) and tokens. Each follows light and dark mode, a Windows contrast theme, the text size and the animation setting. An `OuroButton` with an `Action` runs it through `Ouro.Actions`. |
| Ouro Shop (`samples/OuroShop`) | A shopping list whose buttons people press and agents call, with a gallery of every tone and variant. The MSIX registers its App Actions and its MCP server. |

## Use it

Each release publishes the packages to GitHub Packages, ourostack's NuGet feed. GitHub's NuGet feed needs a sign-in even for public packages: a token with `read:packages` (for example `gh auth token`, after `gh auth refresh -s read:packages`).

```sh
dotnet nuget add source https://nuget.pkg.github.com/ourostack/index.json --name ourostack --username <your GitHub login> --password <token> --store-password-in-clear-text
dotnet add package Ouro.UI.Windows --source ourostack
```

The `.nupkg` files are also attached to each [release](https://github.com/ourostack/ouro-ui-windows/releases), for a local feed with no sign-in. nuget.org comes later (RELEASING.md).

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

An unpackaged app needs `<EnableMsixTooling>true</EnableMsixTooling>` in its project so the build writes its `resources.pri`; without it the controls' templates don't load and they draw nothing, with no error.

The MCP server is three lines (`samples/OuroShop/OuroShop.Mcp/Program.cs`). An App Action launch is `OuroAppActionUri.Read(catalog, uri)` passed to the same runtime.

## How it's checked

CI runs on the free `windows-latest` runner (`.github/workflows/ci.yml`):

- `actions` (Ubuntu): the runtime and both doors; the composed tree against core's conformance cases, which every valid tree must pass and every forged one (a relabelled button, a loosened consent, a dropped confirm sheet, input on a person-only action, an unknown component) must fail; and the colour rules over every theme core ships: every tone and variant reads at 4.5:1 or better in all 6 themes, light and dark.
- `windows`: builds, packages and installs the MSIX, then reads the window through UI Automation in light, dark, a contrast theme, with animations off and with text at 225%. It checks every claim core makes for each component (`tools/claims.mts`), samples pixels against what each control reports it drew, runs the X-ray (`tools/xray.mts`) that agents and people see the same actions, answers the MCP server's confirmation dialog, and measures launch time, memory and press latency. The run's `windows-evidence` artifact holds the screenshots and tables.

**What the free runner can't show.** It is Windows Server: no App Actions runtime, no On-device Agent Registry and no Mica. CI proves the package registers both doors and that they behave; it can't prove Windows lists them to agents. A Windows 11 client check (build 26220.7262 or later) would add the system App Actions catalog, `odr.exe mcp list`, an agent calling through the registry, and Mica in the screenshots.

## Where Windows differs from the spec

- Card and Item call core's `size` `Density` (Medium, Small), and their `Variant` is an `OuroFrame` (Outline, Plain), so the names stay clear of WinUI's own `Frame` control.
- Under a contrast theme the controls draw the person's system colours, not the theme's (as Windows asks). Status tones then look alike, so their meaning must also be in words.
- A busy Button is disabled while its action runs, so it leaves the tab order until the action finishes.
- Person-only actions: UI Automation can press their buttons. Requiring Windows Hello first is built and off; [docs/person-check.md](docs/person-check.md) is the open decision.

## How the kits fit together

- **Web** (`ourostack/ouro-ui`) is the build-once layer that runs everywhere.
- **Native** is one kit per platform in that platform's own toolkit: SwiftUI on Apple (`ourostack/ouro-ui-apple`), this kit on Windows, Compose on Android.
- The kits share **only the core**: tokens, `defineAction` with its consent classes, the claims and the component specs. They never share rendering.

## License

MIT
