# `init` handles existing files whose names differ only in case

> Issue: [#24](https://github.com/chA0s-Chris/repo-baseline/issues/24)

## Rationale

`init` skips a file only when a file with exactly the same name exists. On a case-sensitive file system, a repository that has `NuGet.config` therefore gets a second file, `nuget.config`, from the template. NuGet reads only one config file per directory and prefers `nuget.config`, so the new file silently replaces the repository's package sources, and packages from private feeds or local folders no longer restore. On macOS and Windows the same `init` skips `NuGet.config`, because the file system ignores case.

The goal is that `init` treats a file whose name differs only in case as the existing file on every platform, and that `init --force` never leaves two files whose names differ only in case.

## Acceptance Criteria

- [ ] `init` treats a file whose name differs only in letter case from a file it would write (a synced file, a created-once template or a block target) as existing: it skips the file and lists it under its existing name, for example `nuget.config: exists as NuGet.config`.
- [ ] With `--force`, `init` replaces such a file with a file under the template's name and leaves no second file; for a block target, it merges the blocks into the existing file's content and writes the result under the template's name.
- [ ] When no file has exactly the name and several files differ from it only in case, `init` skips them without `--force`, and with `--force` reports a problem and writes nothing.
- [ ] The tool tests cover a target with `NuGet.config` and a fixture template `nuget.config`, without and with `--force`, a block target whose name differs in case, and two case variants of one name.
- [ ] The README paragraph on `init` and the `init` row and section in `docs/design.md` describe the rule.

## Technical Details

All files that `init` writes go through `Plan.IsWritable` in `tool/sync.cs`, which uses `File.Exists` on the exact name. The new check compares the file name with the entries of its directory, ignoring case. A file with exactly the same name takes precedence over case variants. On Linux this gives the result that macOS and Windows already give, so the rule is the same on every platform.

Writing under the template's name lets tools that read a file only by its exact name on Linux, such as MSBuild, Git and the tool itself, find it. The path checks of `TargetPath.Problem` apply to the path `init` writes to, including an existing file whose name differs in case.

`init` replaces a case variant by renaming it to the template's name and then writing the new content, never by writing the new file and deleting the variant, which would delete the written file on a case-insensitive file system. The directory entries are compared by ordinal name, not with `File.Exists`, so the result is the same on every platform.

Only the last path component is compared. A directory whose name differs in case, such as `Tests/` for `tests/`, is out of scope. The strong-name key keeps its own check, and the check for an existing `.repo-baseline.json` is unchanged. `sync` is not changed: it writes only synced files, whose names the baseline fixes.
