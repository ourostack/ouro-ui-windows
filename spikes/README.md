# Spikes

Measured experiments behind the native-strategy decision. Nothing here is the kit; it is what the kit's design rests on.

The slice, on a free `windows-latest` runner (`.github/workflows/ci.yml`, job `windows spike`):

1. **One declaration, three doors.** `shared/shopping.actions.mts` declares two actions with core's `defineAction`: `add_shopping_list_item` (write) and `place_grocery_order` (person-only). `shared/windows-actions.mts` generates core's files plus the Windows ones: the App Actions registration, the MCP bundle manifest for the On-device Agent Registry, and `OuroActions.g.cs`. The WinUI 3 window draws one Ouro button per declared action; the MSIX registers the App Action and the MCP server.
2. **Person-only stays with the person.** Every agent door (MCP `tools/call`, an App Action launch) answers core's typed refusal; only the button runs it.
3. **The X-ray agrees** (`shared/xray.mts`): what the window's UI Automation tree shows people, what the live MCP server lists, and what the package registered as App Actions must match the declarations.
4. **Core's Button claims pass on Windows** (`shared/button-claims.mts`), under the real Windows settings each claim names.
5. **Feel is measured** (`tools/measure.ps1`): launch, memory and press latency for WinUI 3 (unpackaged and MSIX) and the runner-up, WPF with its Fluent theme.

Evidence (screenshots, UI Automation trees, tables) is the run's `windows-spike-evidence` artifact.
