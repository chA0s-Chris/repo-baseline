# Tool: implement `sync` and `check`

> Issue: [#2](https://github.com/chA0s-Chris/repo-baseline/issues/2)

## Rationale

The profile content from #1 can only be applied by copying it by hand, and nothing detects later local edits. `docs/design.md` describes a single-file C# app, `tool/sync.cs`, whose `sync` command writes the synced content of a pinned release into a repository and whose `check` command fails CI when that content has been changed locally.

This plan implements both commands. `init` follows in #3.

## Acceptance Criteria

- [ ] `dotnet run tool/sync.cs -- sync <path>` and `dotnet run tool/sync.cs -- check <path>` work on Linux, from a full clone of `repo-baseline` whose `HEAD` differs from the pinned tag, and from a shallow tag clone (`git clone --depth 1 --branch v<version>`).
- [ ] Both commands read the profile content of the tag `v<version>` named by the target's `.repo-baseline.json`; `--ref <git-ref>` reads the content of any other ref instead. A missing tag or ref, a missing or invalid manifest, and an unknown profile fail with a clear message.
- [ ] `sync <path> <version>` writes `<version>` to the manifest, changes nothing else in it, and syncs the content of that version.
- [ ] `sync` writes whole files exactly as they are in the profile, including the executable bit; replaces the content of existing managed blocks and leaves everything outside them untouched; inserts missing blocks at the `position` from `profile.json`, creating missing target files; removes blocks that no selected profile defines from the block target files of the release; writes UTF-8 without BOM and LF line endings; and writes nothing when a target file has malformed block markers.
- [ ] `check` exits non-zero and reports every finding when:
  - a synced file or block differs from the source content, ignoring BOM and line-ending differences, or a synced file is missing or has a different executable bit;
  - a block that a selected profile defines is missing, or a file contains a block that no selected profile defines;
  - the local `<Name>.slnx.DotSettings` is missing, does not inject `..\.repo-baseline\ReSharper.DotSettings`, or redefines a key from `.repo-baseline/ReSharper.DotSettings`, apart from the injection entries;
  - a package referenced by a synced `.props` file has no `PackageVersion` in `Directory.Packages.props`.
- [ ] Every `check` finding names the file, the block, key or package where applicable, and the kind of problem.
- [ ] When `sync` succeeds, `check` directly afterwards reports no content or block findings.
- [ ] On the hand-validated Chaos.Mongo branch from #1, `sync --ref 8d69a2c` produces no diff, and `sync --ref 83fe291` produces only the changes from `13c0af2`, `d82c88e` and `8bc881d`.
- [ ] An automated test script `tests/tool/run.sh` passes. It covers:
  - whole files including the executable bit;
  - block replacement and insertion, including into a new file;
  - fragments joined across profiles;
  - missing and unknown blocks, and removal of unknown blocks by `sync`;
  - malformed markers, including that `sync` then writes nothing;
  - BOM and CRLF tolerance;
  - a missing ReSharper file, a missing injection, redefined keys, and no and several `.slnx` files;
  - missing package versions;
  - writing the version to the manifest;
  - `--ref`, and the rejection of `<version>` combined with `--ref`;
  - a missing tag or ref, a missing or invalid manifest and an unknown profile;
  - reading a tag from a checkout at another commit and from a shallow tag clone outside a fixture target that has its own `Directory.Build.props`;
  - the exit codes `0`, `1` and `2`.
- [ ] The README describes how to run both commands locally and in CI, including that the CI clone and the working directory must be outside the target repository. `AGENTS.md` and `docs/design.md` describe the tool, `--ref` and the test command, and neither the README nor `AGENTS.md` says that the tool does not exist.

## Technical Details

### Runtime and invocation

`tool/sync.cs` (new) is a file-based app. It targets .NET 10, runs on later SDKs, and rolls forward to a later major runtime when .NET 10 is not installed. It uses only the base class library (`System.Text.Json`, `System.Xml.Linq`) and the `git` CLI, so CI needs the .NET SDK and Git but no NuGet packages.

Command line (illustrative):

```
dotnet run tool/sync.cs -- sync  <path> [<version>] [--ref <git-ref>]
dotnet run tool/sync.cs -- check <path> [--ref <git-ref>]
```

- `<version>` and `--ref` cannot be combined on `sync`. `--ref` never changes the manifest.
- `<version>` is the version without the tag's `v` prefix, as in the manifest. A leading `v` on the command line is removed before the version is written.
- Exit codes: `0` for success and no findings, `1` for `check` findings, `2` for usage, manifest and source errors.
- The tool locates its own `repo-baseline` checkout from the location of `sync.cs`, not from the current directory.
- Because a file-based app reads `global.json`, `Directory.Build.props`, `Directory.Packages.props` and `NuGet.config` from its parent directories, the `repo-baseline` clone must not be inside the target repository. In CI it goes to a directory outside the workspace, such as the runner's temp directory. The `dotnet` host also selects the SDK from the `global.json` of the current directory, so the tool runs with the current directory outside the target repository, for example from the clone: `dotnet run tool/sync.cs -- check <target>`.

### Reading a release

The content comes from the Git object store of that checkout: `v<version>` or the `--ref` value is resolved to a tree, and `profiles/` is read with `git ls-tree -r` and `git cat-file`. The working tree and `HEAD` do not matter, and file modes (`100644`, `100755`) come from the tree.

The running tool may be newer than the content it reads. The profile format in `docs/design.md` is the contract between them; the tool does not run code from the tag.

### Composing the expected content

- Selected profiles are processed in manifest order. Whole files are `profiles/<profile>/files/<target path>`. Two selected profiles that provide the same target path are a source error.
- Fragments for the same file and block ID are joined in manifest profile order, separated by one empty line. Missing blocks are inserted in manifest profile order, then in `profile.json` order.
- The marker syntax follows the target file: `.md` files use `<!-- BEGIN repo-baseline: <id> -->` and `<!-- END repo-baseline: <id> -->`; `.editorconfig` and `.gitignore` use `# BEGIN repo-baseline: <id>` and `# END repo-baseline: <id>`. Any other target file type is a source error until a syntax is added.
- An inserted block is separated from local content by one empty line.
- Malformed markers (a `BEGIN` without `END`, nested blocks, a duplicate block ID) are a `check` finding. `sync` checks every target file before writing; when any file has malformed markers, it writes nothing, lists every problem and exits with `2`.

### Check

- Unknown blocks are searched in every block target file that any profile of the release defines, selected or not, so blocks left behind after a profile is removed are reported. `sync` removes such blocks, together with the empty line that separated them from local content. A file left empty after removing blocks is kept.
- Only a leading UTF-8 BOM and CRLF line endings are ignored. A missing or extra final newline is a difference.
- The executable bit is compared for whole files only.
- The ReSharper checks apply when a selected profile syncs `.repo-baseline/ReSharper.DotSettings`. The local file is `<Name>.slnx.DotSettings` for the single `.slnx` in the repository root, as in TOOL-1; none or several `.slnx` files are a finding. Keys are compared by `x:Key`. Injection entries are the keys under `/Default/Environment/InjectedLayers/`; the injection is valid when a `FileInjectedLayer/=<GUID>/RelativePath/@EntryValue` equals `..\.repo-baseline\ReSharper.DotSettings`.
- The package check compares the `PackageReference Include` items of the synced `.props` files of the selected profiles with the `PackageVersion Include` items of the root `Directory.Packages.props`, ignoring case. A missing `Directory.Packages.props` reports every package.
- The ReSharper and package findings concern local files that `sync` does not change. They remain after `sync` until they are fixed by hand, which is why the issue's "`sync` followed by `check` always succeeds" is narrowed to content and block findings.

Output format (illustrative):

```
AGENTS.md: block code-style-and-inspections: differs from v1.0.0
.editorconfig: block legacy: not defined by any selected profile
inspect_code.sh: executable bit differs from v1.0.0
Chaos.Mongo.slnx.DotSettings: key /Default/CodeStyle/...: redefines a shared key
.repo-baseline/Tests.props: package NUnit: no PackageVersion in Directory.Packages.props
```

### Manifest

Writing a version changes only the value of `version`; the rest of the manifest keeps its formatting. The tool ignores `inspection` and other fields it does not use.

### Tests

`tests/tool/run.sh` (new) follows the pattern of `tests/scripts/run.sh`: isolated Git configuration and a work directory under `tmp/`. It builds a fixture `repo-baseline` repository with a copy of `tool/sync.cs` and fixture profiles, tags several commits, and runs the tool against fixture target repositories. The fixture profiles are independent of the real ones, so profile changes do not break the tests.

### Out of scope

- `init` (#3), creating pull or merge requests, and distribution as a .NET tool.
- Adopting the baseline in this repository. That is a follow-up, and it would select only `common` and `github`: the `dotnet` profile assumes a `.slnx` and a `Directory.Packages.props`, which a repository with one tool file does not have.
