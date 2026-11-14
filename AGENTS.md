# Working in ouro-ui-windows

- Trunk-based: `main` is the only long-lived branch. Small pull requests, auto-merge on, merged green within about 30 minutes.
- Commit as `16390116+arimendelow@users.noreply.github.com`. No AI attribution anywhere: no `Co-Authored-By` lines, no "Generated with" footers.
- What comes from core is vendored by release tag and SHA-256 and never edited. Change core, then bump the pin.
- Generated files are committed and checked in CI; never edit them by hand.
- CI runs only on free GitHub-hosted runners (`windows-latest`, `ubuntu-latest`).
- Builds and UI checks run on GitHub's `windows-latest`, not locally: push and read the `windows` job's step summary and its `windows-evidence` artifact.
- One colour path: `src/Ouro.UI.Windows/OuroPaint.cs` decides every fill, label and edge; controls never pick colours themselves. Under a contrast theme they draw the system's colours (`OuroUI.SystemPaint`).
- A new component: vendor its spec from core (`scripts/provenance.mjs`), add the control with an automation peer that says what its spec's `accessibility.windows` says, report through `OuroUI.Report`, add it to the Ouro Shop gallery, and add a `spoken` and `pairs` entry in `tools/claims.mts`. CI fails until every claim the spec names passes.
- In code under `namespace Ouro.UI.Windows`, never write a `Windows.`-qualified name: it resolves to `Ouro.UI.Windows`. Use a `using` at the top of the file, or `global::Windows.…`.
- Person-only stays with the person: no door may run a person-only action for an agent. Turning on the person check (`docs/person-check.md`) is Ari's decision.
