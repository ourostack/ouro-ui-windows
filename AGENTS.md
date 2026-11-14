# Working in ouro-ui-windows

- Trunk-based: `main` is the only long-lived branch. Small pull requests, auto-merge on, merged green within about 30 minutes.
- Commit as `16390116+arimendelow@users.noreply.github.com`. No AI attribution anywhere: no `Co-Authored-By` lines, no "Generated with" footers.
- What comes from core is vendored by release tag and SHA-256 and never edited. Change core, then bump the pin.
- Generated files are committed and checked in CI; never edit them by hand.
- CI runs only on free GitHub-hosted runners (`windows-latest`, `ubuntu-latest`).
