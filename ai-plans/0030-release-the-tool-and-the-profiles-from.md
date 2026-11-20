# Release the tool and the profiles from manually started workflows

> Issue: [#30](https://github.com/chA0s-Chris/repo-baseline/issues/30)

## Rationale

The tool is a .NET tool since #29, but nothing publishes it to nuget.org. Profile releases start from a signed tag that the maintainer pushes. A tag with an invalid version exists before the workflow rejects it, nothing checks that the tag is on `main`, and the rule that no profile release comes before its minimum tool version is on nuget.org is only documented.

The goal is one release process for both: the maintainer starts a workflow with a version, and the workflow checks it, tests the commit, publishes exactly what it tested, and then creates the tag and the GitHub release. A release that fails partway can be completed by a rerun.

## Acceptance Criteria

- [ ] A new workflow, `.github/workflows/release-tool.yml`, is started manually with a `version` input. It publishes `Chaos.RepoBaseline` with that version to nuget.org through trusted publishing, without a stored API key, then creates the tag `tool/v<version>` and a GitHub release without assets.
- [ ] `.github/workflows/release-profiles.yml` is started manually with a `version` input instead of running on pushed tags. It publishes `profiles.tar.gz` and `profiles.tar.gz.sha256` as before and creates the tag `v<version>` itself.
- [x] Both workflows stop before publishing anything unless they run on `refs/heads/main`, the version is SemVer 2.0.0 without build metadata and without a leading `v`, and Git accepts the tag name. They also stop when the tag, the GitHub release or the nuget.org package already exists for another commit.
- [x] The profile workflow also stops unless the version in `tool/minimum-tool-version` is on nuget.org.
- [ ] Both workflows run the checks of `ci.yml` on the commit and publish the archive or package that this CI run built and tested.
- [x] CI packs the tool on every run, with the release version when a release workflow calls it, and the packed-tool test of `tests/tool/run.sh` runs against that package.
- [x] A rerun after a partial failure completes the release of the same commit: it keeps an existing tag and package, and it completes an existing GitHub release without replacing its assets.
- [x] Only versions with a pre-release suffix become GitHub pre-releases. Pre-releases and tool releases are never marked as the latest release.
- [ ] The tag is an annotated tag on the released commit. The GitHub release's notes come from the release-drafter draft of its stream.
- [ ] A new workflow keeps one release-drafter draft per stream up to date on every push to `main`: profiles (tag prefix `v`, pull requests that change `profiles/`) and tool (tag prefix `tool/v`, pull requests that change `src/RepoBaseline/`). The notes group pull requests labelled `enhancement` and `bug` and list the rest separately.
- [x] In the release workflows, only the nuget.org job has `id-token: write` and runs in the GitHub environment `nuget`, and only the tag and release job has `contents: write`. The draft-update workflow has only `contents: write` and `pull-requests: read`.
- [x] The package contains a short readme, set as `PackageReadmeFile`, which nuget.org shows.
- [x] The release logic lives in tested scripts under `tool/release/` that both workflows use. `tests/release/run.sh` covers them without network access, and CI runs it.
- [x] The shared Renovate preset no longer has the package rule with `ignoreUnstable: false`, and the preset validation passes.
- [x] `AGENTS.md`, `docs/design.md` and `docs/decisions.md` (TOOL-5) describe the new release process and pre-release rule; agents still start no release unless the maintainer asks.

## Technical Details

### Release scripts

The scripts follow the pattern of the maintainer's other public repositories: small bash scripts that read their inputs from the environment, so that no workflow expression is interpolated into shell code. A shared library holds the version rule and the remote queries. The scripts cover validation, the preflight checks, the nuget.org push and the tag and release step; one set serves both streams, which differ in tag prefix, assets and extra checks.

The tests stub `gh` on `PATH`, use a local bare repository as `origin`, and point the nuget.org queries at a local fixture through an environment variable (for example `NUGET_FLATCONTAINER_URL`). They need bash, Git, `jq`, GNU `tar`, `gzip` and `sha256sum`, as the existing release tests do.

- **Same commit:** a tag counts as the same release when it points to the dispatched commit. For nuget.org, the preflight reads the served `.nuspec` and compares its repository commit; the project sets `PublishRepositoryUrl` so that the package records it.
- **Validation order:** `git check-ref-format "refs/tags/<tag>"` runs before anything is published, because a version can be valid SemVer but not a valid tag name, such as one ending in `.lock`.
- **Minimum tool version:** the profile preflight looks the version up in nuget.org's flat-container index for `chaos.repobaseline`, after NuGet's version normalization. nuget.org lists a new version in that index only a few minutes after the push; a profile release that fails the check right after a tool release succeeds when rerun later.
- **Rerun:** `dotnet nuget push --skip-duplicate`. Immutable releases are on, so assets cannot be added to a published release. The release step creates the GitHub release as a draft, uploads its assets and publishes it as its last action. The draft is a pre-release until it is published, because release-drafter v7 updates the first non-pre-release draft of its stream whatever its tag, and would otherwise take over a draft left by an interrupted run. A rerun keeps an existing tag on the same commit, completes a draft left by an interrupted run without replacing uploaded assets, and treats a published release as complete.
- **Tag:** an annotated tag by `github-actions[bot]` with a short message such as `Release tool/v0.1.0`. The tag is created only after the nuget.org push, so a failed push leaves no tag.

### CI and the packed tool

`ci.yml` accepts separate `workflow_call` inputs for the tool and profile versions. By default, CI packs the tool as `0.0.0-dev.g<short commit>` and the profile archive as `0.0.0-dev.<short commit>`. The `g` keeps the tool's prerelease identifier valid in SemVer even when the short commit contains only digits and starts with zero. CI uploads the package as a workflow artifact. `tests/tool/run.sh` accepts a package directory and version through the environment. Without them, it packs the tool itself. The other tool tests run the built tool with the version `0.0.0-dev`. The development package has higher precedence than the fixture's minimum tool version of `0.0.0-dev`.

### Release notes

Two release-drafter configurations, one per stream, with `tag-prefix` `v` and `tool/v` and the draft tags `vNext` and `tool/vNext`. release-drafter only considers releases with its prefix, both for the draft it updates and for the previous release, so the streams do not mix. `include-pre-releases` keeps its default, so a pre-release never counts as the previous release. The stream is chosen only by a `type: pre-include` category with `when.paths`; `include-paths` is deprecated in v7. Categories by label only group, they do not include or exclude. A pull request that changes both directories appears in both streams.

The release workflow renders the notes with release-drafter in dry-run mode for the given tag; on a rerun after the release was published, it reuses the published notes, because release-drafter would then find no changes. Without a previous published release in the stream, release-drafter v7 finds no changes and adds a warning to the notes. The release workflows therefore have an optional `from` input, a tag or commit to list the changes from, which release-drafter uses as the comparison base, and the release-notes script refuses notes with that warning. release-drafter cannot run locally, so its configuration is checked by the first draft updates after the merge.

### Permissions and nuget.org

Jobs get only the permissions they need, starting from `permissions: {}`. The job that calls `ci.yml` grants `contents: read`, because a called workflow cannot get more permissions than its calling job. Each release workflow uses a fixed concurrency group, `release-tool` or `release-profiles`, without cancellation. The group must differ from the one `ci.yml` computes when called, because that one uses the caller's workflow name and ref. `NuGet/login` uses the nuget.org user `chAos-Chris` (letter O), which differs from the GitHub account `chA0s-Chris`.

The maintainer sets up outside the code: the GitHub environment `nuget` (no required reviewer), and a nuget.org trusted publishing policy for this repository, `release-tool.yml` and that environment. Renaming the workflow file breaks publishing. Before the first run, the maintainer verifies that trusted publishing can push the first version of a new package ID.

### Package readme

A new, short package readme in `src/RepoBaseline/` is packed with `PackageReadmeFile`: what the tool does, how to pin it in `dotnet-tools.json`, the three commands and a link to the repository. It has no relative links, because nuget.org cannot resolve them.

### Renovate

The preset's package rule with `ignoreUnstable: false` exists only because `0.x` profile releases are GitHub pre-releases. Under the new rule, the GitHub pre-release flag and the version's pre-release suffix agree, so Renovate's default proposes pre-releases only to repositories that already pin one, whichever of the two it reads.

### Rollout

- The first tool release is `0.1.0`, the current minimum in `tool/minimum-tool-version`. The profile preflight enforces that it comes first.
- `v0.5.0` is a GitHub pre-release under the old rule. The maintainer checks whether its flag can still be changed on an immutable release. If not, the first profile release after the merge has no previous release in its stream and is started with `from` set to `v0.5.0`. The first tool release, `tool/v0.1.0`, is started with `from` set to the commit to list the tool's changes from.
- The workflows cannot run before the merge. `actionlint` checks them in CI, the script tests cover the release logic, and the first releases verify the rest.
