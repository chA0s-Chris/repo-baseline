# Shared ReSharper file layout never matches NUnit test methods

> Issue: [#8](https://github.com/chA0s-Chris/repo-baseline/issues/8)

## Rationale

The "NUnit Test Fixtures" type pattern in `.repo-baseline/ReSharper.DotSettings` and its "Test Methods" entry combine the `Test`, `TestCase` and `TestCaseSource` attributes with `And`, so they never match a real test method. Only classes with `[TestFixture]` or `[TestFixtureSource]` use the pattern, and even there test methods are never grouped. All other test classes fall back to the Default Pattern.

The maintainer decided to fix the matching and give test classes a deliberate member order. Setup comes first, then the tests, then helpers and nested types, so a reader sees the fixture's state and setup before the tests.

## Acceptance Criteria

- [ ] In both places in `.repo-baseline/ReSharper.DotSettings`, a method with any one of the `Test`, `TestCase` or `TestCaseSource` attributes is recognized as a test method.
- [ ] The NUnit pattern orders members as: static fields and constants, fields (both sorted as today), constructors, setup/teardown methods, test methods in written order, all other members in written order, nested types.
- [ ] Classes without NUnit test methods or fixture attributes still use the unchanged Default Pattern.
- [ ] `docs/decisions.md` records the test class member order as a new Confirmed decision CS-13, including that adopting repositories apply the reordering in their reformatting commit listed in `.git-blame-ignore-revs`.
- [ ] With the baseline applied at Chaos.Mongo `1db6f36` and ShadowDrop `73c9184`, `cleanup_code.sh --all` changes the same files as the measured extended variant (50 and 13 files), and every change is a pure move into the specified order.
- [ ] A comment on #8 summarizes the measurement of all three variants and the decision; it is shown to the maintainer before it is posted.

## Technical Details

A class uses the NUnit pattern when it declares a test method (`HasMember`, now with the corrected `Or`). NUnit does not require `[TestFixture]`, so this is how ordinary test classes are recognized. As a result, nearly every test class moves from the Default Pattern to the NUnit pattern; that is the intended effect.

The existing checks for an inherited `[TestFixture]` or `[TestFixtureSource]` stay. They cover parameterized fixtures, for which the attribute is still required, and classes whose test methods are all inherited from a base fixture. A plain `[TestFixture]` is redundant, and the pattern does not depend on it.

Entries in the NUnit pattern, in order (illustrative; existing entries keep their match and sort rules):

| Entry | Match | Sort |
| --- | --- | --- |
| Static Fields and Constants | unchanged | unchanged |
| Fields | unchanged | unchanged |
| Constructors (new) | `Kind Is="Constructor"` | static, then access, as in the Default Pattern |
| Setup/Teardown Methods | unchanged | none |
| Test Methods | `Kind Is="Method"` and any of the three attributes, `Priority="100"` | none |
| All other members | catch-all | none |
| Nested Types (new) | `Kind Is="Type"` | none |

Test methods and other members have no sort, so the written order is kept and the one-time reordering stays small.

Measured with the baseline applied (`init` and `sync --ref main`) on Chaos.Mongo `1db6f36` and ShadowDrop `73c9184`:

| Variant | Chaos.Mongo | ShadowDrop |
| --- | --- | --- |
| Fix the `Or` only | 113 files, ~9,800 lines moved | 80 files, ~13,100 lines moved |
| Drop the NUnit pattern | 7 files, 475 lines | 13 files, 2,405 lines |
| Fix and extend (chosen) | 50 files, 845 lines | 13 files, 387 lines |

In the chosen variant, the moves are mostly `[SetUp]` and `[OneTimeSetUp]` methods moving above the tests, plus a few helpers and nested types moving below them. To reproduce: apply the baseline with `init` and `sync`, commit, then run `cleanup_code.sh --all` once per variant of the pattern and compare the diffs.

`[Theory]` and other NUnit test attributes stay out of scope; the issue names only the three attributes.

The pattern is XML escaped inside a string value (`&lt;`, `&gt;`, `&#xD;` line endings). Edits keep that encoding, UTF-8 without BOM and LF line endings. No automated test covers DotSettings content; the cleanup measurement is the verification. ReSharper may ignore a pattern it cannot parse, so the cleanup run also confirms that the escaped pattern is still valid.
