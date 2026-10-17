# Create the first profile content

> Issue: [#1](https://github.com/chA0s-Chris/repo-baseline/issues/1)

## Rationale

The shared settings, scripts and agent instructions exist only as copies in individual repositories, and those copies have drifted apart. `docs/design.md` defines the profiles that should own this content, and `docs/decisions.md` records the confirmed rules, but the profile content itself does not exist yet.

This plan creates the first content for the `common`, `dotnet`, `dotnet-nuget`, `github` and `gitlab` profiles and validates it by applying it to Chaos.Mongo by hand. The tool in #2 is then written to reproduce that known-good result.

## Acceptance Criteria

- [ ] `docs/design.md` documents how profiles are stored: the directory layout, the `profile.json` contract, how fragments that share a block ID are combined, how missing blocks are inserted, and where the managed-by header goes for each file type.
- [ ] The profiles `common`, `dotnet`, `dotnet-nuget`, `github` and `gitlab` contain the synced files and blocks listed in Technical Details.
- [ ] All synced content follows the rules for synced content: no variables, no employer or client details, UTF-8 without BOM, LF line endings, and the managed-by header where the file type permits comments.
- [ ] Every confirmed decision that affects file content is reflected in it, and no rule is configured in both `.editorconfig` and the ReSharper settings.
- [ ] `cleanup_code.sh` and `inspect_code.sh` find the single `.slnx` in the repository root, and fail with a clear message when there is none or more than one.
- [ ] `cleanup_code.sh --all` applies the `Zorn` profile to every file in the solution.
- [ ] `inspect_code.sh` omits findings whose `TypeId` is listed in `inspection.suppressedIds` of `.repo-baseline.json`, and fails with a clear message when `jq` is not available.
- [ ] An automated test script exercises `cleanup_code.sh` and `inspect_code.sh` against a stubbed `dotnet`, covering solution discovery (none, one, several), the missing-`jq` error, `--all`, and `suppressedIds` filtering, and passes.
- [ ] Applied by hand to a local Chaos.Mongo branch, the content gives a reviewed `cleanup_code.sh --all` diff, a Release build without warnings and a passing test suite. `cleanup_code.sh` and `inspect_code.sh` run without arguments. `inspect_code.sh --all -e=SUGGESTION` lists findings grouped by project, and after one of the listed `TypeId`s is added to `inspection.suppressedIds`, the same run no longer shows findings of that type.
- [ ] Each finding from that validation is fixed in the profile content, recorded as a decision in `docs/decisions.md`, or tracked in a follow-up issue.
- [ ] `docs/design.md`, `docs/decisions.md` and `docs/layout-dotnet.md` are consistent with the profile content, including the removal of `SourceLink.props`, the signing correction, and the wording of CS-5.

## Technical Details

### Profile storage (new)

```
profiles/<profile>/
  profile.json            block definitions
  files/<target path>     whole files, written as they are
  blocks/<fragment>       block fragments
```

`profile.json` lists the blocks the profile contributes to. Illustrative shape:

```json
{
  "blocks": [
    { "id": "editorconfig", "file": ".editorconfig", "source": "blocks/editorconfig-core.editorconfig", "position": "start" }
  ]
}
```

- `position` is `start` or `end`; it is used only when the target file does not contain the block yet.
- When several selected profiles define the same block ID for the same file, their fragments are concatenated in the profile order of the manifest.
- The created-once templates from #3 already contain every block marker in the intended order, so `sync` normally fills existing blocks. Insertion is the fallback for a profile added later: missing blocks are inserted in manifest profile order, then in `profile.json` order, and the repository may move them afterwards. `check` does not verify block positions.

### Blocks

| Target file | Block ID | Profiles | Position |
|---|---|---|---|
| `.editorconfig` | `editorconfig` | `common`, `dotnet` | start |
| `.gitignore` | `gitignore` | `common`, `dotnet` | start |
| `AGENTS.md` | `implementation-rules` | `common` | end |
| `AGENTS.md` | `code-style-and-inspections` | `dotnet` | end |
| `AGENTS.md` | `notes-from-agents` | `common` | end |
| `.github/copilot-instructions.md` | `code-review-ignore` | `github` | start |

### Whole files

| File | Profile |
|---|---|
| `CLAUDE.md`, `ai-plans/AGENTS.md` | `common` |
| `CODESTYLE.md`, `cleanup_code.sh`, `inspect_code.sh`, `.repo-baseline/Common.props`, `.repo-baseline/Tests.props`, `.repo-baseline/ReSharper.DotSettings` | `dotnet` |
| `.repo-baseline/Packaging.props` | `dotnet-nuget` |

The `gitlab` profile stays defined but has no content yet.

### Headers

- Whole files start with the managed-by line from `docs/design.md` as a comment in the file's syntax. Shell scripts carry it directly after the shebang. Markdown, `.props` and `.DotSettings` files use an HTML or XML comment.
- Blocks carry no extra header; their `BEGIN repo-baseline` and `END repo-baseline` markers identify them.
- The validation confirms that ReSharper reads the shared `.DotSettings` file with a leading comment.

### Content sources and required changes

The Chaos.Mongo files are the starting point. `CLAUDE.md` and `ai-plans/AGENTS.md` start from this repository's own copies.

The maintainer's private reference repository may hold a more recent generic state of the scripts, the `.editorconfig` rules or the ReSharper settings, for example the `Zorn` profile in a newer Rider serialization. Compare against it and take the newer generic state where it differs. Copy nothing repository-specific from it, and do not name it in this repository, its issues or its pull requests.

- **`.editorconfig`:** `common` contains `root = true`, `[*]` and `[*.{json,yml,yaml,md}]` (CS-12). `dotnet` contains `[*.{csproj,slnx,props,config,targets,nuspec,xml}]` and one `[*.{cs,razor}]` section that replaces the two `[*.cs]` sections (CS-11). No `file_header_template`. Expression-bodied options use severity `none` (CS-6), and `resharper_object_creation_when_type_not_evident = explicit` (CS-7).
- **`.editorconfig` versus ReSharper settings (rule 4):** settings that have an `.editorconfig` equivalent read by ReSharper move into the `.editorconfig` block; for example, `max_line_length = 180` in `[*.{cs,razor}]` replaces `WRAP_LIMIT` (CS-5). Check each key against ReSharper's documented `.editorconfig` names; a key without an equivalent stays in the DotSettings file.
- **`ReSharper.DotSettings`:** from `Chaos.Mongo.slnx.DotSettings`, keeping only what `.editorconfig` cannot express: the cleanup profiles and the silent cleanup profile, file layout and member order, naming rules, null-check pattern priorities, IDE behavior such as typing assist, and the settings-migration markers. The markers stay in the shared layer. The assumption that this keeps Rider from re-running migrations into the local file is confirmed by the Rider layer test in #4. In the `Zorn` profile, `ArrangeArgumentsStyle` is off (CS-1), CSS `Reformat` and `Rearrange` are off (CS-2), and `RemoveRedundantParentheses` is not enabled (CS-8). No dictionary words and no naming abbreviations (CS-3).
- **`.gitignore`:** `common` contains `/artifacts/`, `/tmp/` (both anchored to the repository root) and editor backup files (`~$*`, `*~`). `dotnet` contains the Dotnet.gitignore patterns, `*.user`, `BenchmarkDotNet.Artifacts/`, and `.nuke/*` with `!.nuke/parameters.json`. Neither contains `.snk` (BLD-4) or `.idea/` (TOOL-4).
- **`AGENTS.md` blocks:** implementation rules and notes from agents (AGT-1) in `common`. Code style and inspections in `dotnet`, without solution names; it refers to the ReSharper settings generically when explaining why `dotnet format` is not used (TOOL-2).
- **`CODESTYLE.md`:** named arguments (CS-1), strict nullability and nullable-contract guards as worded in CS-4 without repository-specific exceptions, both member body forms (CS-6), alphabetical usings without groups (CS-9), no primary constructors on classes (CS-10).
- **Scripts:** both use the single `*.slnx` in the repository root instead of a hard-coded name (TOOL-1). `cleanup_code.sh` gains `--all` (new), which applies the `Zorn` profile to the whole solution instead of the changed files. `inspect_code.sh` keeps the `ACMR` selection and the default severity WARNING (CS-11). It always requires `jq`, reads `.repo-baseline.json` from the repository root, and drops findings from the XML report whose `TypeId` matches an entry exactly before displaying it. Entries use the full `TypeId` as the report writes it, for example `CSharpWarnings::CS8618`. A missing manifest or an empty list suppresses nothing.
- **`Common.props`:** `LangVersion` latest, `ImplicitUsings`, `Nullable`, `WarningsAsErrors` extended with `nullable`, `TreatWarningsAsErrors` when `'$(Configuration)' == 'Release' Or '$(CI)' == 'true'`, `NoWarn` 1591, `PreferredUILang` en (BLD-1, BLD-2, CS-4).
- **`Tests.props`:** `IsTestProject`, `IsPackable=false`, and the TST-1 packages without Moq and without `Microsoft.TestPlatform.TestHost`.
- **`Packaging.props`:** MIT license, authors, `GenerateDocumentationFile`, `EmbedUntrackedSources`, and the symbol package settings (`IncludeSymbols`, `SymbolPackageFormat` `snupkg`).
- **`copilot-instructions.md` block:** the code review ignore list from Chaos.Mongo.

### Source Link

Source Link comes from the .NET SDK, which includes it for GitHub and GitLab since .NET 8. Synced content therefore contains no Source Link package and no `SourceLink.props`. A repository on a self-hosted GitLab instance declares its `SourceLinkGitLabHost` item in its local `src/Directory.Build.props`, because the host name must not appear in synced content.

### Signing correction

Strong-name signing and the key path stay in each repository's build pipeline, because the key file name is repository-specific and synced content contains no variables. Issue #1 lists signing under `Packaging.props`; this plan supersedes that. For `dotnet-nuget`, "strong-name key handling" in `docs/design.md` covers only what `init` does (#3): generating the key and ignoring it in the local part of `.gitignore`.

### Script tests (new)

A bash test script under `tests/scripts/` creates throwaway Git repositories in `tmp/` and puts a stub `dotnet` first on `PATH`. The stub records its arguments and writes a fixed `inspectcode` XML report, so the tests need only bash, Git and `jq`, and no ReSharper. The missing-`jq` case runs with a restricted `PATH`. Real ReSharper behavior is covered only by the Chaos.Mongo validation.

### Validation in Chaos.Mongo

- The validation runs on a local Chaos.Mongo branch that is neither pushed nor merged; #4 adopts the baseline properly.
- The `.slnx.DotSettings` layer injection is written by hand. The working `RelativePath` form is recorded in `docs/design.md` for the template in #3.
- Chaos.Mongo still uses Moq, so it keeps a local Moq reference (TST-1).
- `TreatWarningsAsErrors` under `CI=true` and nullable warnings as errors in Debug may surface new warnings; they are fixed in Chaos.Mongo's validation branch or recorded as findings.

### Out of scope

Templates for created-once files (#3), the Renovate preset (#5), `tests/AGENTS.md` (TST-2), and applying the `common` profile to this repository's own files, which happens once the tool exists.
