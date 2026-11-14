# Releasing

Releases are tags `vX.Y.Z` on `main`, published as GitHub releases by `.github/workflows/release.yml`.

1. Merge everything for the release through pull requests, and wait for `main`'s CI to pass, `windows` included.
   That job is the proof: X-ray, every component's claims in light, dark, a contrast theme, animations off and text at
   225%, and pixels against self-reports.
2. Set `<Version>` in `Directory.Build.props` and the sample's `AppxManifest.xml` `Version` to the new version, in a
   pull request.
3. Check the next free tag (`git ls-remote --tags origin`), then tag and push:

   ```sh
   git tag -a vX.Y.Z -m "vX.Y.Z: <one line>" <commit>
   git push origin vX.Y.Z
   ```

   The release workflow checks the tag matches `Directory.Build.props`, runs the tests, packs `Ouro.Actions` and
   `Ouro.UI.Windows` as `.nupkg`, builds the Ouro Shop sample MSIX signed with a certificate made in the run (its public
   `.cer` beside it), and creates the release with the notes in `docs/releases/vX.Y.Z.md`. Write that file in step 2.
4. Wait for the release workflow to pass. A failure with no open issue is a bug: file it before anything is rerun.

## What a release can't prove yet

The free `windows-latest` runner is Windows Server. It has no App Actions runtime, no On-device Agent Registry and
no Mica. So CI proves the package registers its App Actions and MCP server, and that both doors behave, but not
that Windows lists them to agents. A Windows 11 client check (build 26220.7262 or later) would add:

- `ActionCatalog.GetAllActions()` listing the two App Actions and not the person-only one;
- `odr.exe mcp list` listing the MCP server, and an agent calling it through the registry;
- the window drawn on Mica, and its screenshots.

Until then, the release notes say so.

## nuget.org

The packages are attached to each GitHub release. Publishing them to nuget.org needs an account and API key that only
Ari can create; until then, consumers add the release's packages to a local feed (README).
