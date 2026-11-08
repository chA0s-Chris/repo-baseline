# XML formatting of MSBuild files follows the shared style

> Issue: [#18](https://github.com/chA0s-Chris/repo-baseline/issues/18)

## Rationale

The shared `.editorconfig` section for `*.{csproj,slnx,props,config,targets,nuspec,xml}` sets no line length, and nothing in the shared settings says whether a blank line follows the XML declaration. `cleanup_code.sh --all` therefore formats these files with ReSharper's defaults: it wraps attributes at 120 characters while C# wraps at 180, and it inserts a blank line after `<?xml …?>`. In the Chaos.Mongo pilot (chA0s-Chris/Chaos.Mongo#274), the first full cleanup changed five MSBuild and config files for these reasons alone, and the profile's own `nuget.config` and `coverlet.xml` templates would change the same way.

The goal is that the shared settings decide both, so that cleanup leaves XML files alone when they already follow them.

## Acceptance Criteria

- [x] The XML section of the shared `.editorconfig` block sets `max_line_length = 180` and `xml_blank_line_after_pi = false`.
- [x] CS-5 in `docs/decisions.md` covers the line length of the XML file types as well as C#, and a new decision CS-14 records that cleanup inserts no blank line after the XML declaration.
- [x] Cleanup with the shared `Zorn` profile leaves an XML file unchanged when it already follows these settings, verified on the five files the pilot's cleanup changed and on the profile's `nuget.config` and `coverlet.xml` templates.

## Technical Details

Both keys go in the `.editorconfig` block, not in `ReSharper.DotSettings`, because the design keeps only what `.editorconfig` cannot express in the ReSharper settings. ReSharper reads `max_line_length` in the XML section as its XML wrap limit, as it does for C# (CS-5).

A test with the ReSharper CLI 2026.2.4 on Chaos.Mongo at `60a57c33`, the commit before its cleanup commit, showed:

- With both keys, cleanup of the five files changed nothing. Without them, it changed all five.
- Each key is needed: `max_line_length` alone still added the blank line, and `xml_blank_line_after_pi` alone still wrapped two lines.
- With both keys, cleanup keeps an existing blank line after the declaration and keeps attributes that are already wrapped. Repositories that already have either see no churn.

The script tests run against a stub `dotnet` and cannot cover ReSharper, so the third criterion is verified by hand with the ReSharper CLI, the same way. Cleanup only processes files that belong to the solution, and the `.slnx` that `init` creates is empty, so the `nuget.config` and `coverlet.xml` templates are copied over the files of the same name in the Chaos.Mongo clone, whose solution lists both. Under the old settings, cleanup added the blank line to both templates. Rider's own formatting of XML files is not verified; `cleanup_code.sh` is the authority (TOOL-1).

The `.editorconfig` block is synced content, so adopting repositories receive the change with `sync` once they pin the next release (`v0.2.0`).
