# .NET Repository Layout

This guide describes the standard layout of a .NET repository that uses the `dotnet` profile. It serves two purposes: to keep existing repositories consistent, and to let an agent or a person create a new repository that matches the others.

Each entry states how it is maintained:

- **synced**: written and checked by the `repo-baseline` tool; never edit it in the repository (see [design.md](design.md)).
- **block**: the file contains synced managed blocks and local content.
- **created once**: created from a template by `init`; the repository owns it afterwards.
- **described**: created by hand following this guide.

Decisions referenced as `CS-…`, `BLD-…` and so on are recorded in [decisions.md](decisions.md).

## Overview

```
<repository>/
├── .github/                      GitHub only
│   ├── copilot-instructions.md   block
│   └── workflows/                described
├── .nuke/
│   └── parameters.json           described (only with build/)
├── .repo-baseline/               synced
│   ├── Common.props
│   ├── Packaging.props           dotnet-nuget only
│   ├── ReSharper.DotSettings
│   └── Tests.props
├── ai-plans/
│   └── AGENTS.md                 synced
├── artifacts/                    ignored
├── benchmarks/                   optional
│   ├── AGENTS.md                 described
│   └── Directory.Build.props     created once
├── build/                        optional, described
├── dist/                         optional
├── docs/                         optional
├── src/
│   ├── Aspire/                   optional: app host and service defaults
│   ├── AGENTS.md                 created once
│   └── Directory.Build.props     created once
├── tests/
│   ├── Aspire/                   optional: end-to-end tests
│   ├── AGENTS.md                 created once
│   └── Directory.Build.props     created once
├── tmp/                          ignored
├── .editorconfig                 block
├── .gitignore                    block
├── .repo-baseline.json           created once
├── <Name>.slnx                   described
├── <Name>.slnx.DotSettings       created once
├── <Name>.snk                    ignored, dotnet-nuget only
├── AGENTS.md                     block
├── CLAUDE.md                     synced
├── CODESTYLE.md                  synced
├── Directory.Build.props         created once
├── Directory.Packages.props      created once
├── LICENSE                       public repositories
├── README.md                     created once
├── cleanup_code.sh               synced
├── coverlet.xml                  created once
├── dotnet-tools.json             created once (SDK 10+; older SDKs: .config/)
├── global.json                   created once
├── inspect_code.sh               synced
├── nuget.config                  created once
└── renovate.json                 created once
```

`build.sh`, `build.cmd` and `build.ps1` are added to the root when the repository has a `build/` project.

## Root Files

### Code style

| File | Kind | Notes |
|---|---|---|
| `.editorconfig` | block | The shared block comes first and starts with `root = true`. Local sections follow it: at least a `[*.cs]` section with the `file_header_template`, plus path-specific sections if needed. |
| `<Name>.slnx.DotSettings` | created once | Injects `.repo-baseline/ReSharper.DotSettings` as a settings layer. Contains only local settings: dictionary words and naming abbreviations (CS-3). Must not redefine shared keys. |
| `CODESTYLE.md` | synced | Human-readable code style. Repository-specific code style rules go into `AGENTS.md` or a nested `AGENTS.md`. |
| `cleanup_code.sh` | synced | Applies the `Zorn` cleanup profile to changed, staged and untracked files, or to the whole solution with `--all` (TOOL-1). |
| `inspect_code.sh` | synced | Runs ReSharper inspections on changed files, on a branch (`--base`), or on the whole solution (`--all`). Its warnings and errors are what needs fixing; suggestions are ignored (CS-11). Suppressed inspection IDs come from `.repo-baseline.json`, so the script needs `jq`. |
| `dotnet-tools.json` | created once | In the root for SDK 10 or later, in `.config/dotnet-tools.json` for older SDKs (BLD-5). Contains at least `jetbrains.resharper.globaltools` and `dotnet-coverage`. Repositories add further tools such as `dotnet-ef`. |

### Agent instructions

| File | Kind | Notes |
|---|---|---|
| `AGENTS.md` | block | Local: project description, project structure, local development commands, and pointers to nested `AGENTS.md` files. Synced blocks: implementation rules, code style and inspections, and notes from agents (AGT-1). |
| `CLAUDE.md` | synced | Only points to `AGENTS.md`. |
| `ai-plans/AGENTS.md` | synced | Rules for Guided Coding plans and plan deviation files. |
| `.github/copilot-instructions.md` | block | The shared block lists directories that code reviews ignore. Repository-specific review hints follow it. |

The root `AGENTS.md` keeps a table of local development commands with at least these rows, adapted to the solution name:

| Purpose | Command |
|---|---|
| Restore local tools | `dotnet tool restore` |
| Apply the code style | `./cleanup_code.sh` |
| Inspect changed C# files | `./inspect_code.sh` |
| Inspect C# changes relative to a base | `./inspect_code.sh --base <revision>` |
| Complete test suite | `dotnet test <Name>.slnx` |
| Tests excluding integration and end-to-end tests | `dotnet test <Name>.slnx --filter "TestCategory!=Integration&TestCategory!=E2E"` |
| Release build | `dotnet build -c Release <Name>.slnx` |

### Build and packages

| File | Kind | Notes |
|---|---|---|
| `<Name>.slnx` | described | The solution uses the XML format (`.slnx`). It contains every project in `src/`, `tests/`, `benchmarks/` and `build/`. |
| `Directory.Build.props` | created once | Imports `.repo-baseline/Common.props`, then defines the target frameworks (see below) and local overrides. |
| `Directory.Packages.props` | created once | Enables Central Package Management (`ManagePackageVersionsCentrally`) and contains a `PackageVersion` for every referenced package, including those referenced by synced `.props` files. Projects reference packages without versions. |
| `global.json` | created once | Pins the SDK: `version` is a full SDK version, `rollForward` is `latestFeature`, `allowPrerelease` is `false`. Renovate updates it. |
| `nuget.config` | created once | Public repositories use only nuget.org (BLD-3). |
| `coverlet.xml` | created once | Run settings for the XPlat Code Coverage collector. The template includes all assemblies (`[*]*`) and excludes test assemblies (`[*.Tests]*`), generated files and members marked with `GeneratedCodeAttribute`, `CompilerGeneratedAttribute` or `ExcludeFromCodeCoverageAttribute`. |
| `renovate.json` | created once | Extends the shared preset (`github>chA0s-Chris/repo-baseline//renovate/default`) and adds local rules only. |
| `<Name>.snk` | ignored | Only for published libraries. Never committed; the release pipeline restores it from a CI secret (BLD-4). |

#### Target frameworks

A repository that targets a single framework sets `TargetFramework` in the root `Directory.Build.props`.

A library that targets several frameworks defines the list once and reuses it:

```xml
<PropertyGroup>
  <LatestTarget>net10.0</LatestTarget>
  <MainTargets>$(LatestTarget);net9.0;net8.0</MainTargets>
  <TargetFrameworks>$(MainTargets)</TargetFrameworks>
</PropertyGroup>
```

Projects that should not be multi-targeted, such as benchmarks or tools, use `$(LatestTarget)`.

### Repository metadata

| File | Kind | Notes |
|---|---|---|
| `.repo-baseline.json` | created once | Baseline version, profiles and the few settings shared scripts read ([design.md](design.md)). |
| `README.md` | created once | Description, installation or usage, and badges for public repositories. |
| `LICENSE` | created once | MIT for public repositories. |
| `CHANGELOG.md` | described | For published libraries, if the release process does not generate release notes elsewhere. |

## Directories

### `src/`

Contains the production projects, one directory per project, named after the project (`<Name>`, `<Name>.<Feature>`).

- **`src/Directory.Build.props`** (created once) imports the root `Directory.Build.props` and sets `GenerateDocumentationFile` to `true`. It appends `$(AdditionalConstants)` to `DefineConstants`, so the build pipeline can set constants such as `NUGET_RELEASE`. Published libraries also import `.repo-baseline/Packaging.props` and set package metadata: project URL, tags, copyright, and the package icon from `dist/`. Source Link comes from the .NET SDK; a repository on a self-hosted GitLab instance adds a `SourceLinkGitLabHost` item for its host here (BLD-6).
- **`src/Aspire/`** (optional) contains `MyProject.AppHost` and `MyProject.ServiceDefaults` for applications that use Aspire for local development and end-to-end tests (TST-5). In repositories that publish packages, `src/Aspire/Directory.Build.props` imports the parent file and sets `IsPackable` to `false`, so the pipeline does not pack the Aspire projects.
- **`src/AGENTS.md`** (created once) gives an overview of the projects (one bullet per project with its responsibility) and the production code rules specific to the repository.

### `tests/`

Contains one test project per production project, named `<ProjectUnderTest>.Tests`, plus the end-to-end test project if there is one.

- **`tests/Directory.Build.props`** (created once) imports the root `Directory.Build.props` and `.repo-baseline/Tests.props`, which provides the shared test packages (TST-1) and marks the projects as non-packable test projects. Local additions include `NoWarn` entries such as `CS0618;NU1701` and packages only this repository needs.
- **`tests/AGENTS.md`** (created once) contains the testing rules. A shared version is deferred (TST-2).
- **Structure** (TST-4): folders and namespaces mirror the production project. For `src/MyProject/Some/Feature/MyClass.cs`:
  - unit tests: `tests/MyProject.Tests/Some/Feature/MyClassTests.cs`
  - component tests for Razor and Blazor components, written with bUnit: `tests/MyProject.Tests/Some/Feature/MyClassComponentTests.cs`
  - integration tests: `tests/MyProject.Tests/Integration/Some/Feature/MyClassIntegrationTests.cs`
- **Shared integration setup**: an NUnit `[SetUpFixture]` in the `MyProject.Tests.Integration` namespace starts shared infrastructure such as Testcontainers once for all integration tests, and never for unit or component tests (TST-4).
- **End-to-end tests** (TST-5) live in `tests/Aspire/MyProject.E2E.Tests` and start the application through the Aspire app host.
- **Categories** (TST-3): component tests carry `[Category("Component")]`, integration tests `[Category("Integration")]` and end-to-end tests `[Category("E2E")]`. Unit tests carry no category. The fast-tests filter in the commands table excludes only `Integration` and `E2E`, so it still runs component tests.

### `benchmarks/`

Optional. Contains BenchmarkDotNet projects.

- **`benchmarks/Directory.Build.props`** imports the root file and replaces multi-targeting with a single target: `<TargetFrameworks></TargetFrameworks>` and `<TargetFramework>$(LatestTarget)</TargetFramework>`.
- **`benchmarks/AGENTS.md`** contains the benchmark rules.
- `BenchmarkDotNet.Artifacts/` is ignored.

### `build/`

Optional. Contains the NUKE project that defines the CI/CD pipeline and additional tooling.

- `build/Nuke.csproj` is an executable project with `NukeRootDirectory` and `NukeScriptDirectory` set to `..`, `IsPackable` and `IsPublishable` set to `false`.
- `build/Directory.Build.props` and `build/Directory.Build.targets` deliberately do **not** import the parent files, so the root properties (warnings as errors, target frameworks) do not apply to the pipeline.
- The pipeline is a partial class `BuildPipeline`, split by concern: `BuildPipeline.cs` (parameters and entry point), `BuildPipeline.Common.cs`, `BuildPipeline.Build.cs`, `BuildPipeline.Test.cs`, `BuildPipeline.Tools.cs`, and depending on the project type `BuildPipeline.NuGet.cs` (packing and publishing packages) or `BuildPipeline.Publish.cs` (publishing applications).
- The `Test` target runs every test project with the XPlat Code Coverage collector and `coverlet.xml` and writes the merged report to `artifacts/test-coverage/coverage.cobertura.merged.xml`.
- `.nuke/parameters.json` names the solution; `.nuke/` is otherwise ignored.
- `build.sh`, `build.cmd` and `build.ps1` at the root bootstrap the pipeline. Use `bash ./build.sh <Target>`.

The pipeline code is repository-specific and not synced.

### `ai-plans/`

Contains Guided Coding plans and plan deviation files, named after the issue number (`0042-short-description.md`). The rules are in the synced `ai-plans/AGENTS.md`.

### `docs/`, `dist/`

Optional. `docs/` contains user and architecture documentation. `dist/` contains distribution assets such as the package icon.

### Ignored directories

- `artifacts/` holds build output, packages and coverage reports.
- `tmp/` is for temporary files of any kind, for people and agents alike.

Both are in the shared `.gitignore` block. `.idea/` is ignored in the local part, which `init` creates (TOOL-4).

## Creating a New Repository

Required inputs:

| Input | Example |
|---|---|
| Name (solution and root namespace) | `Chaos.Example` |
| Project type | application or published library |
| Forge | GitHub or GitLab, including the repository URL |
| Target frameworks | `net10.0`, or a list for libraries |
| Optional parts | `build/`, `benchmarks/`, `docs/`, Aspire (`src/Aspire/`, `tests/Aspire/`) |

Steps:

1. Run `init` with the profiles `common`, `dotnet`, `github` or `gitlab`, and `dotnet-nuget` for published libraries. It writes all synced and created-once files, generates the strong-name key for libraries, and adds `.idea/` and the key's file name to the local part of `.gitignore`.
2. Create the solution `<Name>.slnx` and the projects in `src/` and `tests/`, and add them to the solution.
3. Fill in the local parts: project description and structure in `AGENTS.md`, the project overview in `src/AGENTS.md`, package metadata in `src/Directory.Build.props`, and the package versions in `Directory.Packages.props`.
4. Add `build/` and the CI workflow if needed. For published libraries, store the Base64-encoded strong-name key as a CI secret.
5. Run `dotnet tool restore`, `dotnet build -c Release`, `dotnet test`, `./cleanup_code.sh` and the `check` command. All must succeed before the first commit.
