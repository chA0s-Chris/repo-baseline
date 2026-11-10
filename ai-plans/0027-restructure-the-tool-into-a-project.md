# The tool's code is a project split by concern

> Issue: [#27](https://github.com/chA0s-Chris/repo-baseline/issues/27)

## Rationale

The tool is a single file, `tool/sync.cs`, of about 2,550 lines. The command-line handling, profile reading, block parsing and merging, target path checks and the three commands share one file, which makes changes hard to review and logic hard to test in isolation. It also cannot become a .NET tool (#29) in this form.

The goal is that the code lives in a project split by concern, with unit tests for its core logic, while the tool behaves exactly as before.

## Acceptance Criteria

- [ ] The tool's code is in the new project `src/RepoBaseline/RepoBaseline.csproj`, with the root namespace and assembly name `Chaos.RepoBaseline`, split into files by concern: command-line handling and options, manifest, release source and profiles, expected content, block parsing and merging, target paths and file writes, and one file per command.
- [ ] `tool/sync.cs` only references the project and calls its entry point; `dotnet run tool/sync.cs -- …` behaves as before.
- [ ] The tool tests pass without changes to their expectations; the fixtures copy the project as well and ignore its build output.
- [ ] A new unit test project, `tests/RepoBaseline.Tests`, with the assembly name and root namespace `Chaos.RepoBaseline.Tests`, covers block parsing and merging and the case-variant lookup, and CI runs it.
- [ ] A new `RepoBaseline.slnx` contains both projects.
- [ ] The README, `AGENTS.md` and `docs/design.md` describe the new structure, and `AGENTS.md` lists the build and unit test command that CI runs.

## Technical Details

`tool/sync.cs` stays a file-based app that references the project with `#:project ../src/RepoBaseline/RepoBaseline.csproj` and calls `Chaos.RepoBaseline.Cli.Run(args)`. The project is a class library; only `Cli` is public, everything else stays internal and is visible to the test assembly through `InternalsVisibleTo`. Locating the `repo-baseline` checkout through the entry point's file path keeps working, so the commands in the README, the CI snippet of adopting repositories and the tool tests do not change. #29 turns the project into the .NET tool and replaces the entry point.

Building the project writes `bin/` and `obj/` next to its sources, so `.gitignore` ignores both.

The move changes no behavior. Code is moved, not rewritten; renames are limited to what the split into files and the namespace require.

The projects set their build settings themselves (nullable, implicit usings) instead of importing a root `Directory.Build.props`. A root `Directory.Build.props` or `Directory.Packages.props` would also apply to the test fixtures under `tmp/` and change their builds. For the same reason, the unit test project keeps its package versions in its project file. It uses NUnit and FluentAssertions 7, as TST-1 prescribes. Renovate does not update the test packages yet; enabling its `nuget` manager for this repository is a separate decision.

Warnings are errors only in this repository's CI, which builds `RepoBaseline.slnx` with `-p:TreatWarningsAsErrors=true`; running the tool never fails on a compiler warning, also not in the CI of an adopting repository.

The package ID `Chaos.RepoBaseline` and making the project a .NET tool are part of #29.
