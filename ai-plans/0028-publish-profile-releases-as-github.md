# Profile releases are published as GitHub release archives

> Issue: [#28](https://github.com/chA0s-Chris/repo-baseline/issues/28)

## Rationale

A profile release is only a Git tag. The tool reads the profile content from the Git objects of a `repo-baseline` clone, so every user and every CI job needs a clone. The tool that #29 distributes separately needs a way to get the profile content of a version without cloning, and a release should be checked before it is published.

The goal is that every new `v*` tag produces a tested GitHub release with an archive of the profile content and its checksum.

## Acceptance Criteria

- [ ] A new workflow, `.github/workflows/release-profiles.yml`, runs on pushed `v*` tags. It runs the same checks as CI and then publishes a GitHub release for the tag with `profiles.tar.gz` and `profiles.tar.gz.sha256` as assets.
- [ ] CI builds the archive and its checksum on every run with `tool/pack-profiles.sh` and uploads them as workflow artifacts, so a pull request shows the archive a release would publish.
- [x] A new script, `tool/pack-profiles.sh <version> <output-directory>`, builds the archive. It contains `profiles/`, the `LICENSE` and a generated `release.json` with the profile version and a format version. It keeps executable bits, and building the same commit twice gives byte-identical archives.
- [ ] Versions `0.x` and versions with a pre-release suffix are published as GitHub pre-releases. The release notes come from the annotated tag's message.
- [x] Tests for the script in `tests/release/run.sh`, listed in `AGENTS.md`, check the archive's content, its executable bits, `release.json`, the checksum file and the byte-identical rebuild, and CI runs them.
- [x] `docs/design.md`, the README and `AGENTS.md` describe profile releases and how the maintainer publishes one.

## Technical Details

`ci.yml` becomes callable with `workflow_call`, so the release workflow runs exactly the CI checks before it publishes. Only the publishing job gets `contents: write`.

The archive is deterministic: `tar` with sorted names, the commit time as modification time, and owner and group 0. The archive is compressed with `gzip -n`, and file modes are normalized to 644 and 755, so neither the time of the build nor the umask changes it. The checksum file uses the `sha256sum` format. `release.json` holds the version from the tag without the `v` prefix and a format version of `1`. #29 adds the minimum tool version to `release.json` together with the tool's check and raises the format version.

The maintainer keeps creating signed `v*` tags; the workflow only reacts to them and stops unless the tag is `v` followed by a version that the tool accepts. Tool tags (`tool/v*`, #30) do not match `v*`. Existing tags up to `v0.4.0` get no archive; the next tag is the first with one, so #29 can test against a real archive before `v1.0.0`.

CI passes the version to the script: the tag's version when the release workflow calls it, otherwise `0.0.0-dev.<short commit>`. The release job publishes the archive and checksum that this CI run built, so the published archive is exactly the tested one. CI's archive on the pull request verifies the checks and the packing. The first real publish, with its pre-release flag and release notes, is verified with the next tag after the merge.

The tool keeps reading Git tags until #29 switches it to the archive. Immutable releases are a repository setting that the maintainer turns on.
