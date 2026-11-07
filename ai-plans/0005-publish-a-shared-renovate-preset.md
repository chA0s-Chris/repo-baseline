# Publish a shared Renovate preset

> Issue: [#5](https://github.com/chA0s-Chris/repo-baseline/issues/5)

## Rationale

The Renovate configurations of the adopting repositories repeat the same rules and have already drifted: group names differ, one test group is incomplete, and only one repository groups GitHub Actions. The `renovate.json` template already extends `github>chA0s-Chris/repo-baseline//renovate/default`, but that preset does not exist, so every repository set up with `init` has a broken Renovate configuration. This blocks the pilot (#4).

The goal is to publish the shared rules once, so each repository keeps only its local rules.

## Acceptance Criteria

- [ ] `renovate/default.json` (new) contains exactly the shared rules listed in Technical Details.
- [ ] `renovate-config-validator` passes for `renovate/default.json`, and its pinned command is listed under Local Development Commands in `AGENTS.md`.
- [ ] In scratch clones of Chaos.Mongo and ShadowDrop, the resolved repository configuration of a `renovate.json` reduced to the preset plus its local rules matches that of the current one, apart from the intended differences listed in Technical Details.
- [ ] The README explains how to reference the preset, that it follows `main`, and that Renovate on a GitLab instance needs a GitHub token (`RENOVATE_GITHUB_COM_TOKEN` or a `github.com` host rule) to read it.
- [ ] `docs/design.md` ("Renovate preset") states what the preset contains and that repositories follow `main`, and `AGENTS.md` no longer says the preset does not exist.
- [ ] `docs/decisions.md` records the preset as a new Confirmed decision TOOL-5: what it contains and what stays local, that repositories follow `main`, and why `ignorePaths` drops `**/tests/**`.

## Technical Details

The preset contains:

- `config:recommended`.
- `major.dependencyDashboardApproval: true`.
- `addLabels: ["renovate"]`. `addLabels` rather than `labels`, so labels a repository adds itself are kept.
- `ignorePaths`: Renovate's default list without `**/tests/**`, with a `description` explaining why. Custom managers for test images under `tests/` (TST-4) cannot see their files otherwise.
- Package rules:
  - NuGet `rangeStrategy: bump`.
  - FluentAssertions `allowedVersions: "<8"` (TST-1).
  - Group "NUnit": `NUnit`, `NUnit.Analyzers`, `NUnit3TestAdapter`.
  - Group "Test SDK": `Microsoft.NET.Test.Sdk`, `Microsoft.TestPlatform.TestHost`, `Microsoft.Testing.Platform`, `Microsoft.Testing.Extensions.CodeCoverage`.
  - Group "GitHub Actions" for the `github-actions` manager; it has no effect on GitLab or in repositories without workflows.

Custom managers and version holds for test images, the dashboard approval of minimum dependency versions in Chaos.Mongo, and `helpers:pinGitHubActionDigests` stay local.

Intended differences against the current resolved configurations:

- The group names are unified, so open grouped pull requests get new branch names.
- Chaos.Mongo gains the GitHub Actions group and loses its `skip-changelog` label. Both release-drafter configurations already exclude pull requests with the `renovate` label.
- In Chaos.Mongo, the test SDK group gains the two `Microsoft.Testing` packages.

Repositories follow the default branch, so the `renovate.json` template stays unchanged. The file is created once, so `sync` could not keep a pinned tag current. A breaking change to the preset would get a new preset path instead.

This repository has no Node manifest, so the validator runs through `npx` with a pinned `renovate` version. The comparison runs the same version in each clone with `--platform=local --dry-run=lookup` and `LOG_LEVEL=debug`, once with the current and once with the reduced `renovate.json`, and compares the resolved repository configuration from the debug log. Until the preset is on `main`, the reduced configuration references the pushed plan branch (`…//renovate/default#0005-publish-a-shared-renovate-preset`). Fetching the preset needs network access to GitHub; `RENOVATE_GITHUB_COM_TOKEN` avoids the anonymous rate limit. If Renovate cannot resolve the branch reference, the reduced configuration inlines the preset's content instead. That still proves the rules are equivalent, and the pilot (#4) checks the reference itself.

The pinned validator is the automated check for this change. The comparison is a one-time manual check of equivalence.

Changing the real `renovate.json` files of the pilot repositories is part of #4.
