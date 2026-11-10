# The tool tests start the built entry point

> Issue: [#34](https://github.com/chA0s-Chris/repo-baseline/issues/34)

## Rationale

Since #27, `tool/sync.cs` references `src/RepoBaseline` with `#:project`. For a file-based app with a project reference, `dotnet run` has no fast path, so every call runs an MSBuild up-to-date check: a warm call takes about 1.5–1.9 s instead of 0.37 s. The tool tests start the tool about 200 times, so the "Test the tool" step in CI went from 78 s before #27 to 275–343 s.

The goal is that the tool tests start the tool without a build check per call, while the documented `dotnet run tool/sync.cs` stays covered.

## Acceptance Criteria

- [x] The tool tests build the entry point once per fixture checkout with `dotnet build tool/sync.cs -o <folder>` and start the built entry point; their expectations do not change.
- [x] The shallow tag clone test still runs the tool with `dotnet run tool/sync.cs`, as the README's CI snippet does.
- [ ] On the pull request's CI run, "Test the tool" takes less than 100 s.

## Technical Details

`run_tool_from` in `tests/tool/run.sh` builds a checkout's entry point the first time the checkout is used, into an output folder per checkout under `${WORK}/bin/`, outside the fixture repository; later calls start it directly with `dotnet <folder>/sync.dll`, so it uses the same `dotnet` as the other commands and no native launcher. A failed build stops the test run with the build output.

The built entry point carries the fixture's own `tool/sync.cs` path (`EntryPointFilePath`), so it locates the right checkout as before. Building the referenced project writes to the fixture's `src/RepoBaseline/bin/` and `obj/`, which its `.gitignore` already ignores.

The signing test's `dotnet run` builds a separate test project and is not affected. #29 replaces the entry point with the .NET tool; the tests then start that tool instead.
