# The tool is a .NET tool that downloads profile releases

> Issue: [#29](https://github.com/chA0s-Chris/repo-baseline/issues/29)

## Rationale

Every repository and every CI job runs the tool from a `repo-baseline` clone at the pinned tag. The CI snippet clones, changes directory and runs `dotnet run`, with a separate variant for GitLab. Tool and profile content share one version, so a tool fix forces a profile release and the reverse.

The goal is that repositories pin the tool in their tool manifest and the profile version in `.repo-baseline.json`, and that the tool gets the profile content of the pinned version from its GitHub release archive (#28) instead of a clone. A local checkout stays available for development and pilots.

## Acceptance Criteria

- [ ] `src/RepoBaseline` packs as a .NET tool with the package ID `Chaos.RepoBaseline` and the command `repo-baseline`; `tool/sync.cs` is removed.
- [ ] By default, the tool downloads `profiles.tar.gz` and `profiles.tar.gz.sha256` of `v<version>` from the GitHub release, verifies the checksum, caches the verified archive per version, and reuses the cache on later runs without downloading.
- [ ] A checksum mismatch, a missing release or a missing asset is an error with exit code `2`, and nothing is cached.
- [ ] `release.json` has format version `2` with a `minimumToolVersion`, which `tool/pack-profiles.sh` reads from the new tracked file `tool/minimum-tool-version`.
- [ ] The tool refuses, with exit code `2`, an archive that needs a newer tool or has an unknown format version, and the message names the required tool version. Archives of format version `1` are accepted and have no minimum.
- [ ] `--checkout <path>` reads the profile content from the Git objects of a local `repo-baseline` checkout, as the tool does today; `--ref` is accepted only together with `--checkout`.
- [ ] The `dotnet` profile's `dotnet-tools.json` template lists `chaos.repobaseline` with the new placeholder `{{ToolVersion}}`, which `init` fills with its own version.
- [ ] `renovate/default.json` has a custom manager for the `version` in `.repo-baseline.json`, using the `github-releases` datasource of `chA0s-Chris/repo-baseline`, and the Renovate validator passes.
- [ ] The README's CI snippet is `dotnet tool restore` followed by `dotnet repo-baseline check .`, the same for GitHub and GitLab.
- [ ] The README and the adoption section of `docs/design.md` show how to run `init` without a tool manifest, with `dnx Chaos.RepoBaseline`, and that an adopted repository with its own `dotnet-tools.json` adds the tool with `dotnet tool install Chaos.RepoBaseline`.
- [ ] The README, `AGENTS.md` and `docs/design.md` describe the distribution and the local checkout option, and `docs/design.md` replaces the open question on distribution with the decision.
- [ ] Unit tests cover the download, checksum verification, cache reuse and re-download, the format and minimum version check, and the version comparison.
- [ ] The tool tests run the built tool with `--checkout`.
- [ ] The tool tests read fixture archives through a cache directory set with `REPO_BASELINE_CACHE`.
- [ ] A tool test installs the packed tool from a local package source through a tool manifest and runs `check` with it.
- [ ] The release tests check format version `2` and the `minimumToolVersion` in `release.json`.

## Technical Details

The project becomes an executable with `PackAsTool`, `ToolCommandName` `repo-baseline` and `PackageId` `Chaos.RepoBaseline`; its target framework and `RollForward=Major` stay. The version defaults to `0.0.0-dev`; #30 sets the real version from its `tool/v*` tag and publishes the package to nuget.org. Without `tool/sync.cs`, there is no implicit checkout any more; development runs `dotnet run --project src/RepoBaseline -- … --checkout .`. Because the installed tool runs from the restored package, the rule that it must run outside the target repository no longer applies to it. The installed tool needs the .NET 10 runtime or later where it runs, and Git only for `--checkout`; the README's requirements say so.

**Ordering with #30.** #30's first tool tag is `tool/v0.1.0`. Until that package is on nuget.org, no profile release is tagged, and existing repositories keep the clone-based CI snippet of their pinned release.

**Sources.** `ReleaseSource` reads its profiles from one entry abstraction (path, executable bit, content), fed either by the existing Git reader or by a new archive reader. The archive reader reads the verified `profiles.tar.gz` in memory with `System.Formats.Tar`, without extracting it. Only entries under `profiles/` are profile content; a non-regular entry there is an error, like a symbolic link in a Git tree. The same path, mode and profile validation applies to both readers. Without `--checkout`, `--ref` is a usage error; `init --ref` therefore needs `--checkout`, too.

**Download.** Assets are read from `https://github.com/chA0s-Chris/repo-baseline/releases/download/v<version>/<asset>` with `HttpClient`, which honors the proxy variables. A missing archive, as for releases up to `v0.4.0`, gives an error that names the version and points to `--checkout`. The checksum file is in `sha256sum` format and must name `profiles.tar.gz`. The checksum detects corrupt or partial downloads; integrity against a changed release rests on GitHub's immutable releases, which the maintainer enables (#28).

**Cache.** `REPO_BASELINE_CACHE` if set; otherwise `$XDG_CACHE_HOME/repo-baseline` or `~/.cache/repo-baseline` on Unix, and `%LOCALAPPDATA%\repo-baseline\cache` on Windows. Each version has its own directory with the archive and its checksum file. A download is verified before it is moved into place through a temporary file and a rename, so concurrent runs never read a partial archive. A cached archive is verified against its checksum file on every use and downloaded again when they differ.

**Minimum tool version.** `tool/pack-profiles.sh` (new: reads `tool/minimum-tool-version`, which starts at `0.1.0`) refuses a value that is not a SemVer 2.0 version. The tool compares its own version, without build metadata, by SemVer 2.0 precedence. A development build (`0.0.0-dev`) therefore cannot read real archives and uses `--checkout`. The fixture checkouts in the tool tests have their own `tool/minimum-tool-version`, so tests cover both an archive the tool can read and one that needs a newer tool.

**Templates.** `{{ToolVersion}}` is the version of the running tool without build metadata. It is a new entry in the placeholder table in `docs/design.md`.

**Renovate.** The custom manager extracts the version from `.repo-baseline.json` and strips the `v` of the release tag. The preset adds a package rule for this dependency with `ignoreUnstable: false`, because the release workflow marks `0.x` releases as GitHub pre-releases, which Renovate would otherwise skip; versions with a pre-release suffix are proposed as well. The custom manager's update pull request changes only the manifest version, so `check` fails until `sync` runs on that branch; the README says so. The tool version in `dotnet-tools.json` is updated by Renovate's built-in `nuget` manager. Renovate on GitLab needs the GitHub token that the README already describes.

**Tests.** No test needs network access: the unit tests use a stubbed `HttpMessageHandler`, and the tool tests pack fixture archives with `tool/pack-profiles.sh` in a fixture checkout and copy them into the cache directory. The fixture checkouts no longer need a copy of the tool; the tool is built once and run with `--checkout`. The packed-tool test replaces the shallow tag clone test. It restores into its own `NUGET_PACKAGES` directory under `tmp/`, so it never reuses a package from an earlier run.
