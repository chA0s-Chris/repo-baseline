#!/bin/bash
# Tests for tool/sync.cs.
#
# The tests build a fixture repo-baseline repository under tmp/ with a copy of tool/sync.cs and fixture
# profiles, tag releases in it, and run the tool from there against throwaway target repositories. The
# fixture profiles are independent of the real ones, so profile changes do not break the tests. The tests
# need bash, Git and the .NET 10 SDK or later.
set -euo pipefail

# Throwaway repositories must not depend on the user's or system's Git configuration,
# such as commit signing or hooks.
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="${ROOT}/tmp/tool-tests"
BASELINE="${WORK}/baseline"

rm -rf "${WORK}"
mkdir -p "${WORK}"

# Writes a file from its arguments, one line each, creating parent directories.
write() {
    local path="$1"
    shift
    mkdir -p "$(dirname "${path}")"
    printf '%s\n' "$@" > "${path}"
}

commit() {
    git -C "$1" add --all
    git -C "$1" commit --quiet -m "$2"
}

# The fixture repo-baseline repository:
#   base   whole files (one executable), an .editorconfig block and two AGENTS.md blocks at the end
#   extra  a second .editorconfig fragment and an AGENTS.md block at the start
#   net    the shared ReSharper settings and a .props file with package references
#   forge  a block in .github/review.md
# v1.0.0 and v1.1.0 differ in docs/GUIDE.md and the rules block; HEAD is an unreleased commit after v1.1.0.
build_baseline() {
    mkdir -p "${BASELINE}/tool"
    git -C "${BASELINE}" init --quiet --initial-branch=main
    git -C "${BASELINE}" config user.email test@example.invalid
    git -C "${BASELINE}" config user.name test
    cp "${ROOT}/tool/sync.cs" "${BASELINE}/tool/sync.cs"

    local profiles="${BASELINE}/profiles"
    write "${profiles}/base/profile.json" '{' '  "blocks": [' \
        '    { "id": "editorconfig", "file": ".editorconfig", "source": "blocks/editorconfig.editorconfig", "position": "start" },' \
        '    { "id": "rules", "file": "AGENTS.md", "source": "blocks/rules.md", "position": "end" },' \
        '    { "id": "notes", "file": "AGENTS.md", "source": "blocks/notes.md", "position": "end" }' \
        '  ]' '}'
    write "${profiles}/base/blocks/editorconfig.editorconfig" 'root = true' '' '[*]' 'indent_style = space'
    write "${profiles}/base/blocks/rules.md" '## Rules' '' 'Rules v1.'
    write "${profiles}/base/blocks/notes.md" '## Notes' '' 'Report notes.'
    write "${profiles}/base/files/tool.sh" '#!/bin/bash' 'echo tool'
    chmod +x "${profiles}/base/files/tool.sh"
    write "${profiles}/base/files/docs/GUIDE.md" 'Guide v1.'
    printf '\x89PNG\r\n\x1a\n\x00\xff\xfe' > "${profiles}/base/files/logo.png"

    write "${profiles}/extra/profile.json" '{' '  "blocks": [' \
        '    { "id": "editorconfig", "file": ".editorconfig", "source": "blocks/editorconfig.editorconfig", "position": "start" },' \
        '    { "id": "extra", "file": "AGENTS.md", "source": "blocks/extra.md", "position": "start" }' \
        '  ]' '}'
    write "${profiles}/extra/blocks/editorconfig.editorconfig" '[*.cs]' 'indent_size = 4'
    write "${profiles}/extra/blocks/extra.md" 'Extra rules.'

    write "${profiles}/net/profile.json" '{ "blocks": [] }'
    write "${profiles}/net/files/.repo-baseline/ReSharper.DotSettings" \
        '<!-- Managed by repo-baseline. -->' \
        '<wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">' \
        '  <s:Boolean x:Key="/Default/Shared/A/@EntryValue">True</s:Boolean>' \
        '  <s:String x:Key="/Default/Shared/B/@EntryValue">b</s:String>' \
        '  <s:String x:Key="/Default/Environment/InjectedLayers/FileInjectedLayer/=ABC/RelativePath/@EntryValue">shared</s:String>' \
        '</wpf:ResourceDictionary>'
    write "${profiles}/net/files/.repo-baseline/Tests.props" \
        '<Project>' '  <ItemGroup>' '    <PackageReference Include="NUnit" />' \
        '    <PackageReference Include="FluentAssertions" />' '  </ItemGroup>' '</Project>'

    write "${profiles}/forge/profile.json" '{' '  "blocks": [' \
        '    { "id": "review", "file": ".github/review.md", "source": "blocks/review.md", "position": "start" }' \
        '  ]' '}'
    write "${profiles}/forge/blocks/review.md" 'Review rules.'

    commit "${BASELINE}" v1.0.0
    git -C "${BASELINE}" tag v1.0.0

    write "${profiles}/base/files/docs/GUIDE.md" 'Guide v2.'
    write "${profiles}/base/blocks/rules.md" '## Rules' '' 'Rules v2.'
    commit "${BASELINE}" v1.1.0
    git -C "${BASELINE}" tag v1.1.0

    write "${profiles}/base/files/docs/GUIDE.md" 'Guide unreleased.'
    commit "${BASELINE}" unreleased

    # Refs whose profiles are invalid, and one with a stray file under profiles/.
    fixture_ref broken broken_profile "${profiles}/broken"
    fixture_ref marker-fragment write "${profiles}/base/blocks/notes.md" '## Notes' '' '<!-- END repo-baseline: notes -->'
    fixture_ref overlap write "${profiles}/extra/files/.github/review.md" 'Review.'
    fixture_ref conflicting-position sed -i 's/"extra", "file": "AGENTS.md", "source": "blocks\/extra.md", "position": "start"/"rules", "file": "AGENTS.md", "source": "blocks\/extra.md", "position": "start"/' \
        "${profiles}/extra/profile.json"
    fixture_ref duplicate-block sed -i 's/"id": "notes"/"id": "rules"/' "${profiles}/base/profile.json"
    fixture_ref array-profile write "${profiles}/base/profile.json" '[]'
    fixture_ref profiles-readme write "${profiles}/README.md" 'Profiles.'
    fixture_ref path-collision write "${profiles}/extra/files/docs" 'A file where base needs a directory.'
    fixture_ref manifest-file write "${profiles}/base/files/.repo-baseline.json" '{}'
    fixture_ref fenced-fragment write "${profiles}/base/blocks/notes.md" '## Notes' '' '```markdown' \
        '<!-- BEGIN repo-baseline: example -->' '<!-- END repo-baseline: example -->' '```'
    fixture_ref backslash-path write "${profiles}/base/files/..\\outside.txt" 'Outside.'
    fixture_ref drive-file write "${profiles}/base/files/C:/outside.txt" 'Outside.'
    fixture_ref drive-block sed -i 's/"file": "AGENTS.md", "source": "blocks\/rules.md"/"file": "C:\/AGENTS.md", "source": "blocks\/rules.md"/' \
        "${profiles}/base/profile.json"
    fixture_ref case-collision write "${profiles}/extra/files/TOOL.sh" 'Echo.'
    fixture_ref case-parent write "${profiles}/extra/files/Docs" 'A file where base needs a directory.'
    fixture_ref case-unselected write "${profiles}/base/files/.github/Review.md" 'Review.'
    fixture_ref latin1-fragment latin1_file "${profiles}/base/blocks/notes.md"
    fixture_ref device-name write "${profiles}/base/files/docs/NUL.md" 'Device.'
    fixture_ref trailing-dot write "${profiles}/base/files/docs." 'Dot.'
    fixture_ref symlink-fragment symlink_file "${profiles}/base/blocks/notes.md" rules.md
    fixture_ref forbidden-character write "${profiles}/base/files/docs/a?.md" 'Question.'
    fixture_ref unclosed-fence write "${profiles}/base/blocks/notes.md" '## Notes' '' '```markdown'
}

# A profile whose block targets a file type without a block marker syntax.
broken_profile() {
    write "$1/profile.json" '{' '  "blocks": [' \
        '    { "id": "notes", "file": "notes.txt", "source": "blocks/notes.txt", "position": "end" }' '  ]' '}'
    write "$1/blocks/notes.txt" 'Notes.'
}

# Replaces a file with a symbolic link to the given target.
symlink_file() {
    rm "$1"
    ln -s "$2" "$1"
}

# Writes a file that is not valid UTF-8.
latin1_file() {
    printf 'Notes \xE4.\n' > "$1"
}

# Creates a ref from main whose only commit runs the given command in the fixture repository.
fixture_ref() {
    local name="$1"
    shift
    git -C "${BASELINE}" switch --quiet -c "${name}" main
    "$@"
    commit "${BASELINE}" "${name}"
    git -C "${BASELINE}" switch --quiet main
}

FAILURES=0
TESTS=0
TARGET=""

# Creates a target repository with a manifest for the given profiles (a JSON array) and version.
new_target() {
    TARGET="${WORK}/target-${TESTS}"
    mkdir -p "${TARGET}"
    git -C "${TARGET}" init --quiet
    write "${TARGET}/.repo-baseline.json" '{' "  \"version\": \"${2:-1.0.0}\"," "  \"profiles\": $1" '}'
}

# Runs the tool from the given repo-baseline checkout, outside the target repository,
# and records its exit code and combined output in STATUS and OUTPUT.
run_tool_from() {
    local checkout="$1"
    shift
    set +e
    OUTPUT=$(cd "${checkout}" && dotnet run tool/sync.cs -- "$@" 2>&1)
    STATUS=$?
    set -e
}

run_tool() {
    run_tool_from "${BASELINE}" "$@"
}

fail() {
    echo "  FAIL: $1"
    echo "${OUTPUT}" | sed 's/^/    | /'
    FAILURES=$((FAILURES + 1))
}

expect_status() {
    [ "${STATUS}" -eq "$1" ] || fail "expected exit code $1, got ${STATUS}"
}

expect_output() {
    grep -qF -- "$1" <<< "${OUTPUT}" || fail "expected output to contain: $1"
}

expect_no_output() {
    if grep -qF -- "$1" <<< "${OUTPUT}"; then fail "expected output not to contain: $1"; fi
}

# Compares a target file with the given lines.
expect_file() {
    local path="${TARGET}/$1"
    shift
    if [ ! -f "${path}" ]; then
        fail "expected file $1"
    elif ! diff <(printf '%s\n' "$@") "${path}" > "${WORK}/diff" 2>&1; then
        OUTPUT=$(cat "${WORK}/diff")
        fail "unexpected content in ${path#"${TARGET}/"}"
    fi
}

expect_no_file() {
    [ ! -e "${TARGET}/$1" ] || fail "expected no file $1"
}

expect_executable() {
    [ -x "${TARGET}/$1" ] || fail "expected $1 to be executable"
}

test_case() {
    TESTS=$((TESTS + 1))
    echo "$1"
}

build_baseline

test_case "sync: writes whole files and blocks into an empty repository"
new_target '["base", "extra"]'
run_tool sync "${TARGET}"
expect_status 0
expect_file tool.sh '#!/bin/bash' 'echo tool'
expect_executable tool.sh
expect_file docs/GUIDE.md 'Guide v1.'
cmp -s "${BASELINE}/profiles/base/files/logo.png" "${TARGET}/logo.png" || fail "expected logo.png to be written byte for byte"
expect_file .editorconfig '# BEGIN repo-baseline: editorconfig' 'root = true' '' '[*]' 'indent_style = space' '' \
    '[*.cs]' 'indent_size = 4' '# END repo-baseline: editorconfig'
expect_file AGENTS.md '<!-- BEGIN repo-baseline: extra -->' 'Extra rules.' '<!-- END repo-baseline: extra -->' '' \
    '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'
run_tool check "${TARGET}"
expect_status 0
expect_output "No findings"

test_case "sync: replaces block content and leaves local content untouched"
new_target '["base"]'
write "${TARGET}/AGENTS.md" '# Project' '' '<!-- BEGIN repo-baseline: rules -->' 'Old rules.' '<!-- END repo-baseline: rules -->' '' \
    'Local section.' '' '<!-- BEGIN repo-baseline: notes -->' '<!-- END repo-baseline: notes -->' '' 'Local end.'
write "${TARGET}/.editorconfig" '# BEGIN repo-baseline: editorconfig' 'root = false' '# END repo-baseline: editorconfig' '' \
    '[*.cs]' 'file_header_template = local'
run_tool sync "${TARGET}"
expect_status 0
expect_file AGENTS.md '# Project' '' '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    'Local section.' '' '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->' '' \
    'Local end.'
expect_file .editorconfig '# BEGIN repo-baseline: editorconfig' 'root = true' '' '[*]' 'indent_style = space' \
    '# END repo-baseline: editorconfig' '' '[*.cs]' 'file_header_template = local'
run_tool sync "${TARGET}"
expect_status 0
expect_output "is up to date with v1.0.0"

test_case "sync: inserts missing blocks at the start and the end of local content"
new_target '["base", "extra"]'
write "${TARGET}/AGENTS.md" '# Project' '' 'Local text.'
run_tool sync "${TARGET}"
expect_status 0
expect_file AGENTS.md '<!-- BEGIN repo-baseline: extra -->' 'Extra rules.' '<!-- END repo-baseline: extra -->' '' \
    '# Project' '' 'Local text.' '' \
    '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'

test_case "sync: removes blocks that no selected profile defines"
new_target '["base"]'
run_tool sync "${TARGET}"
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: extra -->' 'Extra rules.' '<!-- END repo-baseline: extra -->' '' \
    '# Project' '' '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->' '' \
    '<!-- BEGIN repo-baseline: legacy -->' 'Old.' '<!-- END repo-baseline: legacy -->'
write "${TARGET}/.github/review.md" '<!-- BEGIN repo-baseline: review -->' 'Review rules.' '<!-- END repo-baseline: review -->'
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: block extra: not defined by any selected profile"
expect_output "AGENTS.md: block legacy: not defined by any selected profile"
expect_output ".github/review.md: block review: not defined by any selected profile"
run_tool sync "${TARGET}"
expect_status 0
expect_file AGENTS.md '# Project' '' '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'
[ -f "${TARGET}/.github/review.md" ] && [ ! -s "${TARGET}/.github/review.md" ] || fail "expected .github/review.md to be kept empty"
run_tool check "${TARGET}"
expect_status 0

test_case "check: ignores BOM and CRLF line endings, but not a missing final newline; sync writes LF"
new_target '["base"]'
run_tool sync "${TARGET}"
printf '\xEF\xBB\xBF' > "${TARGET}/converted"
sed 's/$/\r/' "${TARGET}/AGENTS.md" >> "${TARGET}/converted"
mv "${TARGET}/converted" "${TARGET}/AGENTS.md"
sed -i 's/$/\r/' "${TARGET}/tool.sh"
printf '\xEF\xBB\xBFGuide v1.\n' > "${TARGET}/docs/GUIDE.md"
run_tool check "${TARGET}"
expect_status 0
printf 'Guide v1.' > "${TARGET}/docs/GUIDE.md"
run_tool check "${TARGET}"
expect_status 1
expect_output "docs/GUIDE.md: differs from v1.0.0"
# A file that is not valid UTF-8 is compared byte for byte, even with the decoder's replacement characters.
printf '\xEF\xBF\xBDPNG\n\x1a\n\x00\xEF\xBF\xBD\xEF\xBF\xBD' > "${TARGET}/logo.png"
run_tool check "${TARGET}"
expect_output "logo.png: differs from v1.0.0"
run_tool sync "${TARGET}"
expect_status 0
expect_no_output "AGENTS.md"
expect_file tool.sh '#!/bin/bash' 'echo tool'
sed -i 's/Report notes\./Edited notes./' "${TARGET}/AGENTS.md"
run_tool sync "${TARGET}"
expect_status 0
expect_output "AGENTS.md: updated"
expect_file AGENTS.md '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'

test_case "check: reports every changed, missing and unknown file, block and executable bit"
new_target '["base", "extra"]'
run_tool sync "${TARGET}"
echo "local edit" >> "${TARGET}/docs/GUIDE.md"
chmod -x "${TARGET}/tool.sh"
rm "${TARGET}/.editorconfig"
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: rules -->' 'Edited.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: other -->' '<!-- END repo-baseline: other -->'
run_tool check "${TARGET}"
expect_status 1
expect_output "docs/GUIDE.md: differs from v1.0.0"
expect_output "tool.sh: executable bit differs from v1.0.0"
expect_output ".editorconfig: block editorconfig: missing"
expect_output "AGENTS.md: block rules: differs from v1.0.0"
expect_output "AGENTS.md: block extra: missing"
expect_output "AGENTS.md: block notes: missing"
expect_output "AGENTS.md: block other: not defined by any selected profile"
expect_output "7 finding(s) against v1.0.0."
rm "${TARGET}/tool.sh"
run_tool check "${TARGET}"
expect_output "tool.sh: missing"
run_tool sync "${TARGET}"
expect_executable tool.sh
run_tool check "${TARGET}"
expect_status 0

test_case "check and sync: report malformed markers; sync then writes nothing"
new_target '["base"]'
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: rules -->' 'Rules.' '' \
    '<!-- BEGIN repo-baseline: notes -->' '<!-- END repo-baseline: notes -->' '<!-- END repo-baseline: other -->'
write "${TARGET}/.editorconfig" '# BEGIN repo-baseline: editorconfig' '# END repo-baseline: editorconfig' \
    '# BEGIN repo-baseline: editorconfig' '# END repo-baseline: editorconfig'
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: block notes: BEGIN marker on line 4 is nested in block rules from line 1"
expect_output "AGENTS.md: block rules: BEGIN marker on line 1 is closed by the END marker of block notes on line 5"
expect_output "AGENTS.md: block other: END marker on line 6 has no BEGIN marker"
expect_output ".editorconfig: block editorconfig: defined twice, on lines 1 and 3"
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: rules -->' 'Rules.'
run_tool check "${TARGET}"
expect_output "AGENTS.md: block rules: BEGIN marker on line 1 has no END marker"
run_tool sync "${TARGET}" 1.1.0
expect_status 2
expect_output "error: AGENTS.md: block rules: BEGIN marker on line 1 has no END marker"
expect_output "error: sync wrote nothing because of the problems above."
expect_no_file tool.sh
expect_no_file docs/GUIDE.md
expect_file AGENTS.md '<!-- BEGIN repo-baseline: rules -->' 'Rules.'
expect_file .repo-baseline.json '{' '  "version": "1.0.0",' '  "profiles": ["base"]' '}'

test_case "check: ReSharper settings need exactly one solution"
new_target '["net"]'
run_tool sync "${TARGET}"
run_tool check "${TARGET}"
expect_status 1
expect_output "*.slnx: no solution in the repository root"
touch "${TARGET}/A.slnx" "${TARGET}/B.slnx"
run_tool check "${TARGET}"
expect_output "*.slnx: several solutions in the repository root (A.slnx, B.slnx)"
rm "${TARGET}/B.slnx"
run_tool check "${TARGET}"
expect_output "A.slnx.DotSettings: missing"

test_case "check: ReSharper settings must inject the shared layer and not redefine its keys"
new_target '["net"]'
run_tool sync "${TARGET}"
touch "${TARGET}/Sample.slnx"
write "${TARGET}/Directory.Packages.props" '<Project>' '  <ItemGroup>' '    <PackageVersion Include="nunit" Version="4.0.0" />' \
    '    <PackageVersion Include="FluentAssertions" Version="7.0.0" />' '  </ItemGroup>' '</Project>'
xmlns='xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation"'
layer='/Default/Environment/InjectedLayers/FileInjectedLayer/=ABC'
write "${TARGET}/Sample.slnx.DotSettings" "<wpf:ResourceDictionary xml:space=\"preserve\" ${xmlns}>" \
    "  <s:String x:Key=\"${layer}/RelativePath/@EntryValue\">..\\other\\ReSharper.DotSettings</s:String>" \
    '  <s:Boolean x:Key="/Default/Shared/A/@EntryValue">False</s:Boolean>' \
    '  <s:Boolean x:Key="/Default/Local/Word/@EntryValue">True</s:Boolean>' \
    '</wpf:ResourceDictionary>'
run_tool check "${TARGET}"
expect_status 1
expect_output 'Sample.slnx.DotSettings: does not inject ..\.repo-baseline\ReSharper.DotSettings'
expect_output "Sample.slnx.DotSettings: key /Default/Shared/A/@EntryValue: redefines a shared key"
expect_no_output "/Default/Local/Word"
expect_no_output "InjectedLayers"
write "${TARGET}/Sample.slnx.DotSettings" "<wpf:ResourceDictionary xml:space=\"preserve\" ${xmlns}>" \
    "  <s:String x:Key=\"${layer}/RelativePath/@EntryValue\">..\\.repo-baseline\\ReSharper.DotSettings</s:String>" \
    "  <s:Boolean x:Key=\"/Default/Environment/InjectedLayers/FileInjectedLayer/=OTHER/@KeyIndexDefined\">True</s:Boolean>" \
    '</wpf:ResourceDictionary>'
run_tool check "${TARGET}"
expect_status 1
expect_output 'Sample.slnx.DotSettings: does not inject ..\.repo-baseline\ReSharper.DotSettings'
write "${TARGET}/Sample.slnx.DotSettings" "<wpf:ResourceDictionary xml:space=\"preserve\" ${xmlns}>" \
    "  <s:Boolean x:Key=\"${layer}/@KeyIndexDefined\">True</s:Boolean>" \
    "  <s:String x:Key=\"${layer}/RelativePath/@EntryValue\">..\\.repo-baseline\\ReSharper.DotSettings</s:String>" \
    '  <s:Boolean x:Key="/Default/Local/Word/@EntryValue">True</s:Boolean>' \
    '</wpf:ResourceDictionary>'
run_tool check "${TARGET}"
expect_status 0

test_case "check: every package of a synced .props file needs a PackageVersion"
new_target '["net"]'
run_tool sync "${TARGET}"
touch "${TARGET}/Sample.slnx"
cp "${WORK}/target-$((TESTS - 1))/Sample.slnx.DotSettings" "${TARGET}/"
run_tool check "${TARGET}"
expect_status 1
expect_output ".repo-baseline/Tests.props: package NUnit: no PackageVersion in Directory.Packages.props"
expect_output ".repo-baseline/Tests.props: package FluentAssertions: no PackageVersion in Directory.Packages.props"
write "${TARGET}/Directory.Packages.props" '<Project>' '  <ItemGroup>' '    <PackageVersion Include="nunit" Version="4.0.0" />' \
    '  </ItemGroup>' '</Project>'
run_tool check "${TARGET}"
expect_status 1
expect_no_output "package NUnit"
expect_output ".repo-baseline/Tests.props: package FluentAssertions: no PackageVersion in Directory.Packages.props"

test_case "sync: sets and clears only the executable bit"
new_target '["base"]'
run_tool sync "${TARGET}"
chmod -x "${TARGET}/tool.sh"
chmod +x "${TARGET}/docs/GUIDE.md"
run_tool check "${TARGET}"
expect_status 1
expect_output "tool.sh: executable bit differs from v1.0.0"
expect_output "docs/GUIDE.md: executable bit differs from v1.0.0"
run_tool sync "${TARGET}"
expect_status 0
expect_output "tool.sh: executable bit set"
expect_output "docs/GUIDE.md: executable bit cleared"
expect_executable tool.sh
[ ! -x "${TARGET}/docs/GUIDE.md" ] || fail "expected docs/GUIDE.md not to be executable"
run_tool check "${TARGET}"
expect_status 0

test_case "sync: keeps the newline of the last line when it removes a trailing block"
new_target '["base"]'
run_tool sync "${TARGET}"
printf '\n<!-- BEGIN repo-baseline: legacy -->\nOld.\n<!-- END repo-baseline: legacy -->' >> "${TARGET}/AGENTS.md"
run_tool sync "${TARGET}"
expect_status 0
expect_file AGENTS.md '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'

test_case "check and sync: a line that looks like a marker but is not one is malformed"
new_target '["base"]'
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: rules-->' 'Rules.' '  <!-- END repo-baseline: rules -->'
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: line 1 looks like a block marker but is not one"
expect_output "AGENTS.md: line 3 looks like a block marker but is not one"
run_tool sync "${TARGET}"
expect_status 2
expect_no_file tool.sh
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline:  rules -->' 'Rules.' '<!-- END repo-baseline: rules  -->'
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: line 1 looks like a block marker but is not one"
expect_output "AGENTS.md: line 3 looks like a block marker but is not one"
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: foo --> trailing -->' 'Local text.' '<!-- END repo-baseline: foo --> trailing -->'
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: line 1 looks like a block marker but is not one"
run_tool sync "${TARGET}"
expect_status 2
expect_file AGENTS.md '<!-- BEGIN repo-baseline: foo --> trailing -->' 'Local text.' '<!-- END repo-baseline: foo --> trailing -->'

test_case "check and sync: a symbolic link is never written through"
new_target '["base"]'
run_tool sync "${TARGET}"
write "${TARGET}/AGENTS.md" '# Local' '' '<!-- BEGIN repo-baseline: rules -->' '<!-- END repo-baseline: rules -->'
rm "${TARGET}/docs/GUIDE.md"
ln -s ../AGENTS.md "${TARGET}/docs/GUIDE.md"
run_tool check "${TARGET}"
expect_status 1
expect_output "docs/GUIDE.md: is a symbolic link; synced files must be regular files inside the repository"
run_tool sync "${TARGET}"
expect_status 2
expect_output "error: docs/GUIDE.md: is a symbolic link"
expect_output "error: sync wrote nothing because of the problems above."
expect_file AGENTS.md '# Local' '' '<!-- BEGIN repo-baseline: rules -->' '<!-- END repo-baseline: rules -->'

test_case "check and sync: a link in the path of a used target is refused, of an unused one ignored"
new_target '["base", "forge"]'
mkdir -p "${WORK}/outside-${TESTS}"
write "${WORK}/outside-${TESTS}/review.md" 'Outside.'
ln -s "${WORK}/outside-${TESTS}" "${TARGET}/.github"
run_tool check "${TARGET}"
expect_status 1
expect_output ".github/review.md: .github is a symbolic link"
run_tool sync "${TARGET}"
expect_status 2
expect_no_file tool.sh
write "${TARGET}/.repo-baseline.json" '{ "version": "1.0.0", "profiles": ["base"] }'
run_tool sync "${TARGET}"
expect_status 0
run_tool check "${TARGET}"
expect_status 0
[ "$(cat "${WORK}/outside-${TESTS}/review.md")" = "Outside." ] || fail "expected the linked file to be unchanged"

test_case "check and sync: a managed file must be valid UTF-8"
new_target '["base"]'
printf 'Local \xE4.\n' > "${TARGET}/AGENTS.md"
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: not valid UTF-8"
run_tool sync "${TARGET}"
expect_status 2
expect_output "error: AGENTS.md: not valid UTF-8"
[ "$(od -An -tx1 "${TARGET}/AGENTS.md" | tr -d ' \n')" = "4c6f63616c20e42e0a" ] || fail "expected AGENTS.md to be unchanged"
expect_no_file tool.sh

test_case "check and sync: an unused block target that is not valid UTF-8 is skipped"
new_target '["base"]'
mkdir -p "${TARGET}/.github"
printf 'Review \xE4.\n' > "${TARGET}/.github/review.md"
run_tool sync "${TARGET}"
expect_status 0
run_tool check "${TARGET}"
expect_status 0
[ "$(od -An -tx1 "${TARGET}/.github/review.md" | tr -d ' \n')" = "52657669657720e42e0a" ] || fail "expected .github/review.md to be unchanged"

test_case "sync: a directory or file in the way is a problem, and nothing is written"
new_target '["base", "forge"]'
mkdir -p "${TARGET}/docs/GUIDE.md"
touch "${TARGET}/.github"
run_tool sync "${TARGET}"
expect_status 2
expect_output "error: docs/GUIDE.md: is a directory"
expect_output "error: .github/review.md: .github is not a directory"
expect_no_file tool.sh
rmdir "${TARGET}/docs/GUIDE.md"
rm "${TARGET}/.github"
mkdir -p "${TARGET}/.github/review.md"
run_tool check "${TARGET}"
expect_output ".github/review.md: is a directory"

test_case "sync: creates a managed file in a missing directory"
new_target '["base", "forge"]'
run_tool sync "${TARGET}"
expect_status 0
expect_file .github/review.md '<!-- BEGIN repo-baseline: review -->' 'Review rules.' '<!-- END repo-baseline: review -->'

test_case "sync: keeps paragraphs apart when it removes a block"
new_target '["base"]'
write "${TARGET}/AGENTS.md" 'Paragraph A.' '<!-- BEGIN repo-baseline: legacy -->' 'Old.' '<!-- END repo-baseline: legacy -->' '' 'Paragraph B.'
run_tool sync "${TARGET}"
expect_status 0
expect_file AGENTS.md 'Paragraph A.' '' 'Paragraph B.' '' \
    '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'

test_case "sync: treats a file of empty lines as empty"
new_target '["base"]'
printf '\n' > "${TARGET}/AGENTS.md"
run_tool sync "${TARGET}"
expect_status 0
expect_file AGENTS.md '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'

test_case "sync: a cleared whitespace-only file still ends with a newline"
new_target '["forge"]'
mkdir -p "${TARGET}/.github"
printf '  ' > "${TARGET}/.github/review.md"
run_tool sync "${TARGET}"
expect_status 0
expect_file .github/review.md '<!-- BEGIN repo-baseline: review -->' 'Review rules.' '<!-- END repo-baseline: review -->'

test_case "check and sync: near misses match whole words in any case, in both comment syntaxes"
new_target '["base"]'
run_tool sync "${TARGET}"
write "${TARGET}/.editorconfig" '#BEGIN repo-baseline: editorconfig' '# END repo-baseline: editorconfig' '# APPEND repo-baseline: not a marker'
write "${TARGET}/AGENTS.md" '<!-- begin repo-baseline: rules -->' '<!-- END  repo-baseline: rules -->'
run_tool check "${TARGET}"
expect_status 1
expect_output ".editorconfig: line 1 looks like a block marker but is not one; markers look like # BEGIN repo-baseline: <id>"
expect_no_output ".editorconfig: line 3"
expect_output "AGENTS.md: line 1 looks like a block marker but is not one"
expect_output "AGENTS.md: line 2 looks like a block marker but is not one"
run_tool sync "${TARGET}"
expect_status 2

test_case "check and sync: markers in Markdown code fences are content"
new_target '["base"]'
run_tool sync "${TARGET}"
write "${TARGET}/AGENTS.md" '# Project' '' 'Blocks look like this:' '' '```markdown' \
    '<!-- BEGIN repo-baseline: example -->' '<!-- END repo-baseline: example -->' '```' '' \
    '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'
cp "${TARGET}/AGENTS.md" "${WORK}/expected-${TESTS}"
run_tool check "${TARGET}"
expect_status 0
run_tool sync "${TARGET}"
expect_status 0
cmp -s "${WORK}/expected-${TESTS}" "${TARGET}/AGENTS.md" || fail "expected AGENTS.md to be unchanged"
write "${TARGET}/AGENTS.md" '~~~' '<!-- BEGIN repo-baseline: rules -->' '<!-- END repo-baseline: rules -->'
run_tool check "${TARGET}"
expect_status 1
expect_output "AGENTS.md: line 1 opens a code fence that is never closed, so block markers after it would be ignored"
run_tool sync "${TARGET}"
expect_status 2
write "${TARGET}/AGENTS.md" '# Project' '' '```'
run_tool sync "${TARGET}"
expect_status 2
expect_output "error: AGENTS.md: line 3 opens a code fence that is never closed"
write "${TARGET}/AGENTS.md" '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v1.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->' '' '```' 'Open until the end.'
run_tool check "${TARGET}"
expect_status 0
run_tool sync "${TARGET}"
expect_status 0
write "${TARGET}/.github/review.md" '# Review' '' '```'
run_tool sync "${TARGET}"
expect_status 0
run_tool check "${TARGET}"
expect_status 0
write "${TARGET}/AGENTS.md" '~~~' '<!-- BEGIN repo-baseline: rules -->' '<!-- END repo-baseline: rules -->'
run_tool sync "${TARGET}" --ref fenced-fragment
expect_status 2
expect_output "AGENTS.md: line 1 opens a code fence that is never closed"
write "${TARGET}/AGENTS.md" '# Project'
run_tool sync "${TARGET}" --ref fenced-fragment
expect_status 0
run_tool check "${TARGET}" --ref fenced-fragment
expect_status 0

test_case "sync: refuses a symbolic link as manifest when it writes a version, and removes a manifest BOM"
new_target '["base"]'
mv "${TARGET}/.repo-baseline.json" "${WORK}/manifest-${TESTS}.json"
ln -s "${WORK}/manifest-${TESTS}.json" "${TARGET}/.repo-baseline.json"
run_tool sync "${TARGET}" 1.1.0
expect_status 2
expect_output "error: .repo-baseline.json: is a symbolic link"
expect_no_file tool.sh
rm "${TARGET}/.repo-baseline.json"
printf '\xEF\xBB\xBF{ "version": "1.0.0", "profiles": ["base"] }\n' > "${TARGET}/.repo-baseline.json"
run_tool sync "${TARGET}" 1.1.0
expect_status 0
expect_file .repo-baseline.json '{ "version": "1.1.0", "profiles": ["base"] }'

test_case "sync: reports a failed write and how many files it wrote before"
if [ "$(id -u)" -eq 0 ]; then
    echo "  skipped: root can write to read-only directories"
else
    new_target '["base"]'
    chmod a-w "${TARGET}"
    run_tool sync "${TARGET}"
    chmod u+w "${TARGET}"
    expect_status 2
    expect_output "error: Cannot write docs/GUIDE.md: "
    expect_output "sync stopped after writing 0 other file(s)."
fi

test_case "errors: invalid profiles are source errors"
new_target '["base", "extra"]'
run_tool check "${TARGET}" --ref marker-fragment
expect_status 2
expect_output "marker-fragment: line 3 of the base fragment of block notes for AGENTS.md looks like a block marker."
run_tool check "${TARGET}" --ref overlap
expect_status 2
expect_output "overlap: .github/review.md is both a synced file and a block target."
run_tool check "${TARGET}" --ref conflicting-position
expect_status 2
expect_output "conflicting-position: block rules for AGENTS.md has position end in profile base but start in profile extra."
run_tool check "${TARGET}" --ref duplicate-block
expect_status 2
expect_output "block rules for AGENTS.md is defined more than once"
run_tool check "${TARGET}" --ref array-profile
expect_status 2
expect_output "profiles/base/profile.json is invalid: 'blocks' must be an array."
run_tool check "${TARGET}" --ref path-collision
expect_status 2
expect_output "path-collision: docs is a file, but docs/GUIDE.md needs it to be a directory."
run_tool check "${TARGET}" --ref manifest-file
expect_status 2
expect_output "manifest-file: a profile provides .repo-baseline.json, which belongs to the target repository."
run_tool check "${TARGET}" --ref backslash-path
expect_status 2
expect_output 'backslash-path: profiles/base/files/..\outside.txt is not a valid target path'
run_tool check "${TARGET}" --ref drive-file
expect_status 2
expect_output "drive-file: profiles/base/files/C:/outside.txt is not a valid target path"
run_tool check "${TARGET}" --ref drive-block
expect_status 2
expect_output "drive-block: profiles/base/profile.json is invalid: block rules needs relative 'file' and 'source' paths inside the repository."
run_tool check "${TARGET}" --ref case-collision
expect_status 2
expect_output "case-collision: tool.sh and TOOL.sh differ only in case."
run_tool check "${TARGET}" --ref case-parent
expect_status 2
expect_output "case-parent: Docs is a file, but docs/GUIDE.md needs it to be a directory."
run_tool check "${TARGET}" --ref case-unselected
expect_status 2
expect_output "case-unselected: .github/Review.md and .github/review.md differ only in case."
run_tool check "${TARGET}" --ref latin1-fragment
expect_status 2
expect_output "latin1-fragment: fragment profiles/base/blocks/notes.md of block notes is not valid UTF-8."
run_tool check "${TARGET}" --ref device-name
expect_status 2
expect_output "device-name: profiles/base/files/docs/NUL.md is not a valid target path"
run_tool check "${TARGET}" --ref trailing-dot
expect_status 2
expect_output "trailing-dot: profiles/base/files/docs. is not a valid target path"
run_tool check "${TARGET}" --ref symlink-fragment
expect_status 2
expect_output "symlink-fragment: profiles/base/blocks/notes.md has mode 120000; only regular files are supported."
run_tool check "${TARGET}" --ref forbidden-character
expect_status 2
expect_output "forbidden-character: profiles/base/files/docs/a?.md is not a valid target path"
run_tool check "${TARGET}" --ref unclosed-fence
expect_status 2
expect_output "unclosed-fence: line 3 of the base fragment of block notes for AGENTS.md opens a code fence that is never closed."

test_case "sync: a file directly under profiles/ is not a profile"
new_target '["base"]'
run_tool sync "${TARGET}" --ref profiles-readme
expect_status 0
expect_file docs/GUIDE.md 'Guide unreleased.'

test_case "sync: ignores Git variables that point to another repository"
new_target '["base"]'
export GIT_DIR="${WORK}/no-such-repository" GIT_WORK_TREE="${WORK}"
run_tool sync "${TARGET}"
unset GIT_DIR GIT_WORK_TREE
expect_status 0
expect_file docs/GUIDE.md 'Guide v1.'

test_case "sync: writes a given version to the manifest and changes nothing else in it"
new_target '["base"]'
write "${TARGET}/.repo-baseline.json" '{' '  "version": "1.0.0",' '  "profiles": ["base"],' \
    '  "inspection": { "suppressedIds": [], "version": "keep" }' '}'
run_tool sync "${TARGET}" v1.1.0
expect_status 0
expect_file .repo-baseline.json '{' '  "version": "1.1.0",' '  "profiles": ["base"],' \
    '  "inspection": { "suppressedIds": [], "version": "keep" }' '}'
expect_file docs/GUIDE.md 'Guide v2.'
expect_file AGENTS.md '<!-- BEGIN repo-baseline: rules -->' '## Rules' '' 'Rules v2.' '<!-- END repo-baseline: rules -->' '' \
    '<!-- BEGIN repo-baseline: notes -->' '## Notes' '' 'Report notes.' '<!-- END repo-baseline: notes -->'
run_tool check "${TARGET}"
expect_status 0
expect_output "matches v1.1.0"

test_case "sync and check: --ref reads another ref and never changes the manifest"
new_target '["base"]'
run_tool sync "${TARGET}" --ref main
expect_status 0
expect_file docs/GUIDE.md 'Guide unreleased.'
expect_file .repo-baseline.json '{' '  "version": "1.0.0",' '  "profiles": ["base"]' '}'
run_tool check "${TARGET}" --ref=main
expect_status 0
expect_output "matches main"
run_tool check "${TARGET}"
expect_status 1
expect_output "docs/GUIDE.md: differs from v1.0.0"
run_tool sync "${TARGET}" 1.1.0 --ref main
expect_status 2
expect_output "<version> and --ref cannot be combined."

test_case "errors: usage, a missing tag or ref, an invalid manifest and an unknown profile"
new_target '["base"]' 9.9.9
run_tool check
expect_status 2
expect_output "check needs the path of the target repository."
run_tool verify "${TARGET}"
expect_status 2
expect_output "Unknown command 'verify'."
run_tool check ""
expect_status 2
expect_output "check needs the path of the target repository."
run_tool check "${TARGET}" --force
expect_status 2
expect_output "Unknown option '--force'."
run_tool check "${TARGET}" --ref main --ref v1.0.0
expect_status 2
expect_output "--ref is given more than once."
run_tool sync "${TARGET}" '1.0/0'
expect_status 2
expect_output "'1.0/0' is not a valid version."
run_tool sync "${TARGET}" vv1
expect_status 2
expect_output "'vv1' is not a valid version."
run_tool check "${TARGET}"
expect_status 2
expect_output "tag v9.9.9 not found"
run_tool check "${TARGET}" --ref does-not-exist
expect_status 2
expect_output "ref does-not-exist not found"
write "${TARGET}/.repo-baseline.json" '{ "version": "1.0.0", "profiles": ["base", "nope"] }'
run_tool sync "${TARGET}"
expect_status 2
expect_output "Unknown profile(s) nope; v1.0.0 has base, extra, forge, net."
expect_no_file tool.sh
write "${TARGET}/.repo-baseline.json" '{ "version": "1.0.0", "profiles": "base" }'
run_tool check "${TARGET}"
expect_status 2
expect_output "is invalid: 'profiles' must be a non-empty array."
write "${TARGET}/.repo-baseline.json" '{ "version": "v1.0.0", "profiles": ["base"] }'
run_tool check "${TARGET}"
expect_status 2
expect_output "is invalid: 'version' must not start with 'v'; write \"1.0.0\"."
write "${TARGET}/.repo-baseline.json" '{ "version": "v", "profiles": ["base"] }'
run_tool check "${TARGET}"
expect_status 2
expect_output "is invalid: 'v' is not a valid version."
write "${TARGET}/.repo-baseline.json" '{ "version": "1.1.0~1", "profiles": ["base"] }'
run_tool check "${TARGET}"
expect_status 2
expect_output "is invalid: '1.1.0~1' is not a valid version."
write "${TARGET}/.repo-baseline.json" '{ "version": "1.0.0", "profiles": ["base"], "version": "1.1.0" }'
run_tool check "${TARGET}"
expect_status 2
expect_output "is invalid: key 'version' appears more than once."
write "${TARGET}/.repo-baseline.json" 'not json'
run_tool check "${TARGET}"
expect_status 2
expect_output "is invalid"
rm "${TARGET}/.repo-baseline.json"
run_tool check "${TARGET}"
expect_status 2
expect_output ".repo-baseline.json not found"

test_case "errors: a block target without a marker syntax is a source error"
new_target '["base"]'
run_tool check "${TARGET}" --ref broken
expect_status 2
expect_output "broken: no block marker syntax is defined for notes.txt."

# A smoke test: run from a clone outside the target, the target's build files cannot reach the tool.
test_case "shallow tag clone: syncs and checks a target that has its own Directory.Build.props"
git clone --quiet --depth 1 --branch v1.1.0 "file://${BASELINE}" "${WORK}/shallow" 2> /dev/null
new_target '["base", "extra"]' 1.1.0
write "${TARGET}/Directory.Build.props" 'This would break any build that imports it.'
write "${TARGET}/global.json" '{ "sdk": { "version": "1.0.0" } }'
run_tool_from "${WORK}/shallow" sync "${TARGET}"
expect_status 0
expect_file docs/GUIDE.md 'Guide v2.'
run_tool_from "${WORK}/shallow" check "${TARGET}"
expect_status 0

echo
if [ "${FAILURES}" -eq 0 ]; then
    echo "All ${TESTS} tests passed."
else
    echo "${FAILURES} check(s) failed in ${TESTS} tests."
    exit 1
fi
