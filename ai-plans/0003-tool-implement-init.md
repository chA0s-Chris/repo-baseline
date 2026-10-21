# Tool: implement `init`

> Issue: [#3](https://github.com/chA0s-Chris/repo-baseline/issues/3)

## Rationale

A new repository should start with the current baseline instead of a copy of an older repository. `docs/design.md` defines `init` for this: it writes the manifest, the synced content and the created-once files, and fills in the repository-specific values once. `docs/layout-dotnet.md` lists the files and the required inputs. The tool so far has only `sync` and `check`, and no templates exist.

The work is one story, but it has two audiences. The command is C# logic that can be tested against fixture profiles, as `sync` and `check` are. The templates are profile content that is reviewed against the layout guide and verified by building a real solution. The plan is therefore delivered as a two-layer stack: the command first, then the templates.

## Acceptance Criteria

### Layer 1: Command

- [x] `dotnet run tool/sync.cs -- init <path> <version> --name <Name> --type application|library --forge github|gitlab --repository-url <url> --target-frameworks <tfm>[;<tfm>…] [--private] [--force] [--ref <git-ref>]` works. Missing, repeated or invalid inputs exit with `2` and a clear message.
- [x] `init` selects the profiles `common`, `dotnet`, `dotnet-nuget` (libraries only), then `github` or `gitlab`, in that order. It writes a `.repo-baseline.json` with `<version>`, those profiles and an empty `inspection.suppressedIds`.
- [x] `init` reads templates from `profiles/<profile>/templates/` of the same release tree as the synced content. It fills the placeholders in file contents and in target paths. When several selected profiles provide a template for the same target path, the one later in profile order wins. An unknown placeholder, a template that is not valid UTF-8, and a template on the target path of a synced whole file or of the manifest are source errors.
- [x] In an empty directory, `init` writes every synced whole file and every block of the selected profiles. A fixture whose templates provide the `.slnx`, the DotSettings injection and the package versions passes `check` with the same version or `--ref` directly after `init`.
- [x] `init` does not overwrite existing files. It writes the missing ones and lists every file it skipped. `--force` writes all of them. An existing `.repo-baseline.json` stops `init` with exit code `2` unless `--force` is given. A path problem or source error makes `init` write nothing.
- [x] `--private` leaves out the `LICENSE` template.
- [x] For `dotnet-nuget`, `init` generates `<Name>.snk` unless it exists, even with `--force`, and never prints its content. In a Git repository, `git check-ignore` reports the key as ignored, also when `.gitignore` existed before `init`. A minimal project signed with the key builds and has a public key token.
- [x] `bash tests/tool/run.sh` passes and covers placeholder filling in contents and paths, template overrides, the source errors above, skip and `--force`, an existing manifest, `--private`, key generation and ignoring, the input validation, and all four combinations of type and forge.
- [x] The README describes how to run `init`, including adoption of an existing repository with `init` followed by `sync`. `AGENTS.md` and `docs/design.md` describe `init`, the `templates/` directory and the placeholders. None of them says that `init` does not exist yet.

### Layer 2: Templates

- [x] The profiles contain a template for every created-once file in `docs/layout-dotnet.md`, including an empty `<Name>.slnx`. The templates follow the encoding rules for synced content and contain no employer or client details.
- [x] For each combination of type (application, published library) and forge (GitHub, GitLab), `init` in an empty directory produces exactly the synced, block and created-once files that `docs/layout-dotnet.md` lists for it, excluding described and optional entries, plus `<Name>.snk` for libraries. `check` succeeds directly afterwards.
- [x] For both an application and a library, after a solution with one project and one test project has been added, `dotnet tool restore`, `dotnet build -c Release`, `dotnet test` and `./cleanup_code.sh` succeed without manual changes to the generated files, and the cleanup leaves the generated files unchanged. This is verified by hand, because it needs network access.
- [x] `bash tests/tool/run.sh` runs `init` and `check` against the real profiles from the working tree for all four combinations of type and forge, and asserts the list of generated files for each.
- [x] `docs/layout-dotnet.md` lists `<Name>.slnx` as created once and documents the `init` inputs, including `--private`. Its steps for creating a repository add the projects to the existing `<Name>.slnx`.

## Technical Details

### Stack Design

1. `0003-tool-implement-init-command`: implement `init`, its template mechanism and its documentation, tested against fixture profiles. It depends only on `main`.
2. `0003-tool-implement-init-templates`: add the real templates and the layout guide changes, and verify them end to end. It depends on the command layer. A lower layer cannot verify these criteria, because they need the real templates.

### Command line

- `<version>` is required, because the manifest needs it. Content is read from `v<version>` unless `--ref` is given. Unlike `sync`, `init` accepts `<version>` together with `--ref`, so pilots can initialize a repository before the tag exists. `check` then needs the same `--ref` until the tag is released.
- `--name` is the solution name and root namespace: dot-separated C# identifiers, such as `Chaos.Example`. It also names the `.slnx`, the DotSettings file and the key.
- `--repository-url` is an absolute `https` URL. With `--forge github`, its host must be `github.com`; with `--forge gitlab`, any host other than `github.com` is accepted. `--target-frameworks` is a semicolon-separated list of `net<major>.<minor>` monikers.
- The profiles are derived from `--type` and `--forge` instead of being an input, so every new repository gets the combination that `docs/layout-dotnet.md` prescribes.
- `init` does not run `git init`, create projects, or write CI workflows or `build/`. Creating the projects is the job of the agent skill in rollout step 5.

### Templates

- Template storage: `profiles/<profile>/templates/<target path>` (new). `sync` and `check` ignore the directory. Target paths follow the same validation as whole files. They are validated after `{{Name}}` is filled in, and the existing collision checks include them: paths that differ only in case, and a file where a directory is needed.
- There are no conditionals. Variants come from overrides: `dotnet` replaces the `common` templates of `AGENTS.md` and `.editorconfig`, and `dotnet-nuget` replaces `src/Directory.Build.props`.
- Placeholders have the form `{{<Placeholder>}}`:

  | Placeholder | Value |
  |---|---|
  | `{{Name}}` | `--name`; the only placeholder allowed in target paths |
  | `{{RepositoryUrl}}` | `--repository-url` |
  | `{{Year}}` | the current year |
  | `{{LayerGuid}}` | a new GUID for the ReSharper layer injection, in the form ReSharper writes: 32 uppercase hexadecimal digits |
  | `{{TargetFrameworkProperties}}` | lines for the root `Directory.Build.props`: `TargetFramework` for one framework; for several, `LatestTarget` (the highest version), `MainTargets` and `TargetFrameworks` as in the layout guide |
  | `{{SourceLinkGitLabHost}}` | a `SourceLinkGitLabHost` item (BLD-6) for a GitLab host other than `gitlab.com`; otherwise nothing |
  | `{{LicenseNotice}}` | for the `file_header_template`: `\nThis file is licensed under the MIT license. See LICENSE in the project root for more information.`, an escaped continuation of the header; nothing with `--private` |

  A line that contains only a placeholder that expands to nothing is removed.
- Templates of block target files (`AGENTS.md`, `.editorconfig`, `.gitignore`) contain the block markers in the intended order, and `init` fills the blocks. Block targets without a template, such as `.github/copilot-instructions.md`, are created by block insertion, as in `sync`.
- `.repo-baseline.json` is generated in code rather than from a template, because the manifest is the tool's own contract.

### Existing files and adoption

- `init` decides what to write before it writes anything, so a refused or failed run leaves the directory unchanged. The path checks of `sync` apply: symbolic links, and directories or files in the way.
- An existing file, whether synced, a block target or created once, is skipped and listed, and the exit code stays `0`. A skipped block target keeps its content; `sync` later fills or inserts its blocks.
- `--force` writes every file as for an empty directory, except an existing strong-name key. An existing block target without a template gets its blocks merged, as `sync` does.
- Adoption of an existing repository is `init` to add the missing files, then `sync` to update existing synced files and blocks. The ReSharper injection in an existing DotSettings file is added by hand; `check` reports it until then.

### Strong-name key

- An existing key is never replaced, even with `--force`, because a new key changes the strong-name identity of a published library. It is listed with the skipped files.
- The key is generated in C# with `RSACryptoServiceProvider(1024)` and `ExportCspBlob(includePrivateParameters: true)`. On Linux with .NET 10, this produces the 596-byte `RSA2` private key blob that `sn -k` writes. No external tool is needed.
- On Unix, the file is readable and writable only by its owner. The output names the file but never shows its content.
- `init` appends `<Name>.snk` to the local part of `.gitignore` unless that exact line is already there. It does this also when `.gitignore` already existed. Without `--force`, this is the only change `init` makes to an existing file, because otherwise the key could be committed by mistake.
- The templates do not sign: the release pipeline signs when packing (BLD-4). The signing test sets `SignAssembly` and `AssemblyOriginatorKeyFile` on the command line.

### Template content

| Profile | Templates |
|---|---|
| `common` | `AGENTS.md`, `.editorconfig`, `.gitignore` (block and `.idea/`, TOOL-4), `README.md`, `LICENSE` (MIT), `renovate.json` (extends `github>chA0s-Chris/repo-baseline//renovate/default`, #5) |
| `dotnet` | `AGENTS.md` (with the commands table from the layout guide), `.editorconfig` (local `[*.cs]` section with `file_header_template`), `{{Name}}.slnx`, `{{Name}}.slnx.DotSettings`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`, `coverlet.xml`, `dotnet-tools.json`, `src/Directory.Build.props`, `src/AGENTS.md`, `tests/Directory.Build.props`, `tests/AGENTS.md` |
| `dotnet-nuget` | `src/Directory.Build.props`, which also imports `.repo-baseline/Packaging.props` and sets `PackageProjectUrl` and `Copyright` |

- The copyright holder is fixed in the templates, as `Authors` is in `Packaging.props`. The `[*.cs]` file header ends with `{{LicenseNotice}}`, so it refers to the MIT license only when `LICENSE` is written.
- `renovate.json` extends a preset that arrives with #5. Until then Renovate cannot resolve it. This is acceptable, because #5 lands before the pilot (#4) enables Renovate.
- `{{Name}}.slnx.DotSettings` contains only the injection entries from `docs/design.md`, with `{{LayerGuid}}`.
- `Directory.Packages.props` enables Central Package Management and contains a `PackageVersion` for every package in the synced `.props` files, with FluentAssertions on 7.x (TST-1). `dotnet-tools.json` contains `jetbrains.resharper.globaltools` and `dotnet-coverage` (BLD-5). Use the versions that are current at implementation time; Renovate keeps them current afterwards.
- `global.json` pins `10.0.100` with `rollForward` `latestFeature` and `allowPrerelease` `false`, so every SDK 10 satisfies it.
- `tests/AGENTS.md` is a skeleton without shared testing rules (TST-2).

### Tests

- Layer 1 extends `tests/tool/run.sh` with fixture templates in the fixture profiles. The signing test builds a minimal project without package references, so it needs no network. A GitLab case with a self-hosted URL covers `{{SourceLinkGitLabHost}}`.
- Layer 2 adds a fixture `repo-baseline` repository that commits a copy of the real `profiles/` from the working tree. The real templates are therefore tested before they are committed.
