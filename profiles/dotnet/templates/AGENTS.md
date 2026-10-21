# Root AGENTS.md

`{{Name}}`: describe in one or two sentences what this repository contains.

## Project Structure

| Path | Contents |
| --- | --- |
| `src/` | Production projects; see `src/AGENTS.md` |
| `tests/` | Test projects; see `tests/AGENTS.md` |

## Local Development Commands

| Purpose | Command |
| --- | --- |
| Restore local tools | `dotnet tool restore` |
| Apply the code style | `./cleanup_code.sh` |
| Inspect changed C# files | `./inspect_code.sh` |
| Inspect C# changes relative to a base | `./inspect_code.sh --base <revision>` |
| Complete test suite | `dotnet test {{Name}}.slnx` |
| Tests excluding integration and end-to-end tests | `dotnet test {{Name}}.slnx --filter "TestCategory!=Integration&TestCategory!=E2E"` |
| Release build | `dotnet build -c Release {{Name}}.slnx` |

<!-- BEGIN repo-baseline: implementation-rules -->
<!-- END repo-baseline: implementation-rules -->

<!-- BEGIN repo-baseline: code-style-and-inspections -->
<!-- END repo-baseline: code-style-and-inspections -->

<!-- BEGIN repo-baseline: notes-from-agents -->
<!-- END repo-baseline: notes-from-agents -->
