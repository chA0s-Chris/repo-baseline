# CI runs the tool tests, script tests and preset validation

> Issue: [#26](https://github.com/chA0s-Chris/repo-baseline/issues/26)

## Rationale

The repository has no CI workflow. The tool tests, the script tests and the Renovate preset validation run only when someone runs them locally, so a pull request can be merged without them. The upcoming restructuring of the tool (#27) needs these tests as a safety net.

The goal is that every pull request to `main` and every push to `main` runs these checks.

## Acceptance Criteria

- [x] A new workflow, `.github/workflows/ci.yml`, runs on pull requests to `main` and on pushes to `main`.
- [x] It runs `bash tests/tool/run.sh`, `bash tests/scripts/run.sh` and the preset validation command from `AGENTS.md`, and fails when any of them fails.
- [x] It checks the repository's workflows with `actionlint`.
- [x] A new `renovate.json` extends the shared preset and limits Renovate to the `github-actions` manager, so the action tags get update pull requests.
- [x] The workflow passes on the pull request that adds it.
- [x] `AGENTS.md` says that CI runs these commands, and its preset validation command also validates `renovate.json`.

## Technical Details

One job on `ubuntu-latest` with `contents: read` permissions. It sets up .NET `10.0.x` and Node.js 24, which Renovate 44 needs; bash, Git and `jq` come with the runner. Actions are pinned to major tags, as in the maintainer's other repositories: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/setup-node@v7` and `raven-actions/actionlint@v2`.

Renovate keeps the action tags current through `renovate.json`, which extends `github>chA0s-Chris/repo-baseline//renovate/default` and sets `enabledManagers` to `github-actions`. The maintainer enables the Renovate app for this repository. The preset validation in CI also validates `renovate.json`. Versions in the profiles and the Renovate version of the validation command stay outside Renovate for now.

The steps call the commands documented in `AGENTS.md`; the workflow adds no test logic of its own. A new push to a pull request cancels the run of that pull request that is still in progress.

The tests run on Linux only. They use GNU tools such as `sed -i` and `stat -c`, so a macOS job would first need portable tests; that is a separate task.

After the first green run on `main`, the maintainer marks the job as a required check in the repository settings. That is a settings change, not part of the pull request.
