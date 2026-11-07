# `inspect_code.sh` lists each finding once per target framework

> Issue: [#6](https://github.com/chA0s-Chris/repo-baseline/issues/6)

## Rationale

For multi-targeted projects, the XML report of `inspectcode` contains one `Issue` element per target framework, identical apart from its `TargetFramework` attribute. `inspect_code.sh` prints every element, so the same finding appears once per framework. In a repository with three target frameworks, a warning-level run printed 51 findings, of which 17 were distinct. The repetition makes real findings harder to spot.

The goal is to print each finding once and to name the target frameworks only when a finding does not occur in all of them.

## Acceptance Criteria

- [x] A finding that the report lists for several target frameworks of a project appears once in the output of `inspect_code.sh`.
- [x] A finding listed for only some of its project's target frameworks ends with those frameworks, for example `(net8.0)` or `(net8.0, net9.0)`.
- [x] Findings without a `TargetFramework` attribute, suppression through `inspection.suppressedIds` and grouping by project behave as before, and the existing script tests pass unchanged.
- [x] The script tests in `tests/scripts/` cover both cases with a new fixture report whose findings carry `TargetFramework` attributes.
- [x] TOOL-1 in `docs/decisions.md` states that each finding is printed once and when target frameworks are named.

## Technical Details

A finding is identified by its `TypeId`, `File`, `Offset` and `Message`; the line follows from the offset. Each finding prints at the position of its first occurrence, so the order matches today's output.

A project's target frameworks are the `TargetFramework` values of that project's findings in the report. The report does not list the frameworks a project targets, and reading them from the project files would need MSBuild evaluation. As a consequence, a framework without any finding in the project is unknown, and a finding that occurs in all other frameworks prints without a suffix. The frameworks are collected before suppression, so suppressing an inspection does not change how other findings are labeled. A test covers this: a suppressed finding's framework still counts for its project, so another finding in that project keeps its suffix.

Frameworks in a suffix are sorted by version, oldest first, for example `(net8.0, net9.0)`. The version is compared numerically, so `net9.0` comes before `net10.0`. Names that do not have the `netX.Y` form, such as `netstandard2.0` or `net8.0-windows`, come after those and are sorted by name. In a measured report, `inspectcode` listed `net10.0` before `net9.0`, although the project declares `net9.0;net10.0`.

The parser stays in `awk`, and a project's entries are still printed when its `</Project>` element is reached.

`inspect_code.sh` is synced content, so adopting repositories receive the change with `sync`; the manifest does not change.
