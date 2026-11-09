# `init`, `sync` and `check` handle files whose names differ only in case

> Issue: [#24](https://github.com/chA0s-Chris/repo-baseline/issues/24)

## Rationale

`init` skips a file only when a file with exactly the same name exists. On a case-sensitive file system, a repository that has `NuGet.config` therefore gets a second file, `nuget.config`, from the template. NuGet reads only one config file per directory and prefers `nuget.config`, so the new file silently replaces the repository's package sources, and packages from private feeds or local folders no longer restore. On macOS and Windows the same `init` skips `NuGet.config`, because the file system ignores case.

`sync` and `check` compare names exactly as well. After `init` skips a case variant of a synced file or block target, the `sync` it suggests writes a second file next to it, and `check` reports the file as missing.

The goal is that all three commands treat a file whose name differs only in case as the existing file on every platform, and that `init --force` and `sync` never leave two files whose names differ only in case.

## Acceptance Criteria

- [x] `init` treats a file whose name differs only in letter case from a file it would write (a synced file, a created-once template or a block target) as existing: it skips the file and lists it under its existing name, for example `nuget.config: exists as NuGet.config`.
- [x] With `--force`, `init` replaces such a file with a file under the template's name and leaves no second file; for a block target, it merges the blocks into the existing file's content and writes the result under the template's name.
- [x] When no file has exactly the name and several files differ from it only in case, `init` skips them without `--force`, and with `--force` reports a problem and writes nothing.
- [x] The tool tests cover a target with `NuGet.config` and a fixture template `nuget.config`, without and with `--force`, a block target whose name differs in case, and two case variants of one name.
- [x] `sync` renames a synced file or block target that exists only under another case to its name, then writes it. When several files differ only in case, it reports a problem and writes nothing.
- [x] `check` reports a synced file or block target that exists only under another case as `<path>: exists only as <names>`, instead of as missing.
- [x] The tool tests also cover `sync` and `check` for a synced file and a block target in another case, and for several case variants.
- [x] The README and `docs/design.md` describe the rule for all three commands.

## Technical Details

All files that `init` writes go through `Plan.IsWritable` in `tool/sync.cs`, which uses `File.Exists` on the exact name. The new check compares the file name with the entries of its directory, ignoring case. A file with exactly the same name takes precedence over case variants. On Linux this gives the result that macOS and Windows already give, so the rule is the same on every platform.

Writing under the template's name lets tools that read a file only by its exact name on Linux, such as MSBuild, Git and the tool itself, find it. The path checks of `TargetPath.Problem` apply to the path `init` writes to, including an existing file whose name differs in case.

`init` replaces a case variant by renaming it to the template's name and then writing the new content, never by writing the new file and deleting the variant, which would delete the written file on a case-insensitive file system. The directory entries are compared by ordinal name, not with `File.Exists`, so the result is the same on every platform.

Only the last path component is compared. A directory whose name differs in case, such as `Tests/` for `tests/`, is out of scope. The strong-name key keeps its own check, and the check for an existing `.repo-baseline.json` is unchanged.

`sync` and `check` use the same case-variant lookup as `init`, moved to `TargetPath` so that all three commands share it. `check` compares by ordinal name too, so it reports a variant on macOS and Windows as well, where `File.Exists` would accept it. A block target that no selected profile uses is still found only by its exact name. The other `check` lookups keep exact names: the ReSharper settings, the `.props` imports and the package versions.
