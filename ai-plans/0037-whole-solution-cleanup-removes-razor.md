# Whole-solution cleanup selects the same file types as scoped cleanup

> Issue: [#37](https://github.com/chA0s-Chris/repo-baseline/issues/37)

## Rationale

`cleanup_code.sh --all` passes the solution to `dotnet jb cleanupcode` without a filter. It therefore cleans Razor files, which TOOL-1 leaves out of scoped cleanup because ReSharper's Razor cleanup does not apply the shared C# rules. In an adopting repository, whole-solution cleanup removed `@using` directives that a component's `@bind-Value` bindings needed, and the build failed with RZ9991. `--all` also cleans plain `.xml` files, which TOOL-1 leaves out as well, and file types that scoped cleanup never selects.

The goal is that both modes of `cleanup_code.sh` clean the same file types and differ only in which files of those types they select.

## Acceptance Criteria

- [ ] `./cleanup_code.sh --all` limits `cleanupcode` to the file types that scoped cleanup selects, so Razor and plain `.xml` files are not cleaned.
- [ ] `cleanup_code.sh` defines the file types once, and both modes use that definition.
- [ ] The script tests expect the `--all` call to include exactly the masks of the shared file types (`**/*.cs;…;**/*.nuspec`), so no Razor or plain `.xml` mask.
- [ ] A manual run of `./cleanup_code.sh --all` with the ReSharper CLI, on a solution with a C# file and a Razor component that both need cleanup, changes the C# file and leaves the component unchanged.
- [ ] TOOL-1 in `docs/decisions.md`, the cleanup paragraph of `profiles/dotnet/blocks/agents-code-style-and-inspections.md` and the `cleanup_code.sh` row in `docs/layout-dotnet.md` describe the new `--all` selection.

## Technical Details

`--all` still passes the solution and adds `--include` with one `**/*.<ext>` mask per file type, for example `--include=**/*.cs;**/*.csproj;…` (illustrative). It still cleans only files that belong to the solution.

TOOL-1 is a Confirmed decision. The maintainer confirmed this change to it while assessing #37.

As a result, `--all` no longer cleans `.cshtml`, CSS, JS, TS, HTML or YAML files. CSS reformatting is already off (CS-2).

The script tests run against a stub `dotnet`, so they cannot check how ReSharper resolves the masks. The manual run uses a throwaway Blazor repository under `tmp/`, set up with `init` for the dotnet profile, then given this branch's `cleanup_code.sh`, followed by `dotnet tool restore`. Its component contains something cleanup is known to change, such as `String` in an `@code` block (TOOL-1). Run `--all` once with the old script to show that the component is rewritten, then with the new one. Record the ReSharper CLI version used, since the issue observed 2026.2.4.

The change reaches repositories with the next profile release and their next `sync`.
