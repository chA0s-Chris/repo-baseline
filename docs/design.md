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
| `dotnet-nuget` | .NET repositories that publish packages | packaging properties, SourceLink, strong-name key handling |
| `github` | repositories hosted on GitHub | `.github/copilot-instructions.md` block, GitHub SourceLink |
| `gitlab` | repositories hosted on GitLab | GitLab SourceLink |

The order of profiles in the manifest is the order in which their content is combined. Profiles for other languages can be added later without affecting existing ones.

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

Files that do not permit comments (JSON) carry no header; the manifest is the reference for them.

### Managed blocks

Files that mix shared and local content contain marked blocks. The tool replaces the block content and leaves everything outside the blocks untouched:

```markdown
<!-- BEGIN repo-baseline: code-style-and-inspections -->
...
<!-- END repo-baseline: code-style-and-inspections -->
```

The comment syntax follows the file type: `<!-- -->` in Markdown, `#` in `.editorconfig` and `.gitignore`. A block that a profile defines but that the file does not contain is added at the position the profile specifies (for example, the start of the file). A block the file contains but no selected profile defines is reported by `check`.

This mechanism is used for:

- **`.editorconfig`**: one block at the start of the file contains `root = true` and all shared sections. Local sections come after the block. Later sections override earlier ones, so a local `[*.cs]` section can set the `file_header_template` or adjust a shared rule. Path-specific sections are always local.
- **`.gitignore`**: one block with the shared patterns; repository-specific patterns (such as the `.snk` name and, by default, `.idea/`; TOOL-4) come after it.
- **`AGENTS.md`**: blocks for the sections that are the same everywhere, such as implementation rules and code style and inspections. Project description, commands and structure are local.
- **`.github/copilot-instructions.md`**: the code review ignore list is a block; repository-specific review hints are local.

### ReSharper settings layer

ReSharper settings cannot be split into blocks, but ReSharper can load additional settings files as layers:

- The synced file `.repo-baseline/ReSharper.DotSettings` contains the cleanup profile, naming rules, file layout and all other shared settings.
- The repository's `<solution>.slnx.DotSettings` injects that file as a layer and otherwise contains only local settings, such as dictionary words and naming abbreviations.

`init` creates the injection entries. `check` reports every key from the shared layer that the local file redefines, apart from the injection entries themselves. An existing repository contains such a duplicate: its cleanup profile exists in both the injected file and the solution's own file, and the two copies differ. Most likely an edit in the IDE was saved to the solution's own file. The pilot verifies which layer Rider saves to (TOOL-3); the README then explains how to edit shared settings, or how to move such a change into `repo-baseline`.

### MSBuild imports

Shared build properties are synced as separate files under `.repo-baseline/` and imported by the repository's own build files:

| Synced file | Imported by | Contains |
|---|---|---|
| `.repo-baseline/Common.props` | root `Directory.Build.props` | language, nullability, warnings, `NoWarn`, `PreferredUILang` |
| `.repo-baseline/Tests.props` | `tests/Directory.Build.props` | `IsTestProject`, `IsPackable=false`, shared test package references |
| `.repo-baseline/Packaging.props` | `src/Directory.Build.props` | license, authors, documentation file, `EmbedUntrackedSources`, symbols |
| `.repo-baseline/SourceLink.props` | `src/Directory.Build.props` | SourceLink package for the forge (from the `github` or `gitlab` profile) |

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
| `init` | Creates the manifest, writes synced content, copies the created-once templates with names filled in, creates the ReSharper layer injection, and generates a strong-name key for `dotnet-nuget` repositories. Refuses to overwrite existing files unless asked to. |
| `sync` | Updates synced content to the version in the manifest, or to a version given on the command line, which is then recorded in the manifest. |
| `check` | Exits non-zero if synced content differs from the pinned release, if a managed block is missing or unknown, if the local ReSharper file redefines shared keys, or if a package from a synced `.props` file has no version. |

Templates for created-once files may contain placeholders (solution name, package ID, repository URL); `init` fills them once. This does not contradict rule 1, because the repository owns these files from then on.

### Versions and distribution

Releases are Git tags (`v1.0.0`, …). The tool reads profile content from a checkout of the pinned tag:

- **Locally:** run the tool from a clone of `repo-baseline`, passing the target repository's path.
- **In CI:** a shallow clone of the pinned tag (`git clone --depth 1 --branch v<version>`) followed by `check`. The repository is public, so this works from GitHub and from GitLab.

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
