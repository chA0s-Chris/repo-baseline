# `check` reports synced `.props` files that are not imported

> Issue: [#17](https://github.com/chA0s-Chris/repo-baseline/issues/17)

## Rationale

The shared build settings reach a project only when the repository's own `Directory.Build.props` files import the synced `.props` files under `.repo-baseline/`. `check` currently compares the synced files with the pinned release and checks their package versions, but not whether anything imports them. After `init` and `sync` on an existing repository, as in the Chaos.Mongo pilot (chA0s-Chris/Chaos.Mongo#274), the synced files exist, no build file imports them, and `check` reports no findings. The shared settings are then silently inactive.

The goal is that `check` reports a synced `.props` file that the build file named in the layout guide does not import.

## Acceptance Criteria

- [x] `check` reports each synced `.props` file that its importing file does not import: `Directory.Build.props` for `.repo-baseline/Common.props`, `tests/Directory.Build.props` for `.repo-baseline/Tests.props`, and `src/Directory.Build.props` for `.repo-baseline/Packaging.props`.
- [x] A missing importing file is reported in the same way as a missing import, and an importing file that is not valid XML is reported as not valid XML.
- [x] `check` reports no finding for a repository that `init` creates from the real profiles.
- [x] The tool tests cover a missing import, an import written with slashes and one written with backslashes, a missing importing file, and an importing file that is not valid XML.
- [x] The "MSBuild imports" section of `docs/design.md` and the `check` row of the README's command table describe the new check.
- [x] The README's adoption paragraph says that existing `Directory.Build.props` files need the imports added by hand and that `check` reports them until then.

## Technical Details

The mapping from synced file to importing file is a fixed table in `CheckCommand`, next to the existing `Directory.Packages.props` and ReSharper constants. It follows the table in the "MSBuild imports" section of `docs/design.md`. Only synced files of the selected profiles are checked, so `Packaging.props` is checked for `dotnet-nuget` only. A synced `.props` file without an entry in the table is not checked.

An importing file imports a synced file when it contains an `Import` element, at any depth, whose `Project` attribute ends with the synced file's path once backslashes are read as slashes. For example, `$(MSBuildThisFileDirectory)../.repo-baseline/Tests.props` imports `.repo-baseline/Tests.props`. The comparison ignores case, as `CheckPackageVersions` does for the `.props` extension, because MSBuild resolves imports without regard to case on Windows and macOS. The check does not evaluate MSBuild expressions or `Condition` attributes.

A finding names the importing file, like the existing findings: `tests/Directory.Build.props: does not import .repo-baseline/Tests.props`.

The tool tests use fixture profiles, so the real profiles are checked by hand: `init` into an empty directory with `--ref HEAD`, then `check` with the same `--ref`, once for a library and once for an application.

CI runs the tool from the pinned tag, so the check applies to repositories that pin the next release (`v0.2.0`) or later.
