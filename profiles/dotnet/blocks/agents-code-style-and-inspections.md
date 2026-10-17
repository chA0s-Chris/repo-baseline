## Code Style and Inspections

Follow `CODESTYLE.md`. The code-style scripts use the local tools from the tool manifest, so run `dotnet tool restore` after cloning or when the manifest changes.

Run `./cleanup_code.sh` when a code change is complete. It applies ReSharper's `Zorn` profile to relevant files that Git reports as changed, staged, or untracked; `--all` applies it to the whole solution instead. Scoped cleanup leaves Razor files out on purpose, because ReSharper's Razor cleanup does not apply the shared C# rules to them. Inspect the resulting diff, then run scoped inspections and the relevant build and test checks. Do not run cleanup during read-only reviews.

`./inspect_code.sh` reports semantic findings at warning or error severity by default and selects only C# and Razor files. Arguments not owned by the script are forwarded to `dotnet jb inspectcode`; for example, `-e=SUGGESTION` also includes suggestions. The script owns `--all`, `--base`, `--format`, and `--output`; it rejects the last two to preserve the XML report used to display findings grouped by project. Findings whose inspection ID is listed in `inspection.suppressedIds` of `.repo-baseline.json` are not displayed. Both scripts print `No matching files to process.` and exit successfully when no relevant files are selected.

Choose the inspection scope deliberately:

- **No arguments** inspects changed, staged, and untracked C# and Razor files. Use this during implementation, after cleanup and before committing.
- **`--base <revision>`** inspects C# and Razor files added, modified, or renamed in `<revision>...HEAD`, together with current working-tree changes. Use this for committed branch changes; `--base=<revision>` is equivalent.
- **`--all`** inspects the whole solution. Reserve this for explicit whole-solution audits, analyzer or configuration changes, and broad refactors because it includes pre-existing findings.

`--base` and `--all` are mutually exclusive. If a scoped run selects no C# or Razor files, inspection is not applicable to that change; do not fall back to `--all`.

Every finding at warning or error severity needs a fix, or a suppression with a reason where `CODESTYLE.md` or a repository rule allows it; suggestions need no action. The script itself only reports findings and exits successfully. A non-zero exit means the inspection itself failed, such as from invalid arguments, an unresolved base revision, or a missing merge base in a shallow clone. A successful exit alone does not mean there are no findings.

Formatting remains `cleanup_code.sh`'s responsibility; inspections are not a formatting check. Do not use `dotnet format`: it does not read the ReSharper settings and can contradict the cleanup profile. `git diff --check` checks whitespace hygiene, not compliance with that profile.
