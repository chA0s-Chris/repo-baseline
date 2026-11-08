# Pilot the baseline in Chaos.Mongo and ShadowDrop

> Issue: [#4](https://github.com/chA0s-Chris/repo-baseline/issues/4)

## Rationale

Before `v1.0.0`, the baseline has to work in two public repositories of different types: Chaos.Mongo, a published library, and ShadowDrop, an application with an older state in many places. The pilot also answers the open question in TOOL-3: which settings layer Rider saves to when shared ReSharper settings are edited in the IDE.

Most of the work happens in the pilot repositories themselves. This plan covers what `repo-baseline` contributes: the pre-release the pilots pin, an adoption issue in each pilot repository, the Rider findings and the editing workflow, fixes for problems the pilot finds, and the `v1.0.0` release.

## Acceptance Criteria

- [x] The pre-release tag `v0.1.0` exists on `main` before adoption starts.
- [x] Chaos.Mongo and ShadowDrop each have an adoption issue in their own repository, linked from #4, that lists that repository's drift from #4 and from the findings comment on #4, and the reduction of its `renovate.json` to the shared preset plus its local rules (#5).
- [ ] Both adoption issues are closed with their work done: each repository has `.repo-baseline.json` and passes `check` in CI, builds in Release without warnings, passes its test suite, has its drift resolved or recorded with a reason, and lists its reformatting commits in `.git-blame-ignore-revs`.
- [ ] TOOL-3 records the Rider findings: whether the `RelativePath` form of the injection works, which layer Rider saves an edited shared setting and an edited cleanup profile to, and that `check` reports a shared key redefined in the local file. The README explains how to edit shared ReSharper settings and how to move such a change into `repo-baseline`.
- [ ] Problems the pilot finds in the profile content or the tool are fixed through separate issues in this repository, or recorded in follow-up issues linked from #4.
- [ ] `README.md` and `AGENTS.md` no longer describe the design phase.

## Technical Details

This plan's branch holds only the TOOL-3 findings, the README editing workflow and the status changes. Its pull request opens once both adoption issues are closed, as the last step before the release. Fixes go through their own issues and pull requests, so that a new pre-release tag (`v0.2.0`, …) can carry them to the pilots. The maintainer creates all tags on `main`. After this plan's pull request is merged, the maintainer tags `v1.0.0`, which closes #4. The pull request therefore references the issue with `Refs #4` instead of `Closes #4`.

Each pilot adopts the baseline as described in the README: `init` with the pinned version, then `sync`, and the layer injection added to the existing `<Name>.slnx.DotSettings` by hand. CI runs `check` with the snippet from the README. Both repositories are on GitHub.

Each adoption issue re-checks its drift list against the current state first, because some items may be outdated: Chaos.Mongo now targets `net10.0` only, and ShadowDrop already has a root `dotnet-tools.json` (BLD-5).

The maintainer runs the Rider test by hand in Chaos.Mongo. `check` already reports redefined shared keys; the pilot confirms this in a real repository.

Out of scope: adoption in further repositories, `tests/AGENTS.md` (TST-2), and moving the pilots' pin to `1.0.0` after the release.
