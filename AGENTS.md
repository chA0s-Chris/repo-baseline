# Root AGENTS.md

`repo-baseline` is the single source of truth for files that many repositories share: code style and formatter settings, agent instructions, helper scripts, common build properties, and the templates and description of the standard repository layout. A small tool copies this content into repositories and checks that it stays unchanged there.

Read `docs/design.md` before changing anything; it describes profiles, the kinds of content, the manifest, the sync mechanisms and the tool. Repositories pin releases of this content, so a change reaches them only through a new release and their next `sync`.

## Project Structure

| Path | Contents |
| --- | --- |
| `docs/design.md` | How profiles, synced content, managed blocks and the tool work |
| `docs/decisions.md` | The decisions behind the profile content, with their status and reasons |
| `docs/layout-dotnet.md` | The standard layout of a .NET repository that uses the `dotnet` profile |
| `ai-plans/` | Guided Coding plans and plan deviation files |
| `profiles/` | Profile content: one directory per profile with synced files, block fragments and created-once templates, as described under "Profile storage" in `docs/design.md` |
| `src/RepoBaseline/` | The tool: the .NET tool `Chaos.RepoBaseline` with the `init`, `sync` and `check` commands, described under "Tool" in `docs/design.md` |
| `tool/pack-profiles.sh` | Packs the archive of a profile release, described under "Versions and distribution" in `docs/design.md` |
| `tool/minimum-tool-version` | The oldest tool version that can read the profiles, written into each profile release |
| `tool/release/` | The release scripts that both release workflows run, described under "Publishing a Release" |
| `tests/scripts/` | Tests for the shared code-style scripts |
| `tests/tool/` | Tests for the tool, run as a built tool and as a packed .NET tool |
| `tests/RepoBaseline.Tests/` | Unit tests for the tool's code |
| `tests/release/` | Tests for `tool/pack-profiles.sh` and the release scripts |
| `RepoBaseline.slnx` | The solution with the tool's project and its unit tests |
| `renovate/default.json` | The shared Renovate preset, described under "Renovate preset" in `docs/design.md` |
| `renovate.json` | This repository's own Renovate configuration: the shared preset, limited to GitHub Actions |
| `.github/workflows/ci.yml` | CI: runs the commands below on pull requests and pushes to `main`, and packs the profile release archive and the tool |
| `.github/workflows/release-profiles.yml` | Publishes a profile release; started manually with the version |
| `.github/workflows/release-tool.yml` | Publishes a tool release to nuget.org; started manually with the version |
| `.github/workflows/update-release-drafts.yml` | Keeps the release-drafter drafts of both release streams up to date |
| `.github/release-drafter-profiles.yml`, `.github/release-drafter-tool.yml` | The release-drafter configurations of the two release streams |

## Local Development Commands

| Purpose | Command |
| --- | --- |
| Build the tool and run its unit tests | `dotnet test RepoBaseline.slnx -p:TreatWarningsAsErrors=true` |
| Test the shared code-style scripts | `bash tests/scripts/run.sh` |
| Test the tool | `bash tests/tool/run.sh` |
| Test the release packing and the release scripts | `bash tests/release/run.sh` |
| Pack a profile release | `bash tool/pack-profiles.sh <version> <output-directory>` |
| Set up a repository with the profiles of this checkout | `dotnet run --project src/RepoBaseline -- init <path> <version> --name <Name> --type application\|library --forge github\|gitlab --repository-url <url> --target-frameworks <tfm>[;<tfm>…] --checkout .`, optionally with `--private`, `--force` and `--ref <git-ref>` |
| Sync or check a repository with the profiles of this checkout | `dotnet run --project src/RepoBaseline -- sync <path> --checkout .` or `… check <path> --checkout .`, optionally with `--ref <git-ref>` |
| Validate the Renovate preset and configuration | `npx --yes --package renovate@44.181.5 -- renovate-config-validator --strict --no-global renovate/default.json renovate.json` |

The script tests need bash, Git and `jq`. They run the scripts against a stub `dotnet`, so they cover argument handling, file selection and report filtering, but not ReSharper itself.

The tool needs the .NET 10 SDK or later to build, and Git for `--checkout`; its tests also need bash, GNU `tar`, `gzip` and `sha256sum`. The tests build fixture `repo-baseline` repositories with their own profiles and templates under `tmp/`, so changes to the real profiles do not affect them. They run the tool with `--checkout`, read fixture release archives from a cache directory and install the packed tool from a local package source; no test downloads anything. Run the tool with `dotnet run` from this repository, not from inside the target repository, because `dotnet run` reads the `global.json` of the working directory. Without `--checkout`, the tool downloads the profile release from GitHub; a development build has the version `0.0.0-dev` and cannot read releases that need a released tool.

The projects set their own build settings. Do not add a `Directory.Build.props` or `Directory.Packages.props` to the repository root: it would also apply to the fixtures under `tmp/`. Warnings are errors only with `-p:TreatWarningsAsErrors=true`, as in CI, so that a new compiler warning never stops the tool in a repository that runs it.

The preset validator needs Node.js; `npx` downloads the pinned Renovate version. Keep `--no-global`, which validates the files as repository configuration instead of self-hosted configuration.

The release tests need bash, Git, `jq`, GNU `tar`, `gzip` and `sha256sum`. They run the release scripts against stubs of `git`, `gh`, `curl` and `dotnet` with a local bare repository as `origin`, so they need no network access.

CI runs the unit tests, the three test suites and the validation with these commands, checks the workflows with `actionlint`, and packs the profile release archive and the tool as workflow artifacts, with the versions `0.0.0-dev.<short commit>` and `0.0.0-dev.g<short commit>`. The packed-tool test runs against that tool package, which CI passes in `TOOL_PACKAGE_DIR` and `TOOL_PACKAGE_VERSION`; without them, the test packs the tool itself. Keep the workflow and this table in sync.

## Publishing a Release

The tool and the profiles are released by two workflows of the same shape, "Release tool" (`release-tool.yml`) and "Release profiles" (`release-profiles.yml`). The maintainer starts one on `main` with the version, without a leading `v`, such as `0.1.0`. The workflow:

1. Stops unless it runs on `main`, the version is SemVer 2.0.0 without build metadata, and Git accepts the tag name. It also stops when the tag, the GitHub release or the nuget.org package already exists for another commit. The profile workflow also stops unless nuget.org lists the tool version in `tool/minimum-tool-version`.
2. Runs the CI checks of `ci.yml` on the commit and publishes what this CI run packed and tested: the tool package on nuget.org, or the profile archive and its checksum as release assets.
3. Creates the annotated tag, `tool/v<version>` or `v<version>`, on the commit, and then the GitHub release. The release notes come from the release-drafter configuration of the stream. When preparing a new release for a stream with no published stable release, set `from` to the tag or commit from which to list changes. For example, use `v0.5.0` to list profile changes since that prerelease.

Only versions with a pre-release suffix, such as `0.8.0-preview.1`, become GitHub pre-releases. Pre-releases and tool releases are never marked as the latest release, so "Latest" always points to a stable profile release. A run that fails partway is completed by re-running its failed jobs or by starting the workflow again for the same commit; nothing that was published is replaced.

When the profiles need a newer tool, such as for a new template placeholder, the maintainer raises `tool/minimum-tool-version` to the first tool version that supports them, and releases that tool version first. nuget.org lists a new version a few minutes after the push, so a profile release right after a tool release may need to be started again.

The release scripts are in `tool/release/`; the workflows pass every input through the environment. nuget.org trusts `release-tool.yml` and the GitHub environment `nuget` through trusted publishing, so renaming the workflow file breaks publishing. Agents do not create or push tags or start a release unless the maintainer asks for it.

## Rules for This Repository

- **This repository is public.** Never add content specific to an employer or client: project descriptions, internal host names, private package feeds, issue texts or copies of their files. Other repositories may serve as a reference for generic settings only. Never name private repositories or their owners in this repository, its issues or its pull requests.
- Treat decisions with the status **Confirmed** in `docs/decisions.md` as requirements. Change one only when the maintainer revisits it. Treat **Proposed** decisions as the default until they are confirmed or revised.
- When a decision changes, update its entry instead of adding a contradicting one, and change the affected documents and profile files in the same commit.
- Profile content follows the rules for synced content in `docs/design.md`: no variables, no employer or client details, UTF-8 without BOM and LF line endings, and one source per rule.
- Keep `docs/design.md`, `docs/decisions.md` and `docs/layout-*.md` consistent with each other. Reference decisions by their ID (`CS-1`, `BLD-2`, …).
- Write documentation in plain English with short, precise sentences.
- Commit messages follow Conventional Commits.

## Implementation Rules

When implementing an active plan, mark each acceptance criterion as complete only after verifying it. Do not modify historical plans during unrelated work.

## Plan Rules

Read ./ai-plans/AGENTS.md for details on how to write plans.

## Notes from Agents

If you notice something worth recording while working on this code base, do not edit this file. State the finding clearly in your final response and ask the maintainer what to do about it, for example whether to record it in an instruction file, open an issue, or ignore it.
