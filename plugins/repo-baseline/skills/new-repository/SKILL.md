---
name: new-repository
description: Creates a new .NET repository with repo-baseline. Runs `init` with the latest profile release, creates the projects, fills in the local parts, and verifies that the repository builds, passes its tests and passes `check`. Use when the user wants to start a new repository or project with the repo-baseline layout.
---

# New Repository

Create a new .NET repository that follows the repo-baseline layout. The result builds in Release, passes its tests and passes `dotnet repo-baseline check .`, and has one commit that the user confirmed.

## Sources of truth

The tool's options, its validation rules and the layout rules change between releases. Never take them from memory or from this skill. Read them in every run from the release the run uses:

| What | Source |
| --- | --- |
| Profile release | `curl -fsSL https://api.github.com/repos/chA0s-Chris/repo-baseline/releases/latest`, field `tag_name`. "Latest" is always a stable profile release. |
| Tool version | The NuGet search service: the `@id` of a `SearchQueryService` resource in `curl -fsSL https://api.nuget.org/v3/index.json`, queried with `?q=packageid:Chaos.RepoBaseline&prerelease=false&semVerLevel=2.0.0`, field `data[0].version`. Unlike the flat container, it returns only listed versions. |
| Tool options | `dnx --yes Chaos.RepoBaseline@<tool-version> -- --help` |
| Validation rules | The options table under "Setting up a repository" in `https://raw.githubusercontent.com/chA0s-Chris/repo-baseline/<tag>/README.md` |
| Steps and layout rules | `https://raw.githubusercontent.com/chA0s-Chris/repo-baseline/<tag>/docs/layout-dotnet.md`, mainly "Creating a New Repository" and the sections of the files and directories the run creates |

Rules for these sources:

- Read the files with `curl` into a temporary directory outside the target. A web-fetch tool may summarize a page instead of returning it verbatim.
- Always name the tool version. Without it, `dnx` may use a local tool manifest of a parent directory. Resolve the version before the first `dnx` call and write it literally into each command, because shell variables do not persist between commands.
- Stop when a lookup returns no release or no version, or a download fails, instead of continuing without it.
- `--yes` skips the download confirmation of `dnx`. Without `--`, `dnx` prints its own help instead of the tool's.
- Run `dnx` from that temporary directory, so that no `global.json` of the target or its parents selects an older SDK.
- The tool remains the final check of every input.

Only two rules have fallbacks here, because the layout guide of older releases states them incompletely. Each fallback names when the guide replaces it:

- **`benchmarks/Directory.Build.props`:** it imports the root file and sets `IsPackable` to `false`, so the pipeline does not pack the benchmarks. In a repository with one target framework, it keeps the root `TargetFramework`. Only a multi-target repository replaces `TargetFrameworks` with `$(LatestTarget)`. Follow the guide instead only when its `benchmarks/` section describes the single-target case and `IsPackable`.
- **Order of the final checks:** `git init`, then `dotnet tool restore`, `dotnet restore <Name>.slnx`, `./cleanup_code.sh`, the Release build, the tests and `dotnet repo-baseline check .`. `cleanup_code.sh` selects files through Git and can rewrite source and project files, so it needs a Git repository and runs before the build. Follow the guide instead only when its step 5 runs `git init` and `./cleanup_code.sh` before the build.

## Boundaries

- Before the target directory and every input are accepted, write only to the temporary directory. Afterwards, write only to the target directory and the temporary directory. Only the tools write elsewhere: `dnx` caches the tool, and `init` and `check` cache the profile release.
- Never create a remote repository, push, or store a CI secret. List these as manual steps at the end.
- Never read, print or encode the strong-name key `<Name>.snk`.
- Create the first commit only after the user confirms it.
- Ask questions with the host's question tool when it has one. Take answers that the user's request already contains and ask only for the rest.

## Steps

### 1. Check the tools needed before `init`

Check for the .NET 10 SDK or later (`dotnet --list-sdks`), Git and `curl`, which steps 2 and 3 need. Report every missing one at once and stop.

### 2. Read the release

Resolve the profile release and the tool version, read the README and the layout guide of the release, and read the tool's help, all as described under "Sources of truth".

### 3. Accept the target directory

Ask for the target directory and resolve it to an absolute physical path, without symbolic links: for an existing directory with `pwd -P` in it, for a new one by resolving its nearest existing ancestor the same way and appending the missing components. Refuse a new target whose missing components contain `.` or `..`. Accept only:

- a directory that does not exist yet,
- an empty directory, or
- a Git repository rooted exactly at the target (`git -C <target> rev-parse --show-toplevel` prints the target's physical path) whose only entry is `.git`, that has no commits (`git -C <target> rev-parse -q --verify HEAD` fails and `git -C <target> for-each-ref` prints nothing) and whose index is empty (`git -C <target> ls-files` prints nothing).

Refuse, and write nothing:

- A directory inside another Git work tree. Run `git -C <dir> rev-parse --show-toplevel` on the target's parent, or on its nearest existing ancestor when the parent does not exist yet. When it prints a path, name that repository as the reason. When it fails with any error other than "not a git repository", stop and show Git's message.
- Any other directory. List what it contains and point to the paragraph about adopting the baseline in an existing repository under "Setting up a repository" in the README.

Then check for Bash, `jq`, and a Git `user.name` and `user.email`: with `git -C <target> config` when the target is a Git repository, otherwise with `git -C <temporary directory> config`. Report every missing one at once and stop. Step 6 checks them again, but this check stops the run before `init` writes the target. An identity that only an `includeIf` rule sets for the target cannot be seen before `git init`; when the user confirms one, continue, and step 6 checks it.

### 4. Collect the inputs

Ask for everything the run needs, so that only the commit confirmation interrupts the rest of the run:

- The inputs of `init` from "Creating a New Repository" in the layout guide: name, project type, forge, repository URL, target frameworks and visibility. Propose the target directory's name as the name.
- A short description of the project: what it contains and who it is for.
- The projects. Propose `src/<Name>` and `tests/<Name>.Tests`. For an application, the production project uses `console` or another installed `dotnet new` template. Accept another template only when `dotnet new <template> -o <temporary directory> --dry-run` shows that it creates exactly one C# project and needs no workload. The user may rename projects or add more pairs of a production project and its test project.
- Whether to create `docs/` and `benchmarks/`.

Check the name, the URL form, the match of forge and host, and the target framework list against the options table of the release's README. On a mismatch, name it and ask again for that input.

### 5. Run `init`

Create the target directory when it does not exist yet, because `init` needs an existing directory. Then run `init` from the temporary directory, with the option names that the tool's help printed in step 2. `<release-version>` is the tag without its leading `v`. With the options of the current tool, the command is:

```bash
dnx --yes Chaos.RepoBaseline@<tool-version> init "<target>" <release-version> --name <Name> --type <type> \
    --forge <forge> --repository-url "<url>" --target-frameworks '<tfm>[;<tfm>…]' [--private]
```

When `init` exits with `2`, show its message. When the message concerns one of the inputs, ask again for that input and run `init` again. Otherwise stop without retrying: exit code `2` also covers download, checksum and tool-version errors, which no new input fixes. On any other non-zero exit, show the output and stop without retrying. When the run stops after this step created directories, remove those that are still empty, from the target up to the nearest directory that existed before.

When `init` succeeds and the target is not yet the root of a Git repository, run `git init --initial-branch=main` in it.

### 6. Check the tools needed after `init`

In the target directory, check for:

- The SDK that the generated `global.json` selects: `dotnet --version` in the target fails without it.
- The runtime of every target framework, in `dotnet --list-runtimes`.
- Bash and `jq`, which the synced scripts need.
- A Git `user.name` and `user.email`, from `git -C <target> config`.

Report every missing one at once and stop. The user can fix them and let the run continue from this step. To start over instead, the user empties the target or chooses another directory, because step 3 refuses a directory that `init` has written to.

### 7. Create the projects

In the target directory, create each production project with `dotnet new classlib`, or with the confirmed template for an application, and each test project with `dotnet new classlib`, under `src/` and `tests/`. Pass `--no-restore` to each `dotnet new`: with central package management, the template's restore fails until its package versions are moved, and step 10 restores the solution. Then:

- Remove from each project file every property and package that the root files or the synced `.props` files already set, such as `TargetFramework`, `TargetFrameworks`, `ImplicitUsings`, `Nullable` and the test packages of `.repo-baseline/Tests.props`. Move the version of every other `PackageReference` into a `PackageVersion` in `Directory.Packages.props`. Write the project files as UTF-8 without BOM.
- Remove `Class1.cs` from class libraries. An application keeps its entry point, such as `Program.cs`.
- Add a `ProjectReference` from each test project to the project it tests, and one minimal NUnit smoke test, for example:

  ```csharp
  namespace <Name>.Tests;

  using NUnit.Framework;

  [TestFixture]
  public class SmokeTests
  {
      [Test]
      public void Runs() => Assert.Pass();
  }
  ```

- Add every project to the solution with `dotnet sln <Name>.slnx add`.

### 8. Create the optional parts

- `docs/`: create `docs/README.md` with one sentence on what the directory will contain.
- `benchmarks/`: follow the `benchmarks/` section of the layout guide, or the `benchmarks/Directory.Build.props` fallback under "Sources of truth". Create `benchmarks/<Name>.Benchmarks`, an executable project that references the production projects confirmed in step 4, by default `src/<Name>`, and `BenchmarkDotNet`. Its `Program.cs` runs `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);` and contains no benchmarks yet. `benchmarks/AGENTS.md` says how to run the benchmarks in Release. Add the project to the solution.

### 9. Fill in the local parts

From the description and the created projects, edit the files that the layout guide lists for step 3 of "Creating a New Repository":

- Replace every sentence that the templates address to the reader in `AGENTS.md`, `src/AGENTS.md`, `tests/AGENTS.md`, `README.md` and `CODESTYLE.md`, such as "describe in one or two sentences…" or "Add the … rules that are specific to this repository.". A Rules section, or the "Repository-Specific Rules" section of `CODESTYLE.md`, that the description gives no rules for keeps its heading with an empty body.
- `src/AGENTS.md` has one bullet per `src/` project with its responsibility.
- The project structure table of `AGENTS.md` lists `docs/` and `benchmarks/` when they exist, and the `benchmarks/` row points to `benchmarks/AGENTS.md`.
- For a library, `src/Directory.Build.props` sets `PackageTags`. `init` already wrote the project URL and the copyright.
- `Directory.Packages.props` has a `PackageVersion` for every package the projects reference. Keep the versions that step 7 moved there. For a package that has none yet, such as BenchmarkDotNet, take the latest stable listed version from the NuGet search service, queried as for the tool version.

Leave the managed blocks (`<!-- BEGIN repo-baseline: … -->` to `<!-- END … -->`) unchanged.

### 10. Verify

The target is a Git repository since step 5. Run the final checks in the order that "Sources of truth" selects:

```bash
dotnet tool restore
dotnet restore <Name>.slnx
./cleanup_code.sh
dotnet build -c Release <Name>.slnx
dotnet test <Name>.slnx
dotnet repo-baseline check .
```

When one fails, stop and report its output. Report a build error for a target framework that the release's test packages do not support as an incompatible target framework, not as a missing runtime. After a fix, run the checks again from `dotnet tool restore`.

### 11. Commit

Show `git status --short`, confirm that no `.snk` is listed, and ask the user to confirm the first commit. Propose a short message, for example `chore: set up the repository with repo-baseline <release-version>`, and show the complete message with the question, including any trailer that the host adds. Commit only after the confirmation.

### 12. Report

When the run stops before step 11, report what it did, why it stopped and how to continue, without the manual steps below.

After step 11, summarize the release and tool version, the inputs, the created projects and parts, the results of the final checks, and whether the commit was created. List the manual steps:

- Create the remote repository and push.
- For a library, store the Base64-encoded strong-name key as a CI secret, as the layout guide describes. Encode it with `base64 -w0 <Name>.snk` on Linux or `base64 -i <Name>.snk` on macOS, and do not commit it.
- Add the parts that the skill does not create, as the layout guide describes them: `build/`, the CI workflow, and Aspire.
