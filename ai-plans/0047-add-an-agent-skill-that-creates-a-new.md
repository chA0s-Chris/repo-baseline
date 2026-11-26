# Add an agent skill that creates a new repository with `init`

> Issue: [#47](https://github.com/chA0s-Chris/repo-baseline/issues/47)

## Rationale

`init` sets up the baseline files of a new repository but deliberately creates no projects, CI workflows or `build/` (plan 0003). The layout guide lists the remaining steps, and rollout step 5 of `docs/design.md` assigns them to an agent skill. The skill turns a few answers into a repository that builds, passes its tests and passes `check`.

The skill must not become a second source for the tool's options, validation rules or layout rules. It reads them from the tool's help, the README and the layout guide of the release it uses. That is also why it can be built before `v1.0.0`: layout changes during the pilots need no skill change.

## Acceptance Criteria

- [ ] The repository is a Claude Code plugin marketplace with one plugin, `repo-baseline`, which provides the skill `new-repository`. `claude plugin validate --strict` passes for the repository root and for `plugins/repo-baseline`.
- [ ] Before writing anything, the skill checks for the .NET 10 SDK or later, Git and `curl`, and reports every missing one at once and stops. It resolves the latest stable `Chaos.RepoBaseline` version from nuget.org and uses `Chaos.RepoBaseline@<version>` for both the help call and `init`.
- [ ] The skill asks for the target directory. It accepts a directory that does not exist yet, an empty directory, or a Git repository rooted exactly there whose only entry is `.git`, that has no commits and whose index is empty. It refuses a directory inside another Git work tree, checked at the nearest existing ancestor for a new directory, with the reason. It refuses any other directory with what it found and a pointer to the adoption steps in the README. It writes nothing when it refuses.
- [ ] Before running `init`, the skill asks for the name, project type, forge, repository URL, target frameworks and visibility, and determines the latest profile release instead of asking for the version. It checks the name, the URL form, the match of forge and host, and the target framework list against the options table in `README.md` of that release, and asks again on a mismatch. When `init` exits with `2`, the skill shows the tool's message. When the message concerns one of the inputs, it asks again for that input; otherwise it stops without retrying.
- [ ] After `init` and before creating projects, the skill checks for the SDK that the generated `global.json` selects, the runtime of every target framework, Bash, `jq`, and a configured Git `user.name` and `user.email`, and reports every missing one at once and stops.
- [ ] The skill creates the projects that the user confirms, by default `src/<Name>` and `tests/<Name>.Tests`, and adds them to `<Name>.slnx`. Each test project contains one minimal NUnit smoke test.
- [ ] When the user accepts, the skill creates `docs/README.md`, and `benchmarks/<Name>.Benchmarks` with `benchmarks/Directory.Build.props` and `benchmarks/AGENTS.md`. The benchmarks project is in the solution, has a `PackageVersion` for BenchmarkDotNet and builds in Release, in single-target and in multi-target repositories. The skill lists `build/`, the CI workflow and Aspire as manual steps with a pointer to the layout guide.
- [ ] From a short project description, the skill fills in the local parts:
  - None of the sentences that the release's templates address to the reader remains in `AGENTS.md`, `src/AGENTS.md`, `tests/AGENTS.md` and `README.md`, such as "describe in one or two sentences…", "Describe how to install or use…", "describe the responsibility of each project…" and "Add the … rules that are specific to this repository.". A Rules section with no rules from the description keeps its heading with an empty body.
  - `src/AGENTS.md` has one bullet per created `src/` project.
  - The project structure table in `AGENTS.md` lists `docs/` and `benchmarks/` when the skill created them, and the `benchmarks/` row points to `benchmarks/AGENTS.md`.
  - For libraries, `src/Directory.Build.props` sets the package tags in addition to the project URL and copyright that `init` writes.
  - `Directory.Packages.props` has a `PackageVersion` for every package the created projects reference.
- [ ] Before the verification commands, the skill runs `git init --initial-branch=main` unless the target directory already is the root of a Git repository.
- [ ] The skill runs `dotnet tool restore`, `dotnet restore <Name>.slnx` and `./cleanup_code.sh`, then the Release build and the tests on the cleaned-up tree, then `dotnet repo-baseline check .`. When one of them fails, it stops and reports the failure.
- [ ] The skill creates the first commit only after the user confirms it. It never creates a remote repository, pushes, or stores a CI secret, and lists these as manual steps at the end, including the strong-name key secret for libraries. It never reads or prints the strong-name key; the manual step names the command that Base64-encodes it.
- [ ] Two manual runs of the plugin, installed from the local checkout, against the latest profile release at the time of the run: a public GitHub library with `net10.0;net9.0`, and a private GitLab application with one target framework.
  - The plugin is installed by registering the branch checkout as a local marketplace and installing `repo-baseline@repo-baseline` at user scope. The skill is invoked as `/repo-baseline:new-repository` from a session outside this checkout.
  - Both runs accept `benchmarks/`, and one run accepts `docs/`. One run targets a freshly `git init`-ed empty directory.
  - Each run ends with the Release build and `dotnet repo-baseline check .` exiting with `0`, at least one passing test per target framework, the manifest version equal to the release the skill read, exactly one commit, and no tracked `.snk`. The private run has no `LICENSE`.
  - The skill refuses a non-empty directory and a new directory inside another Git work tree without writing. For `--forge gitlab` with a `github.com` URL, it names the mismatch and asks again before running `init`.
  - A probe with `jq` removed from `PATH` shows that every missing prerequisite is reported at once. The handling of `init` exit code `2` is verified by reading `SKILL.md`.
- [ ] The `benchmarks/` section of `docs/layout-dotnet.md` describes the single-target case: `benchmarks/Directory.Build.props` keeps the root `TargetFramework`.
- [ ] Step 5 of "Creating a New Repository" in `docs/layout-dotnet.md` runs `git init` unless the directory already is a Git repository, then `dotnet tool restore`, `dotnet restore <Name>.slnx`, `./cleanup_code.sh`, the Release build, the tests and the `check` command.
- [ ] CI has a step that validates the marketplace and the plugin, and the commands table in `AGENTS.md` lists the same command.
- [ ] `README.md` shows `/plugin marketplace add chA0s-Chris/repo-baseline`, `/plugin install repo-baseline@repo-baseline` and `/repo-baseline:new-repository`. `AGENTS.md` lists `.claude-plugin/` and `plugins/repo-baseline/` in its project structure table and states that every behavior change bumps `version` in `plugin.json`, minor for new behavior and patch for fixes and wording. Its description of the CI checks and of their Node.js requirement covers the plugin validation. Rollout step 5 in `docs/design.md` names the plugin.

## Technical Details

New files:

- `.claude-plugin/marketplace.json`: the marketplace, with `name` `repo-baseline`, an `owner`, a `description`, and the plugin with the source `./plugins/repo-baseline`. The install command depends on the name.
- `plugins/repo-baseline/.claude-plugin/plugin.json`: the plugin, with `name`, `version` (starting at `0.1.0`), `description`, `author` and `license` (`MIT`).
- `plugins/repo-baseline/skills/new-repository/SKILL.md`: the skill.

For the manual runs, the plugin is installed from the local checkout.

The plugin is served from `main`. It belongs to neither release stream, and `tool/pack-profiles.sh` must not pack it. Installed plugins update only when `version` in `plugin.json` changes, which is why the version rule exists.

Single source:

- The tool's options come from its help. Without `--`, `dnx` prints its own help instead of the tool's, so the call is `dnx <options> Chaos.RepoBaseline@<version> -- --help`. Without a version, `dnx` can pick up a local tool manifest of a parent directory, so both calls name the version resolved from nuget.org. `dnx --help` does not list `--yes` and `dnx` accepts unknown options silently, so the implementation confirms on the .NET 10 SDK that `--yes`, or the equivalent option, makes `dnx` run without a prompt before relying on it. If no such option exists, the skill relies on `dnx` not prompting in a non-interactive shell, which the implementer confirms on the .NET 10 SDK and records in `SKILL.md`.
- The validation rules come from the options table in `README.md` of the selected release, read from `https://raw.githubusercontent.com/chA0s-Chris/repo-baseline/v<version>/README.md`. The tool remains the final check. Exit code `2` also covers download, checksum and tool-version errors, which no new input fixes.
- The latest profile release comes from the GitHub releases API (`repos/chA0s-Chris/repo-baseline/releases/latest`). "Latest" is always a stable profile release, as described under "Publishing a Release" in `AGENTS.md`.
- The steps and the layout rules come from `docs/layout-dotnet.md` of that tag, read from `https://raw.githubusercontent.com/chA0s-Chris/repo-baseline/v<version>/docs/layout-dotnet.md`.
- The skill reads these files with `curl`, because a web-fetch tool may summarize them instead of returning them verbatim.
- The skill describes the workflow and its decisions. It does not copy option lists, validation rules or layout rules. The single-target case of `benchmarks/` and the order of the verification commands are the only exceptions: the skill uses them as fallbacks until the layout guide of a release contains them.

Prerequisites: the .NET SDK that the generated `global.json` selects, the runtimes of every target framework for the tests, Bash and `jq` for the synced scripts, and a configured Git `user.name` and `user.email` for the first commit. The skill reads the SDK version and the target frameworks from the generated files instead of hard-coding them. A target framework that the release's pinned test packages do not support fails the Release build; the skill reports it as an incompatible framework rather than a missing runtime.

`init` needs an existing target, so the skill creates a new target directory after accepting it and the inputs, right before running `init`.

Projects: libraries start from `dotnet new classlib`, applications from `console` by default or another installed template that creates a single C# project and needs no additional workload, and test projects from `dotnet new classlib`. The skill removes from the generated project files every property and package version that the root files or the synced `.props` files already set: `TargetFramework` or `TargetFrameworks`, `ImplicitUsings`, `Nullable`, package versions, and the test packages of `Tests.props`. It removes `Class1.cs` from class libraries; application projects keep their entry point, such as `Program.cs`. Each test project references the project it tests. The benchmarks project references `src/<Name>`, and its `Program.cs` runs `BenchmarkSwitcher` on its assembly, with no benchmarks yet. `benchmarks/AGENTS.md` says how to run the benchmarks in Release.

`cleanup_code.sh` starts with `cd "$(git rev-parse --show-toplevel)"` and selects files through Git, which is why `git init` comes before the verification commands and why a directory inside another work tree is refused. It can rewrite source and project files, which is why the build and the tests run after it.

The skill supports released profiles only. Pilots with unreleased profiles keep using the `--checkout` and `--ref` command from the README.

The skill runs before the target repository exists, which is why it is installed at user level and not distributed through a profile. The plugin and the skill follow the rules for this public repository: no employer or client details.

Test coverage: the skill's behavior is model-driven. Its automated coverage is the validation of the marketplace and the plugin in CI; the manual runs cover the workflow.

CI gets a new step next to the Renovate validation that runs `npx --yes --package @anthropic-ai/claude-code@2.1.332 -- claude plugin validate --strict` for the repository root and for `plugins/repo-baseline`. Validation needs no authentication. The version is pinned like the Renovate validator's. The step also runs in both release workflows through `workflow_call`, like the Renovate validation; that is intended.

Rollout step 5 in `docs/design.md` keeps its position. Its sentence names the plugin and states that the plugin already exists and follows the layout guide of each release, so the position marks when it becomes the standard path, not when it is built.

Verified by PR/MR checks:

- The CI plugin-validation step exits with `0` for the repository root and for `plugins/repo-baseline`.
