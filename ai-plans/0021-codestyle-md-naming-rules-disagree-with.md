# `CODESTYLE.md` and the shared ReSharper settings agree on naming

> Issue: [#21](https://github.com/chA0s-Chris/repo-baseline/issues/21)

## Rationale

The synced `CODESTYLE.md` describes naming conventions that the shared ReSharper settings do not enforce. It says "Use `_camelCase` for private fields", but the user rule "Static fields (private)" requires `PascalCase` without a prefix for private static fields that are not readonly, so `inspect_code.sh` reports such a field named `_camelCase` as `InconsistentNaming`. It also says "Use `camelCase` for local variables, constants, and parameters" and, two lines further, "Use `PascalCase` for constant members", which contradict each other for constant fields. In the ShadowDrop pilot (#4), a field named after `CODESTYLE.md` was reported by the inspection.

In addition, `.repo-baseline/ReSharper.DotSettings` still contains four `PredefinedNamingRules` entries with an `m_` prefix for the same private field kinds. The four user rules replaced them when ReSharper's naming-rules upgrade ran, but a reader of the file sees two conflicting sets of rules.

The goal is that `CODESTYLE.md` describes what the shared settings enforce, and that the shared settings contain only the rules that apply.

## Acceptance Criteria

- [ ] The naming lines of `CODESTYLE.md` match the shared naming rules: `_camelCase` for private instance fields, readonly or not; `PascalCase` without a prefix for private static fields, static readonly fields and constant fields; `camelCase` for local variables, local constants and parameters.
- [ ] No two naming lines of `CODESTYLE.md` contradict each other.
- [ ] `.repo-baseline/ReSharper.DotSettings` contains no `PredefinedNamingRules` entries.
- [ ] `inspect_code.sh` with the shared settings reports no `InconsistentNaming` for a sample class that follows every naming line of `CODESTYLE.md`, and reports one for a private static field that is not readonly and is named `_camelCase`. The results are the same before and after the `PredefinedNamingRules` entries are removed.

## Technical Details

The effective naming rules stay as they are: `CODESTYLE.md` changes to describe them, and `ReSharper.DotSettings` loses only entries that no longer apply. Following design rule 4 ("One source per rule"), the ReSharper settings enforce the rules and `CODESTYLE.md` describes them. The effective rules are the four user rules in `.repo-baseline/ReSharper.DotSettings` and ReSharper's defaults for every other kind, such as `camelCase` for locals, local constants and parameters, and `PascalCase` for non-private constants and static readonly fields.

The `IsMigratorApplied` key of `PredefinedNamingRulesToUserRulesUpgrade` stays, so that ReSharper does not run the upgrade again.

The script tests run against a stub `dotnet` and cannot cover ReSharper, so the last criterion is verified by hand with the ReSharper CLI in a scratch repository that `init` creates from the real profiles with `--ref HEAD`, plus a project that contains the sample class and is added to the solution, because inspection covers only files in the solution.

Both files are synced content, so adopting repositories receive the change with `sync` once they pin the next release.
