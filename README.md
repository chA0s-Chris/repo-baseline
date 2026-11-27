# repo-baseline

`repo-baseline` keeps the files that many repositories share in one place: code style and formatter settings, agent instructions, helper scripts, common build properties, and the templates and description of a standard repository layout. A small tool copies this content into repositories and checks in CI that it stays unchanged there.

## Why

Shared configuration drifts. Each repository starts as a copy of an older one, and from then on every repository changes its copy on its own. After a while the same rule has three versions, and nobody knows which one is current.

`repo-baseline` makes this repository the only place to change a shared rule. Repositories pin a release, update to newer releases with one command, and fail CI when shared content is edited locally. Deliberate local differences still have an obvious place.

## How It Works

- **Profiles** group the content. Every repository selects the profiles that apply to it, such as `common`, `dotnet`, `dotnet-nuget`, `github` or `gitlab`.
- **Synced content** is owned by `repo-baseline`. It is either a whole file or a managed block inside a file that also contains local content, such as `AGENTS.md` or `.editorconfig`.
- **Created-once content** comes from a template when a repository adopts the baseline. The repository owns it from then on.
- **Described content** follows the layout guide, but each repository creates it by hand.
- A manifest, `.repo-baseline.json`, records the pinned release and the selected profiles.
- The tool, a .NET tool, has three commands: `init` sets up a repository, `sync` updates synced content to a release, and `check` fails if synced content differs from that release.

Repositories on GitHub and on self-hosted GitLab instances are supported equally. Nothing is fetched at build time: IDEs, command-line tools, CI and agents work with plain files in the repository.

## Usage

The tool is the .NET tool [`Chaos.RepoBaseline`](https://www.nuget.org/packages/Chaos.RepoBaseline) with the command `repo-baseline`. A repository pins its version in its tool manifest, `dotnet-tools.json`, and runs it with `dotnet repo-baseline` in the repository root after `dotnet tool restore`. The tool needs the .NET 10 SDK or later, because `dotnet tool restore` and local tools are SDK commands, and Git only for `--checkout`.

The tool and the profile content have separate versions: `dotnet-tools.json` pins the tool, and `.repo-baseline.json` pins the profile release. The tool downloads the profile content of that release from its [GitHub release](https://github.com/chA0s-Chris/repo-baseline/releases), verifies its SHA-256 checksum and caches it per version, in `REPO_BASELINE_CACHE` or in the user's cache directory (`~/.cache/repo-baseline`, or `%LOCALAPPDATA%\repo-baseline\cache` on Windows). Releases up to `v0.4.0` have no profile archive. A release that needs a newer tool is refused; update the tool in `dotnet-tools.json` first.

| Command | Effect |
| --- | --- |
| `dotnet repo-baseline init <path> <version> --name <Name> …` | Sets up a repository: writes the manifest for `<version>`, the synced content, the created-once files from the templates and, for libraries, a strong-name key. See [Setting up a repository](#setting-up-a-repository). |
| `dotnet repo-baseline sync <path>` | Writes the synced content of the release pinned in `<path>/.repo-baseline.json`. A synced file or block target that exists only under another case, such as `agents.md` for `AGENTS.md`, is renamed to its name first. A block target that is still the synced file of an earlier release, such as a `CODESTYLE.md` from before it had a block, is replaced with its blocks. |
| `dotnet repo-baseline sync <path> <version>` | Syncs release `<version>` and records it in the manifest. |
| `dotnet repo-baseline check <path>` | Lists every place where synced content differs from the pinned release, a managed block is missing, unknown or malformed, a block target is still the synced file of an earlier release, a synced file or block target exists only under another case, a synced file is not a regular file inside the repository, a managed file is not valid UTF-8, the ReSharper settings do not use the shared layer, a synced `.props` file is not imported by its build file, or a shared package has no version. |

`check` exits with `1` when it has findings. All commands exit with `2` on usage, manifest and source errors, and when a release cannot be downloaded, does not match its checksum or needs a newer tool. `sync` also exits with `2` and writes nothing when a block target file has malformed block markers, when a file the selected profiles use is not valid UTF-8, or when such a file has a symbolic link, a directory or a file in its path where the tool needs a regular file.

`--checkout <path>` reads the profile content of the pinned release from the Git tags of a local `repo-baseline` checkout instead of the GitHub release, and `--ref <git-ref>` together with it reads any ref of that checkout, without changing the manifest. Use them to develop profiles and for pilots before a release.

### Setting up a repository

The quickest way is the agent skill `new-repository` in this repository's Claude Code plugin. It asks for the inputs that `init` needs, runs `init` with the latest release, creates the projects, fills in the local parts, and runs the checks of the layout guide before it asks to create the first commit. It never creates a remote repository or pushes. Install the plugin once, then invoke the skill. It asks for the target directory, which must be new, empty, or a Git repository without commits whose only entry is `.git`, and must not be inside another Git work tree:

```text
/plugin marketplace add chA0s-Chris/repo-baseline
/plugin install repo-baseline@repo-baseline
/repo-baseline:new-repository
```

To update the plugin, run `claude plugin marketplace update repo-baseline` and `claude plugin update repo-baseline@repo-baseline`.

To set up a repository by hand:

A new repository has no tool manifest yet, so run `init` with `dnx`, which needs the .NET 10 SDK or later. Name the tool version, such as the latest version on [nuget.org](https://www.nuget.org/packages/Chaos.RepoBaseline): without it, `dnx` may use the tool version of a `dotnet-tools.json` in a parent directory. `init` needs the version to record in the manifest and the inputs from the [layout guide](docs/layout-dotnet.md#creating-a-new-repository):

```bash
cd ~/src/Chaos.Example
dnx Chaos.RepoBaseline@0.2.0 init . 1.0.0 \
    --name Chaos.Example --type library --forge github \
    --repository-url https://github.com/chA0s-Chris/Chaos.Example --target-frameworks 'net10.0;net9.0'
```

| Option | Meaning |
| --- | --- |
| `--name` | Solution name and root namespace: dot-separated C# identifiers. It also names the `.slnx`, its `.DotSettings` file and the strong-name key, so it must be usable as a file name on Windows. |
| `--type` | `application`, or `library` for a published library, which adds the `dotnet-nuget` profile and a strong-name key |
| `--forge` | `github` or `gitlab`, which selects the profile of that name. The repository URL must be on `github.com` for GitHub and on another host for GitLab. |
| `--repository-url` | The `https` URL of the repository in normalized form, without credentials, query, fragment, whitespace or the characters `& < > " '` |
| `--target-frameworks` | One target framework of the form `net<major>.<minor>`, such as `net10.0`, or a list separated by semicolons without duplicates |
| `--private` | Leaves out the MIT `LICENSE` and its mention in the file header |
| `--force` | Overwrites existing files, except an existing strong-name key |
| `--checkout <path>` | Reads the content from the Git tags of a local `repo-baseline` checkout |
| `--ref <git-ref>` | With `--checkout`, reads the content of another ref; the manifest still records `<version>`, so `check` needs the same options until that release is published |

`init` creates `dotnet-tools.json` with the tool at the version that ran `init`, so later runs use `dotnet tool restore` and `dotnet repo-baseline`.

`init` never changes an existing file unless `--force` is given. It skips existing files and lists them, and it refuses to run when the target already has a `.repo-baseline.json`. A file whose name differs only in case, such as `NuGet.config` for `nuget.config`, counts as existing on every platform. `--force` renames it to the template's name before writing, so that no second file remains; when several such files exist and none has exactly the name, `--force` is refused. Like `sync`, it exits with `2` and writes nothing when a file it would write has a symbolic link, a directory or a file in its path. The only exception is the strong-name key of a library: `init` adds its file name to an existing `.gitignore`, so that the key cannot be committed by mistake. The key is never printed. Store it as a CI secret as described in the layout guide.

To adopt the baseline in an existing repository, run `init` to add the missing files, then `sync` to update the synced files and blocks that `init` skipped. When the repository already has a `dotnet-tools.json`, `init` skips it, so add the tool with `dotnet tool install Chaos.RepoBaseline`. Add the ReSharper layer injection to an existing `<Name>.slnx.DotSettings` and the imports of the synced `.props` files to the existing `Directory.Build.props` files by hand; `check` reports them until then. Review the result and commit it.

### Locally

```bash
cd ~/src/my-repository
dotnet tool restore
dotnet repo-baseline sync .
```

Review and commit the result. To try unreleased profiles, run the tool from a `repo-baseline` clone that is outside the target repository, with `--checkout`:

```bash
cd ~/src/repo-baseline
dotnet run --project src/RepoBaseline -- sync ~/src/my-repository --checkout . --ref main
```

`dotnet run` reads `global.json` from the working directory, so run it from the clone; the target's settings could otherwise break the build of the tool.

### In CI

Restore the tools and run `check` in the repository root, on GitHub Actions and on GitLab CI alike:

```bash
dotnet tool restore
dotnet repo-baseline check .
```

The job needs the .NET 10 SDK or later, not only the runtime, and access to `github.com` for the profile release.

### Updating to a new release

The shared Renovate preset proposes new profile releases in `.repo-baseline.json`, and Renovate's `nuget` manager proposes new tool versions in `dotnet-tools.json`. A profile update changes only the version, so `check` fails on it until the synced content is updated: run `dotnet repo-baseline sync .` on the pull request's branch, review the result and push it. Without Renovate, `dotnet repo-baseline sync . <version>` updates the content and the manifest in one step.

A profile release can need a newer tool. The tool then refuses the release and names the version it needs, so merge the tool update in `dotnet-tools.json` before the profile update.

### Editing shared ReSharper settings

Change shared settings in `repo-baseline`, not in the IDE. Rider saves every change to the solution's own `<Name>.slnx.DotSettings`, never to the injected `.repo-baseline/ReSharper.DotSettings`. A shared setting changed in the IDE therefore becomes a local copy of a shared key, and `check` reports it as a redefined shared key. Editing the `Zorn` cleanup profile copies the whole profile into the local file.

To move a change made in the IDE into `repo-baseline`:

1. Copy the changed key from `<Name>.slnx.DotSettings` into `profiles/dotnet/files/.repo-baseline/ReSharper.DotSettings`, replacing the line with the same key, or adding the key when the shared file does not define it yet. Leave out keys that the IDE added without a change, such as an empty `/Default/CustomTools/CustomToolsData/@EntryValue`.
2. Remove the key from `<Name>.slnx.DotSettings`, so that `check` does not report it as a redefined shared key.
3. Change `docs/decisions.md` when the setting implements a decision, open a pull request in `repo-baseline`, and `sync` the repositories once the change is released.

Settings that the `.editorconfig` block contains are changed in `profiles/dotnet/blocks/editorconfig-dotnet.editorconfig` instead. A setting that only one repository needs stays in its own `<Name>.slnx.DotSettings`, as long as the shared file does not define its key.

## Renovate Preset

`renovate/default.json` holds the Renovate rules that all repositories share. The `renovate.json` that `init` creates references it:

```json
{
  "$schema": "https://docs.renovatebot.com/renovate-schema.json",
  "extends": ["github>chA0s-Chris/repo-baseline//renovate/default"]
}
```

The preset also has a custom manager for the profile release in `.repo-baseline.json`; see [Updating to a new release](#updating-to-a-new-release). Add only local rules next to the reference, such as custom managers and version holds for test images, or further presets. A local `ignorePaths` replaces the preset's list instead of extending it.

The reference has no version, so Renovate reads the preset from `main` and every repository picks up changes to the shared rules on its next run. The preset does not follow the pinned release, because `renovate.json` is created once and `sync` could not keep a pinned reference current. A breaking change gets a new preset path instead.

Renovate on a GitLab instance reads the preset and the profile releases from `github.com` and needs a GitHub token for them: set `RENOVATE_GITHUB_COM_TOKEN`, or add a host rule for `github.com`. The repository is public, so a token without scopes is enough.

## Documentation

- [Design](docs/design.md): profiles, kinds of content, the manifest, sync mechanisms, the tool and the rollout.
- [Decisions](docs/decisions.md): the decisions behind the profile content and the reasons for them.
- [.NET repository layout](docs/layout-dotnet.md): the standard layout of a .NET repository, and how to create a new one.

## License

[MIT](LICENSE)
