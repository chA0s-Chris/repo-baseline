# repo-baseline

`repo-baseline` keeps the files that many repositories share in one place: code style and formatter settings, agent instructions, helper scripts, common build properties, and the templates and description of a standard repository layout. A small tool copies this content into repositories and checks in CI that it stays unchanged there.

> **Status:** design phase. The design, the decisions, the first profile content and the `init`, `sync` and `check` commands exist. Until `v1.0.0`, anything may change.

## Why

Shared configuration drifts. Each repository starts as a copy of an older one, and from then on every repository changes its copy on its own. After a while the same rule has three versions, and nobody knows which one is current.

`repo-baseline` makes this repository the only place to change a shared rule. Repositories pin a release, update to newer releases with one command, and fail CI when shared content is edited locally. Deliberate local differences still have an obvious place.

## How It Works

- **Profiles** group the content. Every repository selects the profiles that apply to it, such as `common`, `dotnet`, `dotnet-nuget`, `github` or `gitlab`.
- **Synced content** is owned by `repo-baseline`. It is either a whole file or a managed block inside a file that also contains local content, such as `AGENTS.md` or `.editorconfig`.
- **Created-once content** comes from a template when a repository adopts the baseline. The repository owns it from then on.
- **Described content** follows the layout guide, but each repository creates it by hand.
- A manifest, `.repo-baseline.json`, records the pinned release and the selected profiles.
- The tool has three commands: `init` sets up a repository, `sync` updates synced content to a release, and `check` fails if synced content differs from that release.

Repositories on GitHub and on self-hosted GitLab instances are supported equally. Nothing is fetched at build time: IDEs, command-line tools, CI and agents work with plain files in the repository.

## Usage

The tool is a single-file C# app, `tool/sync.cs`. It needs Git and the .NET 10 SDK or later, and no other packages.

| Command | Effect |
| --- | --- |
| `dotnet run tool/sync.cs -- init <path> <version> --name <Name> …` | Sets up a repository: writes the manifest for `<version>`, the synced content, the created-once files from the templates and, for libraries, a strong-name key. See [Setting up a repository](#setting-up-a-repository). |
| `dotnet run tool/sync.cs -- sync <path>` | Writes the synced content of the release pinned in `<path>/.repo-baseline.json`. |
| `dotnet run tool/sync.cs -- sync <path> <version>` | Syncs release `<version>` and records it in the manifest. |
| `dotnet run tool/sync.cs -- check <path>` | Lists every place where synced content differs from the pinned release, a managed block is missing, unknown or malformed, a synced file is not a regular file inside the repository, a managed file is not valid UTF-8, the ReSharper settings do not use the shared layer, or a shared package has no version. |

`check` exits with `1` when it has findings. All commands exit with `2` on usage, manifest and source errors. `sync` also exits with `2` and writes nothing when a block target file has malformed block markers, when a file the selected profiles use is not valid UTF-8, or when such a file has a symbolic link, a directory or a file in its path where the tool needs a regular file. `--ref <git-ref>` reads the profile content of any Git ref of the `repo-baseline` checkout instead of the pinned release, without changing the manifest.

### Setting up a repository

`init` needs the version to record in the manifest and the inputs from the [layout guide](docs/layout-dotnet.md#creating-a-new-repository):

```bash
cd ~/src/repo-baseline
dotnet run tool/sync.cs -- init ~/src/Chaos.Example 1.0.0 \
    --name Chaos.Example --type library --forge github \
    --repository-url https://github.com/chA0s-Chris/Chaos.Example --target-frameworks 'net10.0;net9.0'
```

| Option | Meaning |
| --- | --- |
| `--name` | Solution name and root namespace: dot-separated C# identifiers. It also names the `.slnx`, its `.DotSettings` file and the strong-name key. |
| `--type` | `application`, or `library` for a published library, which adds the `dotnet-nuget` profile and a strong-name key |
| `--forge` | `github` or `gitlab`, which selects the profile of that name. The repository URL must be on `github.com` for GitHub and on another host for GitLab. |
| `--repository-url` | The `https` URL of the repository, without credentials |
| `--target-frameworks` | One target framework, or a list separated by semicolons |
| `--private` | Leaves out the MIT `LICENSE` and its mention in the file header |
| `--force` | Overwrites existing files, except an existing strong-name key |
| `--ref <git-ref>` | Reads the content of another ref; the manifest still records `<version>`, so `check` needs the same `--ref` until that release is tagged |

`init` never changes an existing file unless `--force` is given. It skips existing files and lists them, and it refuses to run when the target already has a `.repo-baseline.json`. Like `sync`, it exits with `2` and writes nothing when a file it would write has a symbolic link, a directory or a file in its path. The only exception is the strong-name key of a library: `init` adds its file name to an existing `.gitignore`, so that the key cannot be committed by mistake. The key is never printed. Store it as a CI secret as described in the layout guide.

To adopt the baseline in an existing repository, run `init` to add the missing files, then `sync` to update the synced files and blocks that `init` skipped. Add the ReSharper layer injection to an existing `<Name>.slnx.DotSettings` by hand; `check` reports it until then. Review the result and commit it.

Run the tool from a `repo-baseline` clone that is outside the target repository, and with the clone as the working directory. Otherwise the target's `global.json`, `Directory.Build.props`, `Directory.Packages.props` or `NuGet.config` apply to the tool and can break it. A relative `<path>` is resolved against the working directory, which is then the clone.

### Locally

The tool reads each release from the clone's Git objects, so the clone can stay on `main`:

```bash
cd ~/src/repo-baseline
git fetch --tags
dotnet run tool/sync.cs -- sync ~/src/my-repository
```

Review and commit the result in the target repository.

### In CI

Clone the pinned release into a temporary directory outside the workspace and run `check` from there. On GitHub Actions:

```bash
version="$(jq -r .version .repo-baseline.json)"
git clone --quiet --depth 1 --branch "v${version}" https://github.com/chA0s-Chris/repo-baseline.git "${RUNNER_TEMP}/repo-baseline"
cd "${RUNNER_TEMP}/repo-baseline"
dotnet run tool/sync.cs -- check "${GITHUB_WORKSPACE}"
```

On GitLab CI, clone into a new directory from `mktemp -d` instead of `${RUNNER_TEMP}/repo-baseline`, because `/tmp` can outlive a job on shell executors, and use `${CI_PROJECT_DIR}` instead of `${GITHUB_WORKSPACE}`. The snippet reads the version with `jq`, which `inspect_code.sh` needs as well.

## Documentation

- [Design](docs/design.md): profiles, kinds of content, the manifest, sync mechanisms, the tool and the rollout.
- [Decisions](docs/decisions.md): the decisions behind the profile content and the reasons for them.
- [.NET repository layout](docs/layout-dotnet.md): the standard layout of a .NET repository, and how to create a new one.

## License

[MIT](LICENSE)
