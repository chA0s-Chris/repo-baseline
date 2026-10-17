#!/bin/bash
# Tests for the shared code-style scripts of the dotnet profile.
#
# Each test copies the scripts into a throwaway Git repository under tmp/ and puts a stub `dotnet`
# first on PATH. The stub records its arguments and writes a fixed inspectcode report, so the tests
# need only bash, Git and jq, and no ReSharper. Real ReSharper behavior is not covered here.
set -euo pipefail

# Throwaway repositories must not depend on the user's or system's Git configuration,
# such as commit signing or hooks.
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPTS="${ROOT}/profiles/dotnet/files"
FIXTURES="${ROOT}/tests/scripts/fixtures"
WORK="${ROOT}/tmp/script-tests"

rm -rf "${WORK}"
mkdir -p "${WORK}/bin"

# The stub stands in for `dotnet jb cleanupcode` and `dotnet jb inspectcode`.
cat > "${WORK}/bin/dotnet" <<'STUB'
#!/bin/bash
printf '%s\n' "$*" >> "${STUB_LOG}"
if [ "${2-}" = "inspectcode" ]; then
    for argument in "$@"; do
        case "${argument}" in
            --output=*) cp "${STUB_REPORT}" "${argument#--output=}" ;;
        esac
    done
fi
STUB
chmod +x "${WORK}/bin/dotnet"

# A PATH without jq: links to exactly the tools the scripts and the stub use.
mkdir -p "${WORK}/nojq"
for tool in bash git grep sort sed paste mkdir dirname rm awk cp cat; do
    ln -s "$(command -v "${tool}")" "${WORK}/nojq/${tool}"
done
ln -s "${WORK}/bin/dotnet" "${WORK}/nojq/dotnet"

FAILURES=0
TESTS=0
CURRENT=""

# Creates a repository with the scripts and the given solution files, and makes it the working directory.
new_repo() {
    CURRENT="${WORK}/repo-${TESTS}"
    mkdir -p "${CURRENT}"
    cd "${CURRENT}"
    git init --quiet
    git config user.email test@example.invalid
    git config user.name test
    cp "${SCRIPTS}/cleanup_code.sh" "${SCRIPTS}/inspect_code.sh" .
    for solution in "$@"; do
        touch "${solution}"
    done
    git add .
    git commit --quiet -m init
    export STUB_LOG="${CURRENT}/dotnet.log"
    export STUB_REPORT="${FIXTURES}/report.xml"
    : > "${STUB_LOG}"
}

# Runs a command and records its exit code and combined output in STATUS and OUTPUT.
run() {
    set +e
    OUTPUT=$("$@" 2>&1)
    STATUS=$?
    set -e
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

expect_call() {
    grep -qxF -- "$1" "${STUB_LOG}" || { OUTPUT=$(cat "${STUB_LOG}"); fail "expected dotnet call: $1"; }
}

expect_no_call() {
    [ ! -s "${STUB_LOG}" ] || { OUTPUT=$(cat "${STUB_LOG}"); fail "expected no dotnet call"; }
}

test_case() {
    TESTS=$((TESTS + 1))
    echo "$1"
}

PATH="${WORK}/bin:${PATH}"

test_case "cleanup: fails without a solution"
new_repo
run ./cleanup_code.sh
expect_status 2
expect_output "No .slnx solution found"
expect_no_call

test_case "cleanup: fails with several solutions"
new_repo A.slnx B.slnx
run ./cleanup_code.sh
expect_status 2
expect_output "Found several .slnx solutions"
expect_output "A.slnx B.slnx"
expect_no_call

test_case "cleanup: rejects unknown arguments"
new_repo Sample.slnx
run ./cleanup_code.sh --verbose
expect_status 2
expect_output "Unknown argument '--verbose'"
expect_no_call

test_case "cleanup: reports when no files changed"
new_repo Sample.slnx
run ./cleanup_code.sh
expect_status 0
expect_output "No matching files to process."
expect_no_call

test_case "cleanup: cleans changed, staged, untracked and renamed files"
new_repo Sample.slnx
mkdir -p src
echo a > src/Modified.cs
echo b > src/Renamed.cs
git add src && git commit --quiet -m files
echo changed >> src/Modified.cs
git mv src/Renamed.cs src/NewName.cs
echo c > src/Untracked.cs
echo d > notes.txt
run ./cleanup_code.sh
expect_status 0
expect_call "jb cleanupcode --profile=Zorn --verbosity=ERROR --include=**/src/Modified.cs;**/src/NewName.cs;**/src/Untracked.cs Sample.slnx"

test_case "cleanup: cleans MSBuild files but leaves plain XML out"
new_repo Sample.slnx
echo p > Directory.Build.props
echo x > data.xml
run ./cleanup_code.sh
expect_status 0
expect_call "jb cleanupcode --profile=Zorn --verbosity=ERROR --include=**/Directory.Build.props Sample.slnx"

test_case "cleanup: leaves Razor files out"
new_repo Sample.slnx
echo r > Page.razor
run ./cleanup_code.sh
expect_status 0
expect_output "No matching files to process."
expect_no_call

test_case "cleanup: --all cleans the whole solution"
new_repo Sample.slnx
echo c > Untracked.cs
run ./cleanup_code.sh --all
expect_status 0
expect_call "jb cleanupcode --profile=Zorn --verbosity=ERROR Sample.slnx"

test_case "cleanup: runs from a subdirectory"
new_repo Sample.slnx
mkdir -p src
run bash -c "cd src && ../cleanup_code.sh --all"
expect_status 0
expect_call "jb cleanupcode --profile=Zorn --verbosity=ERROR Sample.slnx"

test_case "inspect: fails without jq"
new_repo Sample.slnx
run env PATH="${WORK}/nojq" bash ./inspect_code.sh --all
expect_status 2
expect_output "inspect_code.sh needs jq"
expect_no_call

test_case "inspect: fails without a solution"
new_repo
run ./inspect_code.sh --all
expect_status 2
expect_output "No .slnx solution found"
expect_no_call

test_case "inspect: fails with several solutions"
new_repo A.slnx B.slnx
run ./inspect_code.sh --all
expect_status 2
expect_output "Found several .slnx solutions"
expect_no_call

test_case "inspect: --all shows every finding grouped by project without a manifest"
new_repo Sample.slnx
run ./inspect_code.sh --all
expect_status 0
expect_call "jb inspectcode --caches-home=tmp/inspectcode-caches --format=Xml --absolute-paths --output=tmp/inspectcode-report.xml --verbosity=ERROR --severity=WARNING Sample.slnx"
expect_output "Solution ${CURRENT}/Sample.slnx"
expect_output "    Project Sample.Core"
expect_output "      src/Sample.Core/Widget.cs:12 Non-nullable property 'Name' is uninitialized."
expect_output "      src/Sample.Core/Widget.cs:20 Possible 'System.NullReferenceException'"
expect_output "    Project Sample.Web"
expect_output "      src/Sample.Web/Page.cs:7 Non-nullable field '_service' is uninitialized."

test_case "inspect: drops suppressed inspection IDs"
new_repo Sample.slnx
echo '{ "version": "1.0.0", "profiles": ["common", "dotnet"], "inspection": { "suppressedIds": ["CSharpWarnings::CS8618"] } }' > .repo-baseline.json
run ./inspect_code.sh --all
expect_status 0
expect_output "    Project Sample.Core"
expect_output "Possible 'System.NullReferenceException'"
expect_no_output "is uninitialized"
expect_no_output "Project Sample.Web"

test_case "inspect: an empty suppression list drops nothing"
new_repo Sample.slnx
echo '{ "version": "1.0.0", "profiles": ["common", "dotnet"], "inspection": { "suppressedIds": [] } }' > .repo-baseline.json
run ./inspect_code.sh --all
expect_status 0
expect_output "Project Sample.Web"

test_case "inspect: rejects an invalid suppression list"
new_repo Sample.slnx
echo '{ "inspection": { "suppressedIds": "CSharpWarnings::CS8618" } }' > .repo-baseline.json
run ./inspect_code.sh --all
expect_status 2
expect_output "Cannot read inspection.suppressedIds"
expect_no_call

test_case "inspect: keeps an explicit severity"
new_repo Sample.slnx
run ./inspect_code.sh --all -e=SUGGESTION
expect_status 0
expect_call "jb inspectcode --caches-home=tmp/inspectcode-caches --format=Xml --absolute-paths --output=tmp/inspectcode-report.xml --verbosity=ERROR -e=SUGGESTION Sample.slnx"

test_case "inspect: inspects changed C# files only"
new_repo Sample.slnx
echo c > Changed.cs
echo d > notes.json
run ./inspect_code.sh
expect_status 0
expect_call "jb inspectcode --caches-home=tmp/inspectcode-caches --format=Xml --absolute-paths --output=tmp/inspectcode-report.xml --verbosity=ERROR --severity=WARNING --include=**/Changed.cs Sample.slnx"

test_case "inspect: inspects changed Razor files"
new_repo Sample.slnx
echo r > Page.razor
echo c > Changed.cs
run ./inspect_code.sh
expect_status 0
expect_call "jb inspectcode --caches-home=tmp/inspectcode-caches --format=Xml --absolute-paths --output=tmp/inspectcode-report.xml --verbosity=ERROR --severity=WARNING --include=**/Changed.cs;**/Page.razor Sample.slnx"

test_case "inspect: reports when no C# files changed"
new_repo Sample.slnx
echo d > notes.json
run ./inspect_code.sh
expect_status 0
expect_output "No matching files to process."
expect_no_call

echo
if [ "${FAILURES}" -eq 0 ]; then
    echo "All ${TESTS} tests passed."
else
    echo "${FAILURES} check(s) failed in ${TESTS} tests."
    exit 1
fi
