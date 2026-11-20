# Chaos.RepoBaseline

`repo-baseline` copies the shared files of [repo-baseline](https://github.com/chA0s-Chris/repo-baseline) into a repository and checks that they stay unchanged there: code style and formatter settings, agent instructions, helper scripts and common build properties. The repository pins a release of these files in `.repo-baseline.json`.

The tool needs the .NET 10 SDK or later, and Git only for `--checkout`.

## Installation

The repository pins the tool in its tool manifest, `dotnet-tools.json`. A new repository has no tool manifest yet, so run `init` once with `dnx`; it creates the manifest with the tool:

```bash
dnx Chaos.RepoBaseline init <path> <version> --name <Name> …
```

An existing repository adds the tool to its manifest instead, creating the manifest first if it has none:

```bash
dotnet new tool-manifest
dotnet tool install Chaos.RepoBaseline
```

Then restore it locally and in CI:

```bash
dotnet tool restore
```

## Commands

| Command | Description |
| --- | --- |
| `dotnet repo-baseline init <path> <version> --name <Name> …` | Sets up a repository with the release `<version>`: the manifest, the synced files and the files created from templates. |
| `dotnet repo-baseline sync <path> [<version>]` | Writes the synced files of the pinned release, or of `<version>`, which it then pins. |
| `dotnet repo-baseline check <path>` | Lists every place where the repository differs from the pinned release. It exits with `1` when it has findings. |

See the [documentation](https://github.com/chA0s-Chris/repo-baseline#readme) for all options.

## License

MIT
