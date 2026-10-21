# Design

`repo-baseline` is the single source of truth for files that many repositories share: code style and formatter settings, agent instructions, helper scripts, common build properties, and the templates and description of the standard repository layout. A small tool copies this content into repositories and checks that it stays unchanged there.

## Goals

- One place to change a shared rule, and a simple way to apply the change in every repository.
- Repositories on GitHub and on self-hosted GitLab instances are supported equally.
- Every repository can still keep deliberate local differences, and the place for them is obvious.
- CI detects local edits to shared content, so drift cannot creep back in.
- A new repository starts with the current baseline, not with copies from an older repository.
- IDEs, command-line tools, CI and agents keep working with plain files in the repository: nothing has to be fetched at build time.

## Non-goals

- CI pipeline definitions. GitHub Actions and GitLab CI have their own sharing mechanisms, and their pipelines differ too much.
- The NUKE build project. Its structure is described in the layout guide, but its code stays repository-specific.
- A general-purpose templating engine. Shared files contain no variables (see "Rules for synced content").
- Automatic creation of pull or merge requests in the first version. Running the tool in a local clone and committing the result is enough to start with.
- Content specific to an employer or client. This repository is public; such content stays in the repositories that need it.

## Concepts

### Profiles

Content is organized in profiles, and each repository selects the profiles that apply to it:

| Profile | Applies to | Contains |
|---|---|---|
| `common` | every repository | `CLAUDE.md`, shared `AGENTS.md` blocks, `ai-plans/AGENTS.md`, `.editorconfig` core, `.gitignore` core |
| `dotnet` | every .NET repository | C# `.editorconfig` rules, ReSharper settings, `CODESTYLE.md`, `cleanup_code.sh`, `inspect_code.sh`, shared MSBuild properties, layout templates |
| `dotnet-nuget` | .NET repositories that publish packages | packaging properties; `init` generates the strong-name key |
| `github` | repositories hosted on GitHub | `.github/copilot-instructions.md` block |
| `gitlab` | repositories hosted on GitLab | no content yet |

The order of profiles in the manifest is the order in which their content is combined. Profiles for other languages can be added later without affecting existing ones.

### Profile storage

Each profile is a directory in this repository:

```
profiles/<profile>/
  profile.json            block definitions
  files/<target path>     whole files, written as they are
  blocks/<fragment>       block fragments
  templates/<target path> created-once templates, filled in by `init`
```

Whole files keep their file mode: `sync` sets the executable bit for files that have it in the profile, and `check` reports a synced file whose executable bit differs.

Templates are read only by `init`; `sync` and `check` ignore them. When several selected profiles have a template for the same target path, the one later in manifest profile order replaces the earlier one, so a profile such as `dotnet-nuget` can provide a variant of a `dotnet` template. Templates have no conditionals. A template must not have the target path of a synced whole file or of the manifest. Templates of block target files contain the block markers in the intended order, and `init` fills the blocks.

`profile.json` lists the blocks the profile contributes to:

```json
{
  "blocks": [
    { "id": "editorconfig", "file": ".editorconfig", "source": "blocks/editorconfig-core.editorconfig", "position": "start" }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | Block ID, used in the block markers |
| `file` | Target file, relative to the repository root |
| `source` | Fragment file, relative to the profile directory |
| `position` | `start` or `end`: where a missing block is inserted |

A fragment contains the block content without the markers. When several selected profiles define the same block ID for the same file, their fragments are joined in manifest profile order, separated by one empty line. For example, the `.editorconfig` block consists of the `common` fragment (`root = true`, `[*]` and the data files) followed by the `dotnet` fragment (MSBuild files and C#).

### Three kinds of content

| Kind | Owned by | Updated by | Checked |
|---|---|---|---|
| **Synced** | `repo-baseline` | `sync` overwrites it | yes: `check` fails on any local edit |
| **Created once** | the repository, after creation | the repository itself | no |
| **Described** | the repository | the repository itself, following `docs/layout-*.md` | no |

Synced content is either a whole file or a managed block inside a file that also contains local content.

### Repository manifest

Every adopting repository has a `.repo-baseline.json` at its root:

```json
{
  "version": "1.0.0",
  "profiles": ["common", "dotnet", "dotnet-nuget", "github"],
  "inspection": {
    "suppressedIds": []
  }
}
```

- `version` is the `repo-baseline` release the repository is on.
- `profiles` selects the content.
- Further settings are added only when a shared file needs repository-specific input, and only in places where the tool or a shared script reads them. `inspection.suppressedIds` is the first example: `inspect_code.sh` reads it.

The manifest stores no file hashes. Because synced content contains no variables, `check` can compare it directly with the content of the pinned release.

## Mechanisms

### Whole files

The tool writes the file exactly as it is in the profile. Every synced whole file that permits comments starts with a header:

```
# Managed by repo-baseline (https://github.com/chA0s-Chris/repo-baseline). Do not edit; change it there.
```

| File type | Header |
|---|---|
| Shell scripts | `#` comment directly after the shebang line |
| Markdown | HTML comment (`<!-- … -->`) on the first line |
| `.props`, `.DotSettings` | XML comment (`<!-- … -->`) on the first line, before the root element |
| JSON | none; the manifest is the reference |

Blocks carry no header; their markers identify them.

### Managed blocks

Files that mix shared and local content contain marked blocks. The tool replaces the block content and leaves everything outside the blocks untouched:

```markdown
<!-- BEGIN repo-baseline: code-style-and-inspections -->
...
<!-- END repo-baseline: code-style-and-inspections -->
```

The comment syntax follows the file type: `<!-- -->` in Markdown, `#` in `.editorconfig` and `.gitignore`. A block the file contains but no selected profile defines is reported by `check`.

The created-once templates already contain every block marker in the intended order, so `sync` normally only fills existing blocks. Inserting a missing block is the fallback for a profile that is added later: missing blocks are inserted at the `position` from `profile.json`, in manifest profile order and then in `profile.json` order, and the repository may move them afterwards. `check` does not verify block positions.

This mechanism is used for:

- **`.editorconfig`**: one block at the start of the file contains `root = true` and all shared sections. Local sections come after the block. Later sections override earlier ones, so a local `[*.cs]` section can set the `file_header_template` or adjust a shared rule. Path-specific sections are always local.
- **`.gitignore`**: one block with the shared patterns; repository-specific patterns (such as the `.snk` name and, by default, `.idea/`; TOOL-4) come after it.
- **`AGENTS.md`**: blocks for the sections that are the same everywhere, such as implementation rules and code style and inspections. Project description, commands and structure are local.
- **`.github/copilot-instructions.md`**: the code review ignore list is a block; repository-specific review hints are local.

### ReSharper settings layer

ReSharper settings cannot be split into blocks, but ReSharper can load additional settings files as layers:

- The synced file `.repo-baseline/ReSharper.DotSettings` contains the shared settings that `.editorconfig` cannot express (rule 4): the cleanup profiles, the file layout and member order, naming rules, null-check pattern priorities, IDE behavior such as typing assist, and the settings-migration markers. Everything else, including the line length (`max_line_length`) and inspection severities, is in the `.editorconfig` block.
- The repository's `<solution>.slnx.DotSettings` injects that file as a layer and otherwise contains only local settings, such as dictionary words and naming abbreviations.

`init` creates the injection entries. They consist of a file layer with the path of the shared file and a display name and priority for the layer:

```xml
<s:Boolean x:Key="/Default/Environment/InjectedLayers/FileInjectedLayer/=<GUID>/@KeyIndexDefined">True</s:Boolean>
<s:String x:Key="/Default/Environment/InjectedLayers/FileInjectedLayer/=<GUID>/RelativePath/@EntryValue">..\.repo-baseline\ReSharper.DotSettings</s:String>
<s:Boolean x:Key="/Default/Environment/InjectedLayers/InjectedLayerCustomization/=File<GUID>/@KeyIndexDefined">True</s:Boolean>
<s:String x:Key="/Default/Environment/InjectedLayers/InjectedLayerCustomization/=File<GUID>/DisplayName/@EntryValue">repo-baseline</s:String>
<s:Double x:Key="/Default/Environment/InjectedLayers/InjectedLayerCustomization/=File<GUID>/RelativePriority/@EntryValue">1</s:Double>
```

The path is relative to the settings file itself, so it starts with `..\` although both files are in the repository root; ReSharper on Linux accepts the backslashes. When the path points to a missing file, ReSharper creates an empty one instead of reporting an error, and the shared settings silently do not apply. `check` therefore verifies that the injection points to `.repo-baseline/ReSharper.DotSettings`.

`check` reports every key from the shared layer that the local file redefines, apart from the injection entries themselves. An existing repository contains such a duplicate: its cleanup profile exists in both the injected file and the solution's own file, and the two copies differ. Most likely an edit in the IDE was saved to the solution's own file. The pilot verifies which layer Rider saves to (TOOL-3); the README then explains how to edit shared settings, or how to move such a change into `repo-baseline`.

### MSBuild imports

Shared build properties are synced as separate files under `.repo-baseline/` and imported by the repository's own build files:

| Synced file | Imported by | Contains |
|---|---|---|
| `.repo-baseline/Common.props` | root `Directory.Build.props` | language, nullability, warnings, `NoWarn`, `PreferredUILang` |
| `.repo-baseline/Tests.props` | `tests/Directory.Build.props` | `IsTestProject`, `IsPackable=false`, shared test package references |
| `.repo-baseline/Packaging.props` | `src/Directory.Build.props` | license, authors, documentation file, `EmbedUntrackedSources`, symbols |

Source Link needs no synced file: the .NET SDK includes it for GitHub and GitLab since .NET 8. A repository on a self-hosted GitLab instance declares its host with a `SourceLinkGitLabHost` item in its local `src/Directory.Build.props`, because host names never appear in synced content.

Strong-name signing is not part of `Packaging.props`, because the key file name is repository-specific. The build pipeline signs when packing (BLD-4).

The repository's own `Directory.Build.props` files are created once and stay local. They hold target frameworks and everything repository-specific, and they can override shared properties after the import.

Package versions stay in the repository's `Directory.Packages.props`, where Renovate keeps them current. `check` verifies that every package referenced by a synced `.props` file has a `PackageVersion` entry there.

### Renovate preset

Shared Renovate rules are published as a preset in this repository (`renovate/default.json`). Repositories reference it with `"extends": ["github>chA0s-Chris/repo-baseline//renovate/default"]` instead of copying rules. Renovate on GitLab instances needs a GitHub token to read the preset.

## Rules for synced content

1. **No variables.** Synced files and blocks are identical in every repository. Anything repository-specific goes into local content, an override after an import, or a manifest setting that a shared script reads.
2. **No employer or client details.** Private feed addresses, internal host names and similar content belong in local content.
3. **Normalized encoding.** The tool writes UTF-8 without BOM and LF line endings. `check` ignores BOM and line-ending differences, because IDEs add them when saving.
4. **One source per rule.** A rule that `.editorconfig` or the ReSharper settings enforce is described in `CODESTYLE.md` but configured only once. When both `.editorconfig` and the ReSharper settings can express a rule, `.editorconfig` is preferred because more tools read it.

## Tool

The tool is a single-file C# app (`tool/sync.cs`, run with `dotnet run`) with three commands:

| Command | Effect |
|---|---|
| `init` | Creates the manifest, writes synced content, copies the created-once templates with their placeholders filled in, which creates the ReSharper layer injection, and generates a strong-name key for `dotnet-nuget` repositories. Skips existing files and lists them; `--force` overwrites them, except an existing strong-name key. |
| `sync` | Updates synced content to the version in the manifest, or to a version given on the command line, which is then recorded in the manifest. Removes blocks that no selected profile defines. Writes nothing if a block target file has malformed block markers, if a file the selected profiles use is not valid UTF-8, or if such a file has a symbolic link, a directory or a file in its path where the tool needs a regular file. |
| `check` | Exits non-zero if synced content differs from the pinned release, if a managed block is missing, unknown or malformed, if a synced file is not a regular file inside the repository or a managed file is not valid UTF-8, if the local ReSharper file does not inject the shared layer or redefines shared keys, or if a package from a synced `.props` file has no version. |

All three commands exit with `0` on success, `1` when `check` has findings, and `2` on usage, manifest and source errors and when `init` or `sync` refuses to write. The README shows how to run them.

### `init`

`init` takes the version to record in the manifest and the inputs from the layout guide: the name, the project type, the forge with the repository URL, and the target frameworks. It derives the profiles from the project type and the forge: `common`, `dotnet`, `dotnet-nuget` for published libraries, then `github` or `gitlab`. `--private` leaves out the `LICENSE`. `--ref` reads the content of another ref, as for `sync`, but `init` still records the given version, so a repository can be set up before its release is tagged.

Templates for created-once files may contain placeholders; `init` fills them once. This does not contradict rule 1, because the repository owns these files from then on. Placeholders have the form `{{<Placeholder>}}`; any other `{{` in a template is a source error.

| Placeholder | Value |
|---|---|
| `{{Name}}` | The name: solution, root namespace and file names. The only placeholder allowed in target paths, such as `{{Name}}.slnx`. |
| `{{RepositoryUrl}}` | The repository URL |
| `{{Year}}` | The current year |
| `{{LayerGuid}}` | A new GUID for the ReSharper layer injection, as 32 uppercase hexadecimal digits |
| `{{TargetFrameworkProperties}}` | `TargetFramework` for one target framework; for several, `LatestTarget` (the highest version), `MainTargets` and `TargetFrameworks` as in the layout guide |
| `{{SourceLinkGitLabHost}}` | A `SourceLinkGitLabHost` item for a GitLab host other than `gitlab.com` (BLD-6); otherwise nothing |
| `{{LicenseNotice}}` | The MIT sentence of the `file_header_template`, as an escaped continuation (`\n…`); nothing with `--private` |

A placeholder on a line of its own takes the line's indentation for every line of its value, and the line is removed when the value is empty.

`init` writes nothing while any target path has a problem of the kind that stops `sync`. An existing `.repo-baseline.json` stops it unless `--force` is given. Otherwise it never changes an existing file without `--force`, with one exception: for `dotnet-nuget`, it adds the key's file name to an existing `.gitignore` that does not list it yet, so that the key cannot be committed by mistake. The key is never printed, and on Unix it is readable only by its owner.

**Adopting an existing repository:** run `init` to add the missing files, then `sync` to update the synced files and blocks that `init` skipped. An existing `<Name>.slnx.DotSettings` does not get the layer injection automatically; `check` reports it until it is added by hand.

### Versions and distribution

Releases are Git tags (`v1.0.0`, …). The tool reads the profile content of the pinned tag from the Git objects of the `repo-baseline` checkout it runs from, so the checkout does not have to be at that tag. `--ref <git-ref>` reads the content of any other ref instead, for development and for pilots before a release; it never changes the manifest.

- **Locally:** run the tool from a clone of `repo-baseline`, passing the target repository's path.
- **In CI:** a shallow clone of the pinned tag (`git clone --depth 1 --branch v<version>`) followed by `check`. The repository is public, so this works from GitHub and from GitLab.

The clone and the working directory must be outside the target repository. `dotnet run` reads `global.json` from the working directory and `Directory.Build.props`, `Directory.Packages.props` and `NuGet.config` from the directories above `tool/sync.cs`, so the target's build settings would otherwise apply to the tool.

## Rollout

1. Create the first profile content from the existing repositories, following `decisions.md`.
2. Implement `sync` and `check`, then `init`.
3. Pilot in two public repositories with different project types: a published library and an application. The pilot also verifies how Rider saves edits to injected settings layers.
4. Add `check` to their CI, release `v1.0.0`, then adopt the baseline in the remaining repositories one at a time. Reformatting caused by new rules goes into its own commit, which is listed in `.git-blame-ignore-revs`.
5. Add an agent skill that creates a new repository with `init` and the layout guide.

## Open Questions

- **Distribution as a .NET tool:** packaging the tool together with the profile content as a .NET tool would let Renovate propose baseline updates through the tool manifest. It would require a .NET SDK in non-.NET repositories, so it is postponed until the first version has been tried.
- **Automatic pull and merge requests:** worthwhile once more than a handful of repositories have adopted the baseline.
- **Shared testing guidelines:** deferred (TST-2 in `decisions.md`).
