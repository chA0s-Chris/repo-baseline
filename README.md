# repo-baseline

`repo-baseline` keeps the files that many repositories share in one place: code style and formatter settings, agent instructions, helper scripts, common build properties, and the templates and description of a standard repository layout. A small tool copies this content into repositories and checks in CI that it stays unchanged there.

> **Status:** design phase. The design, the decisions and the first profile content exist; the tool does not exist yet. Until `v1.0.0`, anything may change.

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

## Documentation

- [Design](docs/design.md): profiles, kinds of content, the manifest, sync mechanisms, the tool and the rollout.
- [Decisions](docs/decisions.md): the decisions behind the profile content and the reasons for them.
- [.NET repository layout](docs/layout-dotnet.md): the standard layout of a .NET repository, and how to create a new one.

## License

[MIT](LICENSE)
