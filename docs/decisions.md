# Baseline Decisions

This file records the decisions behind the baseline content and the reasons for them. When a decision changes, update its entry instead of adding a contradicting one, and change the affected profile files in the same commit.

Status values:

- **Confirmed**: decided explicitly by the maintainer.
- **Proposed**: derived from comparing existing repositories, usually by taking the newest state. Treat it as the default until it is confirmed or revised.

## Code Style

### CS-1: Cleanup preserves named arguments

**Status:** Confirmed

The `Zorn` cleanup profile keeps `ArrangeArgumentsStyle` off.

Named arguments document values whose meaning is not obvious at the call site, as with a boolean literal: `Apply(migration, commit: false)`. With the option on, cleanup rewrites such calls to positional form and removes that information. `CODESTYLE.md` asks for named arguments in these cases, so the cleanup profile must not undo them.

### CS-2: Cleanup does not reformat CSS

**Status:** Confirmed

The Rider language settings inside the `Zorn` profile disable `Reformat` and `Rearrange` for CSS.

Stylesheets are often generated or follow the conventions of front-end tooling, and a reformatting pass would produce churn that no C# change caused.

### CS-3: No shared naming abbreviations

**Status:** Confirmed

The shared ReSharper settings define no naming abbreviations. Abbreviations are almost always specific to a project's domain, so each repository adds its own to its local settings layer.

### CS-4: Strict nullability

**Status:** Confirmed

- Nullable reference types are enabled for every project, and the `nullable` warning group is an error in every build configuration (`WarningsAsErrors` contains `nullable`).
- Nullable warnings are never suppressed: no `#nullable` directive and no nullable diagnostic in a `NoWarn` list. A repository that vendors third-party sources may exempt exactly those directories in its local build files.
- ReSharper's nullable-contract inspections (`ConditionalAccessQualifierIsNonNullableAccordingToAPIContract`, `NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract`, `ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract`) are not compiler warnings, so the rule above does not cover them. Remove the reported guard when something enforces the annotation at runtime, and keep it when nothing does, for example for inbound request members, persisted values that can be null, members declared with `= null!`, extension method receivers, or values that a third-party library forwards.
- A kept guard gets `// ReSharper disable once <InspectionId>` directly above it, followed by one comment line that says what can make the value null at runtime. File-level `ReSharper disable` comments are not used for these inspections.

Nullability bugs are cheap to prevent at compile time and expensive to find at runtime. Treating them as errors everywhere, rather than only in Release builds, surfaces them while the change is being written.

### CS-5: Wrap limit of 180 characters

**Status:** Confirmed

The line length limit is 180, set as `max_line_length = 180` for `*.{cs,razor}` in `.editorconfig`. ReSharper reads it as its wrap limit (`WRAP_LIMIT`), so the ReSharper settings do not set it again. The most recently maintained repositories use 180; 160 is an older value.

### CS-6: Expression-bodied members are permitted, not enforced

**Status:** Confirmed

All `csharp_style_expression_bodied_*` options keep their preferred value but use severity `none`, and `CODESTYLE.md` permits both block bodies and expression bodies. Enforcing either form produced findings without improving readability.

### CS-7: Explicit type in object creation when the type is not evident

**Status:** Confirmed

`resharper_object_creation_when_type_not_evident = explicit`: target-typed `new()` is used only where the type is visible from the declaration.

### CS-8: Explicit syntax may stay even when redundant

**Status:** Confirmed

- Parentheses use `always_for_clarity` for all operator groups.
- The cleanup profile does not enable `RemoveRedundantParentheses`.
- Redundant default argument values, anonymous type property names, method type arguments and parentheses are reported at suggestion level only.

Explicit syntax can make code easier to read even when the compiler does not need it.

### CS-9: Usings are sorted alphabetically without groups

**Status:** Confirmed

`dotnet_sort_system_directives_first = false` and `dotnet_separate_import_directive_groups = false`. `CODESTYLE.md` must describe the same rule; an older copy described the opposite.

### CS-10: No primary constructors on classes

**Status:** Confirmed

Classes do not use primary constructors. When a class needs a constructor, it declares an explicit one; classes do not get empty constructors only to satisfy this rule. Records and structs are not affected.

### CS-11: `inspect_code.sh` decides what needs fixing

**Status:** Confirmed

`inspect_code.sh` is the authority for what needs fixing: every finding it reports at warning or error severity is fixed, or suppressed with a reason where CS-4 or a local rule allows it. Suggestions are ignored. The script does not report them by default, and nobody has to act on them.

The severity of an inspection therefore expresses whether it requires action. Inspections that are useful hints but often wrong in framework code, such as unused auto-property accessors or collections that are never updated, are set to suggestion level so they never require a fix. The same applies to the redundant-syntax inspections in CS-8. The shared block applies to `*.{cs,razor}` so Blazor code behaves the same as plain C#.

### CS-12: Two-space indentation for data and documentation files

**Status:** Confirmed

JSON, YAML (`.yml` and `.yaml`), Markdown and the MSBuild and XML files (`.csproj`, `.slnx`, `.props`, `.config`, `.targets`, `.nuspec`, `.xml`) use two-space indentation. All other files, including Razor, CSS and shell scripts, use the four-space default from `[*]`.

The `.yaml` extension is listed explicitly because an older glob covered only `.yml`, so files with the longer extension were indented differently from the others.

## Build

### BLD-1: Warnings are errors in Release builds and CI

**Status:** Confirmed

`TreatWarningsAsErrors` is enabled for Release builds and for every build in CI, whatever the configuration. Warnings therefore cannot be merged, while local Debug builds stay usable during development. Nullable warnings are errors in every configuration (CS-4).

```xml
<TreatWarningsAsErrors Condition="'$(Configuration)' == 'Release' Or '$(CI)' == 'true'">true</TreatWarningsAsErrors>
```

GitHub Actions and GitLab CI both set the environment variable `CI=true`, which MSBuild exposes as the `$(CI)` property.

When a specific warning cannot be fixed, it is muted locally with `NoWarn` in the narrowest scope that works (the project file, or a directory's `Directory.Build.props`), together with a comment explaining why. Nullable warnings are the exception: they are never muted (CS-4).

### BLD-2: Shared MSBuild properties

**Status:** Confirmed

All .NET repositories share `LangVersion=latest`, `ImplicitUsings=enable`, `Nullable=enable`, `NoWarn=1591` and `PreferredUILang=en`. Target frameworks are always repository-specific.

### BLD-3: NuGet sources

**Status:** Confirmed

Public repositories use only nuget.org (`<clear />` followed by the nuget.org source). Repositories that need a private feed keep their own `nuget.config`; private feed addresses never appear in this repository.

### BLD-4: Strong-name keys are never committed

**Status:** Confirmed

Every published library has its own strong-name key; keys are never shared between repositories. The `.snk` file is listed in `.gitignore`. The release pipeline restores it from a CI secret (Base64) and signs assemblies only when packing.

### BLD-5: Location of the local tool manifest follows the SDK

**Status:** Confirmed

- Repositories on .NET SDK 10 or later keep the tool manifest in the repository root (`dotnet-tools.json`).
- Repositories on an older SDK keep it in `.config/dotnet-tools.json`.

When a repository moves to SDK 10 or later, its manifest moves to the root. The baseline tools are `jetbrains.resharper.globaltools` (`jb`) and `dotnet-coverage`.

### BLD-6: Source Link comes from the .NET SDK

**Status:** Confirmed

The .NET SDK includes Source Link for GitHub and GitLab since .NET 8, so the baseline references no Source Link package. A repository on a self-hosted GitLab instance declares its host with a `SourceLinkGitLabHost` item in its local `src/Directory.Build.props`, because host names never appear in synced content.

## Tests

### TST-1: Shared test packages

**Status:** Confirmed

Every test project references NUnit, NUnit3TestAdapter, NUnit.Analyzers, Microsoft.NET.Test.Sdk, FluentAssertions, coverlet.collector and JUnitTestLogger.

`Microsoft.TestPlatform.TestHost` is not part of the shared set. `Microsoft.NET.Test.Sdk` already depends on it, so an explicit reference only adds a version that has to be kept in step. A repository that needs to pin it references it locally.

The shared set contains no mocking framework. Moq-based mocking is being phased out: new tests use hand-written test doubles, and newer repositories do not reference Moq at all. Repositories that still contain older Moq-based tests reference Moq locally in `tests/Directory.Build.props` until those tests are migrated.

FluentAssertions stays on the latest 7.x release, because version 8 changed to a commercial license. 7.x still receives occasional releases, so the shared Renovate preset allows minor and patch updates but blocks major version 8.

### TST-2: Shared testing guidelines are deferred

**Status:** Confirmed

`tests/AGENTS.md` is not part of the first baseline version. The existing files differ too much in structure to merge as part of the initial setup.

### TST-3: Test categories

**Status:** Confirmed

| Test type | Category | Speed |
|---|---|---|
| Unit tests | none | fast, in memory |
| Component tests | `[Category("Component")]` | fast, in memory |
| Integration tests | `[Category("Integration")]` | slow: containers, databases, other I/O |
| End-to-end tests | `[Category("E2E")]` | slow: the whole application |

The categories that matter most are `Integration` and `E2E`, because those tests can take a long time. The filter `TestCategory!=Integration&TestCategory!=E2E` selects the fast tests in every repository, including component tests. Component tests are essentially unit tests, but their category lets them be run or excluded on their own, for example when working only on the UI.

Existing repositories use a mix of categories and namespace conventions, and none of them uses the `Component` category yet.

### TST-4: Test location mirrors the production code

**Status:** Confirmed

Every production project `src/MyProject` has one test project `tests/MyProject.Tests`. Inside it, the folder and namespace structure mirrors the production project:

| Test type | Location | Class name |
|---|---|---|
| Unit tests | `tests/MyProject.Tests/<namespace>/` | `MyClassTests` |
| Component tests (Razor and Blazor components, with bUnit) | `tests/MyProject.Tests/<namespace>/` | `MyClassComponentTests` |
| Integration tests | `tests/MyProject.Tests/Integration/<namespace>/` | `MyClassIntegrationTests` |

For `src/MyProject/Some/Feature/MyClass.cs`, the tests are `tests/MyProject.Tests/Some/Feature/MyClassTests.cs`, `…/MyClassComponentTests.cs` and `tests/MyProject.Tests/Integration/Some/Feature/MyClassIntegrationTests.cs`. Integration tests therefore live in an `.Integration.` namespace *and* carry the `Integration` category (TST-3).

The namespace exists mainly to scope shared setup. Integration tests usually share expensive infrastructure, such as Testcontainers instances, through an NUnit `[SetUpFixture]`. A setup fixture applies to every test in its namespace and the namespaces below it. Placed in `MyProject.Tests.Integration`, it starts the containers once for all integration tests, and never for unit or component tests. The category, by contrast, exists for filtering test runs.

End-to-end tests do not belong to a production project, so they live in their own project (TST-5).

### TST-5: Aspire is the standard for end-to-end tests and local development

**Status:** Confirmed

Applications that consist of several services, or that need infrastructure such as databases, use Aspire:

- `src/Aspire/MyProject.AppHost`: the Aspire app host, which composes the application for local development and for tests.
- `src/Aspire/MyProject.ServiceDefaults`: the service defaults that every service project references.
- `tests/Aspire/MyProject.E2E.Tests`: the end-to-end tests, which start the application through the app host.

Some existing repositories have a `MyProject.E2E.Tests` project without Aspire; they move to this structure when their end-to-end tests are next reworked. New repositories that need end-to-end tests start with it.

## Tooling and Agents

### TOOL-1: Code style scripts are shared

**Status:** Confirmed

`cleanup_code.sh` and `inspect_code.sh` are the same in every .NET repository:

- Both select changed, staged and untracked files, including rename destinations (`--diff-filter=ACMR`). `--all` selects the whole solution instead.
- `inspect_code.sh` selects C# and Razor files. Scoped cleanup leaves Razor files out on purpose: on a Blazor codebase, ReSharper's Razor cleanup turned `String` into `string`, moved `[Inject]` attributes onto the property line and reformatted markup, so it does not apply the shared C# rules to Razor. Inspecting Razor files produced only sensible findings.
- Scoped cleanup selects the MSBuild and packaging files (`.csproj`, `.slnx`, `.props`, `.targets`, `.nuspec`, `.config`) but not plain `.xml` files, which are often data such as test fixtures.
- Both use the single `.slnx` file in the repository root instead of a hard-coded name, and fail when there is none or more than one.
- `inspect_code.sh` reports warnings and errors by default, reads the XML report, and groups findings by project.
- A repository can suppress specific inspection IDs through `inspection.suppressedIds` in `.repo-baseline.json`, for example `CSharpWarnings::CS8618` when a Roslyn diagnostic suppressor already handles it but ReSharper does not run suppressors. Entries are the full `TypeId` as the XML report writes it. `inspect_code.sh` reads the manifest with `jq` and requires it even when nothing is suppressed.

### TOOL-2: `dotnet format` is not used

**Status:** Confirmed

`dotnet format` does not read ReSharper settings and can contradict the cleanup profile. Formatting is the job of `cleanup_code.sh`.

### TOOL-3: Shared ReSharper settings are an injected layer

**Status:** Confirmed

The shared ReSharper settings are a separate synced file that each solution injects as a settings layer. Every repository keeps its `<Name>.slnx.DotSettings` under the default name, so ReSharper and Rider find it without configuration. That file holds only the layer injection and local settings.

How Rider behaves when shared settings are edited in the IDE, in particular which layer it saves to, is verified during the pilot. The README then documents the editing workflow.

### TOOL-4: Ignoring `.idea/` is a local decision

**Status:** Confirmed

The shared `.gitignore` block does not mention `.idea/`. `init` adds `.idea/` to the local part of `.gitignore`, so new repositories ignore the directory completely.

Some repositories commit selected Rider files, such as scopes or project settings. Git cannot re-include a file inside an ignored directory, so a shared `.idea/` pattern would make that impossible. Such a repository replaces the local pattern with more specific ones.

### AGT-1: Agents report notes instead of editing `AGENTS.md`

**Status:** Confirmed

When an agent notices something worth recording, it does not edit `AGENTS.md`. Instead, it states the finding clearly in its final response and asks the maintainer what to do about it, for example whether to record it in an instruction file, open an issue, or ignore it. This replaces the older "write it down in this section" rule.

### AGT-2: Plan file names use neutral wording

**Status:** Confirmed

`ai-plans/AGENTS.md` refers to "the issue number" without naming GitHub or GitLab, so the same file works on both forges.
