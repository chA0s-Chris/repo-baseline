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
| `src/RepoBaseline/` | The tool's code: a class library with the `init`, `sync` and `check` commands, described under "Tool" in `docs/design.md` |
| `tool/sync.cs` | The tool's entry point: a file-based app that references `src/RepoBaseline` |
| `tool/pack-profiles.sh` | Packs the archive of a profile release, described under "Versions and distribution" in `docs/design.md` |
| `tests/scripts/` | Tests for the shared code-style scripts |
| `tests/tool/` | Tests for the tool, run through its entry point |
| `tests/RepoBaseline.Tests/` | Unit tests for the tool's code |
| `tests/release/` | Tests for `tool/pack-profiles.sh` |
| `RepoBaseline.slnx` | The solution with the tool's project and its unit tests |
| `renovate/default.json` | The shared Renovate preset, described under "Renovate preset" in `docs/design.md` |
| `renovate.json` | This repository's own Renovate configuration: the shared preset, limited to GitHub Actions |
| `.github/workflows/ci.yml` | CI: runs the commands below on pull requests and pushes to `main`, and packs the profile release archive |
| `.github/workflows/release-profiles.yml` | Publishes a profile release for a pushed `v*` tag |

## Local Development Commands

| Purpose | Command |
| --- | --- |
| Build the tool and run its unit tests | `dotnet test RepoBaseline.slnx -p:TreatWarningsAsErrors=true` |
| Test the shared code-style scripts | `bash tests/scripts/run.sh` |
| Test the tool | `bash tests/tool/run.sh` |
| Test the release packing | `bash tests/release/run.sh` |
| Pack a profile release | `bash tool/pack-profiles.sh <version> <output-directory>` |
| Set up a repository | `dotnet run tool/sync.cs -- init <path> <version> --name <Name> --type application\|library --forge github\|gitlab --repository-url <url> --target-frameworks <tfm>[;<tfm>…]`, optionally with `--private`, `--force` and `--ref <git-ref>` |
| Sync or check a repository | `dotnet run tool/sync.cs -- sync <path>` or `… check <path>`, optionally with `--ref <git-ref>` |
| Validate the Renovate preset and configuration | `npx --yes --package renovate@44.181.5 -- renovate-config-validator --strict --no-global renovate/default.json renovate.json` |

The script tests need bash, Git and `jq`. They run the scripts against a stub `dotnet`, so they cover argument handling, file selection and report filtering, but not ReSharper itself.

The tool needs Git and the .NET 10 SDK or later; its tests also need bash. The tests build fixture `repo-baseline` repositories with a copy of the tool and their own profiles and templates under `tmp/`, so changes to the real profiles do not affect them. Run the tool from this repository, not from inside the target repository.

The projects set their own build settings. Do not add a `Directory.Build.props` or `Directory.Packages.props` to the repository root: it would also apply to the fixtures under `tmp/`. Warnings are errors only with `-p:TreatWarningsAsErrors=true`, as in CI, so that a new compiler warning never stops the tool in a repository that runs it.

The preset validator needs Node.js; `npx` downloads the pinned Renovate version. Keep `--no-global`, which validates the files as repository configuration instead of self-hosted configuration.

The release tests need bash, Git, GNU `tar`, `gzip` and `sha256sum`.

CI runs the unit tests, the three test suites and the validation with these commands, checks the workflows with `actionlint`, and packs the profile release archive as a workflow artifact, with the version `0.0.0-dev.<short commit>`. Keep the workflow and this table in sync.

## Publishing a Profile Release

The maintainer creates a signed, annotated tag `v<version>` on `main`, with the release notes as its message, and pushes it with an explicit refspec (`git push origin refs/tags/v<version>`). The release workflow stops unless the tag is `v` followed by a valid version, runs the CI checks on the tagged commit, and publishes the archive and its checksum as a GitHub release. Versions `0.x` and versions with a pre-release suffix become pre-releases. Agents do not create or push tags unless the maintainer asks for it.

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
