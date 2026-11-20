#!/bin/bash
# Tests for tool/pack-profiles.sh, which packs the archive of a profile release, and for the release scripts in
# tool/release/, which both release workflows run.
#
# The tests copy the packing script into throwaway Git repositories under tmp/ with fixture profiles, so that the
# real profiles do not affect them. They need bash, Git, jq, GNU tar, gzip and sha256sum, and no network access.
set -euo pipefail

# Throwaway repositories must not depend on the user's or system's Git configuration,
# such as commit signing or hooks.
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="${ROOT}/tmp/release-tests"
FIXTURE="${WORK}/repository"

rm -rf "${WORK}"
mkdir -p "${FIXTURE}/tool"

FAILURES=0
TESTS=0
OUTPUT=""
STATUS=0

# Writes a file from its arguments, one line each, creating parent directories.
write() {
    local path="$1"
    shift
    mkdir -p "$(dirname "${path}")"
    printf '%s\n' "$@" > "${path}"
}

# Runs the fixture's copy of the script and records its exit code and combined output.
pack() {
    set +e
    OUTPUT=$(bash "${FIXTURE}/tool/pack-profiles.sh" "$@" 2>&1)
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

test_case() {
    TESTS=$((TESTS + 1))
    echo "$1"
}

cp "${ROOT}/tool/pack-profiles.sh" "${FIXTURE}/tool/pack-profiles.sh"
write "${FIXTURE}/tool/minimum-tool-version" '0.3.0-rc.1'
write "${FIXTURE}/LICENSE" 'MIT License'
write "${FIXTURE}/profiles/common/profile.json" '{ "blocks": [] }'
write "${FIXTURE}/profiles/common/files/CLAUDE.md" 'Read AGENTS.md.'
write "${FIXTURE}/profiles/dotnet/files/cleanup.sh" '#!/bin/bash' 'echo cleanup'
chmod 775 "${FIXTURE}/profiles/dotnet/files/cleanup.sh"
chmod 664 "${FIXTURE}/profiles/common/files/CLAUDE.md"
write "${FIXTURE}/README.md" 'Not part of a release.'
git -C "${FIXTURE}" init --quiet --initial-branch=main
git -C "${FIXTURE}" -c user.email=test@example.invalid -c user.name=test add --all
git -C "${FIXTURE}" -c user.email=test@example.invalid -c user.name=test commit --quiet -m fixture
# Untracked, so it must stay out of the archive.
write "${FIXTURE}/profiles/common/files/stray.md" 'Stray.'

test_case "pack: the archive holds the tracked profiles, the LICENSE and release.json"
pack 1.2.3 "${WORK}/first"
expect_status 0
expect_output "${WORK}/first/profiles.tar.gz"
listing="$(tar -tzvf "${WORK}/first/profiles.tar.gz")"
names="$(tar -tzf "${WORK}/first/profiles.tar.gz")"
expected_names="$(printf '%s\n' LICENSE profiles/common/files/CLAUDE.md profiles/common/profile.json profiles/dotnet/files/cleanup.sh release.json)"
[ "${names}" = "${expected_names}" ] || { OUTPUT="${names}"; fail "expected exactly the tracked files and release.json, in this order"; }
grep -qE '^-rwxr-xr-x 0/0 .* profiles/dotnet/files/cleanup.sh$' <<< "${listing}" || { OUTPUT="${listing}"; fail "expected the script with mode 755 and owner 0/0"; }
grep -qE '^-rw-r--r-- 0/0 .* profiles/common/files/CLAUDE.md$' <<< "${listing}" || { OUTPUT="${listing}"; fail "expected a regular file with mode 644 and owner 0/0"; }
commit_time="$(git -C "${FIXTURE}" log -1 --format=%ct)"
while IFS= read -r name; do
    # tar sets TAR_MTIME for the command it runs per entry, so the single quotes are intended.
    # shellcheck disable=SC2016
    member_time="$(tar -xzf "${WORK}/first/profiles.tar.gz" --to-command='echo "${TAR_MTIME}"' "${name}")"
    [ "${member_time}" = "${commit_time}" ] || { OUTPUT="${name}: ${member_time}"; fail "expected every entry to have the commit time ${commit_time}"; }
done <<< "${names}"

test_case "pack: release.json holds the format, the version and the minimum tool version"
release="$(tar -xzOf "${WORK}/first/profiles.tar.gz" release.json)"
expected_release="$(printf '%s\n' '{' '  "format": 2,' '  "version": "1.2.3",' '  "minimumToolVersion": "0.3.0-rc.1"' '}')"
[ "${release}" = "${expected_release}" ] || { OUTPUT="${release}"; fail "expected release.json with format 2, version 1.2.3 and minimum tool version 0.3.0-rc.1"; }

test_case "pack: the checksum file verifies the archive in the format of sha256sum"
checksum="$(cat "${WORK}/first/profiles.tar.gz.sha256")"
[[ "${checksum}" =~ ^[0-9a-f]{64}\ \ profiles\.tar\.gz$ ]] || { OUTPUT="${checksum}"; fail "expected '<sha256>  profiles.tar.gz'"; }
(cd "${WORK}/first" && sha256sum --check --quiet profiles.tar.gz.sha256) || fail "expected the checksum to match the archive"

test_case "pack: building the same commit again gives the same bytes, whatever the time, the umask and TAR_OPTIONS"
sleep 1
chmod 644 "${FIXTURE}/profiles/common/files/CLAUDE.md"
(umask 077 && TAR_OPTIONS=--format=v7 bash "${FIXTURE}/tool/pack-profiles.sh" 1.2.3 "${WORK}/second" > /dev/null)
cmp -s "${WORK}/first/profiles.tar.gz" "${WORK}/second/profiles.tar.gz" || fail "expected byte-identical archives"
cmp -s "${WORK}/first/profiles.tar.gz.sha256" "${WORK}/second/profiles.tar.gz.sha256" || fail "expected identical checksum files"

test_case "pack: changes to tracked profiles, the LICENSE or the minimum tool version are refused, staged or not, and nothing is written"
write "${FIXTURE}/profiles/common/files/CLAUDE.md" 'Uncommitted.'
pack 1.2.3 "${WORK}/dirty"
expect_status 2
expect_output "error: LICENSE, profiles/ or tool/minimum-tool-version differ from HEAD; commit or stash the changes before packing a release."
[ -e "${WORK}/dirty" ] && fail "expected nothing to be written for an uncommitted change"
git -C "${FIXTURE}" add profiles/common/files/CLAUDE.md
pack 1.2.3 "${WORK}/staged"
expect_status 2
[ -e "${WORK}/staged" ] && fail "expected nothing to be written for a staged change"
git -C "${FIXTURE}" reset --quiet --hard
write "${FIXTURE}/tool/minimum-tool-version" '0.4.0'
pack 1.2.3 "${WORK}/minimum"
expect_status 2
[ -e "${WORK}/minimum" ] && fail "expected nothing to be written for an uncommitted minimum tool version"
git -C "${FIXTURE}" reset --quiet --hard

test_case "pack: a minimum tool version that is not SemVer 2.0 without build metadata is refused, and nothing is written"
for minimum in '1.0' 'v1.0.0' '01.0.0' '1.0.0+build' '1.0.0-' 'latest'; do
    write "${FIXTURE}/tool/minimum-tool-version" "${minimum}"
    git -C "${FIXTURE}" -c user.email=test@example.invalid -c user.name=test commit --quiet -am "minimum ${minimum}"
    pack 1.2.3 "${WORK}/invalid-minimum"
    expect_status 2
    expect_output "error: tool/minimum-tool-version holds '${minimum}', which is not a SemVer 2.0 version without build metadata."
    [ -e "${WORK}/invalid-minimum" ] && fail "expected nothing to be written for minimum tool version '${minimum}'"
    git -C "${FIXTURE}" reset --quiet --hard HEAD~1
done

test_case "pack: an untracked minimum tool version is refused"
git -C "${FIXTURE}" rm --quiet --cached tool/minimum-tool-version
git -C "${FIXTURE}" -c user.email=test@example.invalid -c user.name=test commit --quiet -m "untrack the minimum"
write "${FIXTURE}/tool/minimum-tool-version" '0.3.0'
pack 1.2.3 "${WORK}/untracked"
expect_status 2
expect_output "error: tool/minimum-tool-version is not tracked."
git -C "${FIXTURE}" reset --quiet --hard HEAD~1

test_case "pack: an invalid version or missing arguments are usage errors, and nothing is written"
for version in '' '-1.0' 'v1.0/x' '1.0 beta'; do
    pack "${version}" "${WORK}/invalid"
    expect_status 2
    [ -e "${WORK}/invalid" ] && fail "expected nothing to be written for version '${version}'"
done
pack 1.2.3
expect_status 2
expect_output "Usage: tool/pack-profiles.sh <version> <output-directory>"

# --- Release scripts ---------------------------------------------------------------------------------------------------
#
# The scripts of tool/release/ run against the doubles in tests/release/doubles/, which take the place of git, gh, curl
# and dotnet. Each test runs in its own subshell with a local bare repository as origin, a fake nuget.org flat container
# and fake GitHub releases. The git double runs the real git against these repositories; the other doubles replay the
# fake state. Every double records its calls, so the tests assert which writes happen and that nothing published is
# overwritten or deleted. No test needs the network.

REAL_GIT="$(command -v git)"
export REAL_GIT

readonly OTHER_COMMIT=0123456789abcdef0123456789abcdef01234567
# Every call that changes remote or repository state.
readonly WRITE_CALLS='^(git( .*)? (push|tag)( |$)|gh release (create|upload|edit|delete|delete-asset)( |$)|dotnet nuget (push|delete)( |$))'
# Calls that would overwrite or delete published state. Deleting an incomplete release asset is allowed; the gh double
# records the deletion of a complete one and every change to a published release.
readonly DESTRUCTIVE_CALLS='^(git( .*)? push( .*)? (--force|--force-with-lease|-f|--delete|-d|--mirror|\+[^ ]*)( |$)|gh( .*)? (--clobber|delete)( |$)|dotnet nuget delete)'

git_real() {
    "${REAL_GIT}" "$@"
}

commit_all() {
    git_real add --all
    git_real -c user.name=Test -c user.email=test@example.invalid commit --quiet --message "$1"
}

# Creates the test directory T with the doubles on PATH, origin and a source repository with fixture profiles, and
# sets the environment of a release of version 1.2.3 of the tool.
setup_release_test() {
    T="${WORK}/scripts/$1"
    mkdir -p "${T}/bin" "${T}/state"
    cp "${ROOT}/tests/release/doubles/"* "${T}/bin/"

    export PATH="${T}/bin:${PATH}"
    export DOUBLE_STATE="${T}/state" DOUBLE_CALLS="${T}/calls" DOUBLE_ORIGIN="${T}/origin.git"
    : > "${DOUBLE_CALLS}"

    git_real init --quiet --bare --initial-branch=main "${DOUBLE_ORIGIN}"
    git_real init --quiet --initial-branch=main "${T}/source"
    cd "${T}/source"
    git_real remote add origin "${DOUBLE_ORIGIN}"
    mkdir -p tool
    cp "${ROOT}/tool/pack-profiles.sh" tool/pack-profiles.sh
    write tool/minimum-tool-version '0.1.0'
    write LICENSE 'MIT License'
    write profiles/common/profile.json '{ "blocks": [] }'
    commit_all "Previous commit"
    PREVIOUS_COMMIT="$(git_real rev-parse HEAD)"
    write profiles/common/files/CLAUDE.md 'Read AGENTS.md.'
    commit_all "Release commit"
    git_real push --quiet origin main

    GITHUB_SHA="$(git_real rev-parse HEAD)"
    export GITHUB_SHA PREVIOUS_COMMIT
    export GITHUB_REF=refs/heads/main GITHUB_OUTPUT="${T}/github-output"
    export STREAM=tool VERSION=1.2.3
    export GH_TOKEN=token GH_REPO=chA0s-Chris/repo-baseline
    export NOTES_FILE="${T}/artifacts/release-notes/body.md"
    export PACKAGE_DIR="${T}/artifacts/tool" ASSET_DIR="${T}/artifacts/profiles"
    export NUGET_KEY=short-lived-key NUGET_FLATCONTAINER_URL=https://nuget.test/v3-flatcontainer
    export NUGET_POLL_ATTEMPTS=3 NUGET_POLL_INTERVAL=0
    unset DOUBLE_GH_HIDE_DRAFTS DOUBLE_GH_FAIL_UPLOAD DOUBLE_GH_STARTER_UPLOAD DOUBLE_GIT_FAIL_PUSH DOUBLE_CURL_FAIL \
        DOUBLE_DOTNET_FAIL_PUSH DOUBLE_INDEX_DELAY DOUBLE_PACKED_COMMIT NUGET_SOURCE

    # Release notes with characters that a shell would interpret.
    # shellcheck disable=SC2089,SC2090 # The quotes and backslashes are meant to be literal.
    DRAFTED_BODY="### Features

- Quote \"double\" and 'single' (#2) by @a
- Backtick \`touch ${T}/pwned-backtick\` (#3) by @b
- Substitution \$(touch ${T}/pwned-substitution) and \${HOME} (#4) by @c
- Percent 100% %s %n and backslash \\n (#5) by @d"
    # shellcheck disable=SC2090
    export DRAFTED_BODY

    seed_tool_versions 0.1.0
}

# Makes the fake flat container list the given tool versions.
seed_tool_versions() {
    local index="${DOUBLE_STATE}/web/v3-flatcontainer/chaos.repobaseline/index.json"
    mkdir -p "$(dirname "${index}")"
    printf '%s\n' "$@" | jq --raw-input . | jq --slurp '{ versions: . }' > "${index}"
}

# Makes the fake flat container serve the tool package in version $1 with nuspec version $2 and commit $3.
seed_package() {
    local nuspec="${DOUBLE_STATE}/web/v3-flatcontainer/chaos.repobaseline/${1,,}/chaos.repobaseline.nuspec"
    mkdir -p "$(dirname "${nuspec}")"
    printf '<package><metadata><id>Chaos.RepoBaseline</id><version>%s</version><repository type="git" url="https://github.com/chA0s-Chris/repo-baseline" commit="%s" /></metadata></package>\n' \
        "$2" "$3" > "${nuspec}"
}

# Pushes the tag $1 at commit $2 to origin without recording the call.
seed_tag() {
    git_real -C "${T}/source" -c user.name=Test -c user.email=test@example.invalid tag --annotate "$1" --message "$1" "$2"
    git_real -C "${T}/source" push --quiet origin "refs/tags/$1"
}

# Creates the release $1 with draft flag $2 and body $3, and assets with the names in the remaining arguments.
seed_release() {
    local tag="$1" draft="$2" body="$3" release="${DOUBLE_STATE}/releases/$1" asset
    shift 3
    mkdir -p "${release}/assets"
    printf '%s\n' "${draft}" > "${release}/draft"
    printf 'false\n' > "${release}/prerelease"
    printf 'false\n' > "${release}/latest"
    printf '%s\n' "${tag}" > "${release}/title"
    printf '%s\n' "${body}" > "${release}/body"
    for asset in "$@"; do
        printf 'original\n' > "${release}/assets/${asset}"
    done
}

# Checks out the dispatched commit in a fresh clone, like actions/checkout does for every job.
checkout() {
    cd "${T}"
    rm -rf "${T}/job"
    git_real clone --quiet "${DOUBLE_ORIGIN}" "${T}/job"
    cd "${T}/job"
    git_real -c advice.detachedHead=false checkout --quiet "${GITHUB_SHA}"
}

script() {
    bash "${ROOT}/tool/release/$1" >> "${T}/output" 2>&1
}

# Runs the jobs of the release workflow of $STREAM in their order, from job $1 (default: prepare) up to job $2 (default:
# release), and stops at the first failure like "needs:" does. Starting at a later job is "Re-run failed jobs": the
# outputs and artifacts of the earlier jobs are reused. The ci job is a stub that packs what ci.yml would pack.
run_release() {
    local from="${1:-prepare}" until="${2:-release}" started=false job

    for job in prepare ci publish release; do
        [[ ${job} == "${from}" || ${started} == true ]] || continue
        started=true

        case "${job}" in
            prepare)
                rm -rf "$(dirname "${NOTES_FILE}")"
                : > "${GITHUB_OUTPUT}"
                checkout
                script validate.sh || return 1
                # The prepare job only has read permissions, which do not show drafts.
                DOUBLE_GH_HIDE_DRAFTS=1 script preflight.sh || return 1
                DOUBLE_GH_HIDE_DRAFTS=1 script release-notes.sh || return 1
                ;;
            ci)
                checkout
                if [[ ${STREAM} == tool ]]; then
                    rm -rf "${PACKAGE_DIR}"
                    mkdir -p "${PACKAGE_DIR}"
                    printf 'Chaos.RepoBaseline %s\n' "${VERSION}" > "${PACKAGE_DIR}/Chaos.RepoBaseline.${VERSION}.nupkg"
                else
                    rm -rf "${ASSET_DIR}"
                    bash tool/pack-profiles.sh "${VERSION}" "${ASSET_DIR}" >> "${T}/output" 2>&1 || return 1
                fi
                ;;
            publish)
                [[ ${STREAM} == tool ]] || continue
                checkout
                script publish.sh || return 1
                ;;
            release)
                checkout
                if [[ ${STREAM} == tool ]]; then
                    script verify-published.sh || return 1
                fi
                script release.sh || return 1
                ;;
        esac

        [[ ${job} != "${until}" ]] || break
    done
}

fail_test() {
    printf 'Assertion failed: %s\n' "$*"
    printf -- '--- calls\n'
    cat "${DOUBLE_CALLS}"
    if [[ -f ${T}/output ]]; then
        printf -- '--- output\n'
        cat "${T}/output"
    fi
    exit 1
}

assert_succeeds() {
    "$@" || fail_test "expected success: $*"
}

assert_fails() {
    if "$@"; then
        fail_test "expected failure: $*"
    fi
}

assert_output_contains() {
    grep -qF -- "$1" "${T}/output" || fail_test "output does not contain '$1'"
}

# Asserts that exactly $1 calls match the extended regular expression $2.
assert_calls() {
    local actual
    actual="$(grep -cE -- "$2" "${DOUBLE_CALLS}" || true)"
    [[ ${actual} == "$1" ]] || fail_test "expected $1 calls matching '$2', got ${actual}"
}

assert_no_writes() {
    assert_calls 0 "${WRITE_CALLS}"
}

assert_equals() {
    [[ $1 == "$2" ]] || fail_test "expected '$2', got '$1'${3:+ ($3)}"
}

assert_file_content() {
    [[ -f $1 ]] || fail_test "$1 does not exist"
    assert_equals "$(< "$1")" "$2" "$1"
}

# Asserts that the release $1 is published with the pre-release flag $2, the latest flag $3 and exactly the assets
# named in the remaining arguments.
assert_release() {
    local tag="$1" release="${DOUBLE_STATE}/releases/$1" prerelease="$2" latest="$3"
    shift 3
    [[ -d ${release} ]] || fail_test "the release ${tag} does not exist"
    assert_file_content "${release}/draft" false
    assert_file_content "${release}/prerelease" "${prerelease}"
    assert_file_content "${release}/latest" "${latest}"
    assert_file_content "${release}/title" "${tag}"
    local expected=""
    (($# == 0)) || expected="$(printf '%s\n' "$@" | LC_ALL=C sort)"
    assert_equals "$(find "${release}/assets" -type f -printf '%f\n' | LC_ALL=C sort)" "${expected}" "assets of ${tag}"
}

assert_no_release() {
    [[ ! -e ${DOUBLE_STATE}/releases/$1 ]] || fail_test "the release $1 exists"
}

# Asserts that the tag $1 on origin is an annotated tag by github-actions[bot] at the commit $2.
assert_remote_tag() {
    assert_equals "$(git_real --git-dir="${DOUBLE_ORIGIN}" cat-file -t "refs/tags/$1")" tag "type of tag $1"
    assert_equals "$(git_real --git-dir="${DOUBLE_ORIGIN}" rev-parse "refs/tags/$1^{commit}")" "$2" "commit of tag $1"
    assert_equals "$(git_real --git-dir="${DOUBLE_ORIGIN}" for-each-ref --format='%(taggername)' "refs/tags/$1")" \
        'github-actions[bot]' "tagger of $1"
}

assert_no_remote_tag() {
    if git_real --git-dir="${DOUBLE_ORIGIN}" rev-parse --quiet --verify "refs/tags/$1" > /dev/null; then
        fail_test "the tag $1 exists"
    fi
}

assert_no_execution() {
    [[ ! -e ${T}/pwned-backtick && ! -e ${T}/pwned-substitution && ! -e ${T}/pwned-version ]] ||
        fail_test "untrusted text was executed"
}

# Asserts the complete outcome of a successful release of $VERSION in $STREAM.
assert_complete_release() {
    local prerelease=false latest=false
    [[ ${VERSION} != *-* ]] || prerelease=true
    [[ ${prerelease} == true || ${STREAM} == tool ]] || latest=true
    if [[ ${STREAM} == tool ]]; then
        assert_remote_tag "tool/v${VERSION}" "${GITHUB_SHA}"
        assert_release "tool/v${VERSION}" "${prerelease}" "${latest}"
        [[ -f ${DOUBLE_STATE}/web/v3-flatcontainer/chaos.repobaseline/${VERSION,,}/chaos.repobaseline.nuspec ]] ||
            fail_test "the package was not pushed"
    else
        assert_remote_tag "v${VERSION}" "${GITHUB_SHA}"
        assert_release "v${VERSION}" "${prerelease}" "${latest}" profiles.tar.gz profiles.tar.gz.sha256
    fi
}

# --- Validation

release_test_valid_versions_are_accepted() {
    local version expected stream tag
    for stream in tool profiles; do
        for version in 0.1.0 1.2.3 10.20.30 0.8.0-preview.1 1.0.0-rc.1 1.0.0-alpha.beta 1.0.0-x-y.0 1.0.0-0A.is.legal; do
            expected=false
            [[ ${version} != *-* ]] || expected=true
            tag="v${version}"
            [[ ${stream} == profiles ]] || tag="tool/v${version}"
            : > "${GITHUB_OUTPUT}"
            STREAM="${stream}" VERSION="${version}" assert_succeeds script validate.sh
            assert_file_content "${GITHUB_OUTPUT}" "$(printf 'tag=%s\nprerelease=%s' "${tag}" "${expected}")"
        done
    done
    assert_no_writes
}

release_test_invalid_versions_fail_before_any_side_effect() {
    local version
    # shellcheck disable=SC2016 # The command substitution must stay literal.
    for version in '' v1.2.3 1.2 1.2.3.4 01.2.3 1.02.3 1.2.3-01 1.2.3- 1.2.3+build 1.2.3-rc.1+build ' 1.2.3' \
        "$(printf '1.2.3\n1.2.4')" '1.2.3-a..b' '1.2.3-rc_1' '$(touch '"${T}"'/pwned-version)'; do
        VERSION="${version}" assert_fails script validate.sh
    done
    assert_output_contains "is not a SemVer 2.0.0 version without build metadata and leading v."
    assert_no_execution
    assert_calls 0 '^(git|gh|curl|dotnet) '
}

release_test_versions_that_git_cannot_tag_fail_before_any_side_effect() {
    local stream
    for stream in tool profiles; do
        STREAM="${stream}" VERSION=1.2.3-rc.lock assert_fails script validate.sh
    done
    assert_output_contains "Git does not accept tool/v1.2.3-rc.lock as a tag name."
    assert_output_contains "Git does not accept v1.2.3-rc.lock as a tag name."
    assert_no_writes
}

release_test_other_refs_fail_before_any_side_effect() {
    local ref
    for ref in refs/heads/feature refs/tags/v1.2.3 refs/pull/1/merge ''; do
        GITHUB_REF="${ref}" assert_fails script validate.sh
    done
    assert_output_contains "Releases only run from refs/heads/main"
    assert_calls 0 '^(git|gh|curl|dotnet) '
}

release_test_unknown_stream_fails() {
    STREAM=other assert_fails script validate.sh
    assert_output_contains "The stream other is neither 'tool' nor 'profiles'."
}

# --- Preflight

release_test_tag_at_another_commit_fails_before_any_side_effect() {
    local stream
    seed_tag tool/v1.2.3 "${PREVIOUS_COMMIT}"
    seed_tag v1.2.3 "${PREVIOUS_COMMIT}"
    for stream in tool profiles; do
        STREAM="${stream}" assert_fails run_release
    done
    assert_output_contains "The tag 'tool/v1.2.3' points to commit '${PREVIOUS_COMMIT}' instead of '${GITHUB_SHA}'."
    assert_output_contains "The tag 'v1.2.3' points to commit '${PREVIOUS_COMMIT}' instead of '${GITHUB_SHA}'."
    assert_no_writes
}

release_test_tag_with_other_casing_fails_before_any_side_effect() {
    VERSION=1.2.3-RC.1
    seed_tag tool/v1.2.3-rc.1 "${GITHUB_SHA}"
    assert_fails run_release
    assert_output_contains "The tag 'tool/v1.2.3-rc.1' differs from 'tool/v1.2.3-RC.1' only in case."
    assert_no_writes
}

release_test_lightweight_tag_at_the_dispatched_commit_fails_preflight() {
    git_real -C "${T}/source" tag tool/v1.2.3 "${GITHUB_SHA}"
    git_real -C "${T}/source" push --quiet origin refs/tags/tool/v1.2.3
    assert_fails run_release prepare prepare
    assert_output_contains "The tag 'tool/v1.2.3' is lightweight. An annotated release tag is required."
    assert_no_writes
}

release_test_tags_of_the_other_stream_do_not_count() {
    seed_tag v1.2.3 "${PREVIOUS_COMMIT}"
    assert_succeeds run_release
    assert_complete_release
}

release_test_release_without_its_tag_fails_before_any_side_effect() {
    seed_release v1.2.3 false "Old notes"
    STREAM=profiles assert_fails run_release
    assert_output_contains "The release v1.2.3 exists, but its tag does not."
    assert_no_writes
}

release_test_package_from_another_commit_fails_before_any_side_effect() {
    seed_package 1.2.3 1.2.3 "${OTHER_COMMIT}"
    assert_fails run_release
    assert_output_contains "nuget.org serves Chaos.RepoBaseline 1.2.3 from commit '${OTHER_COMMIT}' instead of '${GITHUB_SHA}'."
    assert_no_writes
}

release_test_package_with_other_version_casing_fails_before_any_side_effect() {
    VERSION=1.2.3-RC.1
    seed_package 1.2.3-rc.1 1.2.3-rc.1 "${GITHUB_SHA}"
    assert_fails run_release
    assert_output_contains "nuget.org serves Chaos.RepoBaseline as version '1.2.3-rc.1' instead of '1.2.3-RC.1'."
    assert_calls 1 '^curl .*/chaos\.repobaseline/1\.2\.3-rc\.1/chaos\.repobaseline\.nuspec$'
    assert_no_writes
}

release_test_unexpected_feed_answers_fail_before_any_side_effect() {
    mkdir -p "${DOUBLE_STATE}/web/v3-flatcontainer/chaos.repobaseline/1.2.3"
    printf '503' > "${DOUBLE_STATE}/web/v3-flatcontainer/chaos.repobaseline/1.2.3/chaos.repobaseline.nuspec.status"
    assert_fails run_release
    assert_output_contains "nuget.org answered 503 for Chaos.RepoBaseline 1.2.3."
    DOUBLE_CURL_FAIL=1 STREAM=profiles assert_fails run_release
    assert_output_contains "Could not query nuget.org for the versions of Chaos.RepoBaseline."
    assert_no_writes
}

release_test_profiles_fail_before_any_side_effect_until_the_minimum_tool_version_is_listed() {
    STREAM=profiles
    seed_tool_versions 0.0.9 0.1.0-rc.1 0.1.1
    assert_fails run_release
    assert_output_contains "nuget.org does not list Chaos.RepoBaseline 0.1.0, the version in tool/minimum-tool-version."
    rm "${DOUBLE_STATE}/web/v3-flatcontainer/chaos.repobaseline/index.json"
    assert_fails run_release
    assert_no_writes
    assert_no_remote_tag v1.2.3
}

release_test_minimum_tool_version_is_looked_up_in_lowercase() {
    STREAM=profiles
    write "${T}/source/tool/minimum-tool-version" '0.2.0-RC.1'
    (cd "${T}/source" && commit_all "Raise the minimum" && git_real push --quiet origin main)
    GITHUB_SHA="$(git_real -C "${T}/source" rev-parse HEAD)"
    seed_tool_versions 0.1.0 0.2.0-rc.1
    assert_succeeds run_release prepare prepare
    assert_output_contains "nuget.org lists Chaos.RepoBaseline 0.2.0-RC.1, the minimum tool version of the profiles."
}

# --- Complete releases

release_test_stable_tool_release_publishes_the_package_then_tags_and_releases() {
    assert_succeeds run_release
    assert_complete_release
    assert_calls 1 '^dotnet nuget push .*/Chaos\.RepoBaseline\.1\.2\.3\.nupkg --api-key short-lived-key --source https://api\.nuget\.org/v3/index\.json --skip-duplicate$'
    assert_calls 1 '^gh release create tool/v1\.2\.3 --draft --verify-tag --title tool/v1\.2\.3 --notes-file .* --prerelease=true --latest=false$'
    assert_calls 1 '^gh release edit tool/v1\.2\.3 --draft=false --prerelease=false --latest=false$'
    assert_calls 0 '^gh release upload'
    # The package is pushed before the tag, and the tag before the release.
    assert_equals "$(grep -nE "${WRITE_CALLS}" "${DOUBLE_CALLS}" | cut -d' ' -f1-3 | cut -d: -f2-)" \
        "$(printf '%s\n' 'dotnet nuget push' 'git -c user.name=github-actions\[bot\]' 'git push origin' 'gh release create' 'gh release edit')"
}

release_test_tool_pre_release_is_marked_and_not_latest() {
    VERSION=0.2.0-preview.1
    assert_succeeds run_release
    assert_complete_release
    assert_file_content "${DOUBLE_STATE}/releases/tool/v0.2.0-preview.1/prerelease" true
}

release_test_stable_profile_release_uploads_the_packed_archive_and_is_latest() {
    STREAM=profiles VERSION=0.6.0
    assert_succeeds run_release
    assert_complete_release
    assert_calls 0 '^(dotnet|curl .*\.nuspec)'
    assert_equals "$(tar -xzOf "${DOUBLE_STATE}/releases/v0.6.0/assets/profiles.tar.gz" release.json | jq -r .version)" 0.6.0
    cmp -s "${ASSET_DIR}/profiles.tar.gz" "${DOUBLE_STATE}/releases/v0.6.0/assets/profiles.tar.gz" ||
        fail_test "expected the archive that CI packed"
}

release_test_profile_pre_release_is_marked_and_not_latest() {
    STREAM=profiles VERSION=0.8.0-preview.1
    assert_succeeds run_release
    assert_complete_release
}

release_test_release_notes_arrive_verbatim_and_nothing_executes() {
    local stream tag
    for stream in tool profiles; do
        tag=v1.2.3
        [[ ${stream} == profiles ]] || tag=tool/v1.2.3
        STREAM="${stream}" assert_succeeds run_release
        assert_file_content "${DOUBLE_STATE}/releases/${tag}/body" "${DRAFTED_BODY}"
    done
    assert_no_execution
}

release_test_missing_release_notes_fail_before_any_side_effect() {
    DRAFTED_BODY='' assert_fails run_release
    assert_output_contains "release-drafter rendered no release notes."
    assert_no_writes
}

release_test_notes_without_a_previous_release_fail_before_any_side_effect() {
    local stream
    for stream in tool profiles; do
        STREAM="${stream}" DRAFTED_BODY='- No changes since the last release

---
> [!WARNING]
> Release Drafter could not find a previous **published release** for `chA0s-Chris/repo-baseline`.
---' assert_fails run_release
    done
    assert_output_contains "release-drafter found no previous release of tool/v1.2.3's stream to list the changes from."
    assert_output_contains "release-drafter found no previous release of v1.2.3's stream to list the changes from."
    assert_no_writes
}

release_test_archive_of_another_version_fails_before_tagging() {
    STREAM=profiles
    assert_succeeds run_release prepare ci
    (cd "${T}/job" && bash tool/pack-profiles.sh 1.2.4 "${ASSET_DIR}" > /dev/null)
    assert_fails run_release release
    assert_output_contains "profiles.tar.gz was packed as version '1.2.4' instead of '1.2.3'."
    printf 'corrupt' >> "${ASSET_DIR}/profiles.tar.gz"
    assert_fails run_release release
    assert_output_contains "profiles.tar.gz does not match profiles.tar.gz.sha256."
    assert_no_writes
}

release_test_missing_package_fails_the_publish_job() {
    assert_succeeds run_release prepare ci
    rm "${PACKAGE_DIR}/Chaos.RepoBaseline.1.2.3.nupkg"
    assert_fails run_release publish
    assert_output_contains "The package Chaos.RepoBaseline.1.2.3.nupkg is not in ${PACKAGE_DIR}."
    assert_no_writes
}

# --- Reruns

release_test_completed_release_is_not_changed_by_a_new_dispatch() {
    local stream
    for stream in tool profiles; do
        STREAM="${stream}" assert_succeeds run_release
    done
    : > "${DOUBLE_CALLS}"
    for stream in tool profiles; do
        STREAM="${stream}" DRAFTED_BODY='- No changes since the last release' assert_succeeds run_release
        STREAM="${stream}" assert_complete_release
    done
    assert_output_contains "Reusing the notes of the published release tool/v1.2.3."
    assert_output_contains "The release v1.2.3 is already published."
    assert_calls 1 '^dotnet nuget push .* --skip-duplicate$'
    assert_calls 0 '^(git( .*)? (push|tag)|gh release (create|upload|edit|delete-asset))( |$)'
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/body" "${DRAFTED_BODY}"
}

release_test_failed_package_push_is_completed_by_rerunning_failed_jobs() {
    DOUBLE_DOTNET_FAIL_PUSH=1 assert_fails run_release
    assert_no_remote_tag tool/v1.2.3
    assert_no_release tool/v1.2.3
    assert_succeeds run_release publish
    assert_complete_release
}

release_test_package_published_before_a_failure_is_completed_by_a_new_dispatch() {
    DOUBLE_GIT_FAIL_PUSH=1 assert_fails run_release
    assert_no_remote_tag tool/v1.2.3
    assert_succeeds run_release
    assert_complete_release
    assert_output_contains "nuget.org serves Chaos.RepoBaseline 1.2.3 from commit ${GITHUB_SHA}."
}

release_test_delayed_package_visibility_is_awaited() {
    DOUBLE_INDEX_DELAY=2 assert_succeeds run_release
    assert_complete_release
    assert_output_contains "nuget.org does not serve Chaos.RepoBaseline 1.2.3 yet (404)."
}

release_test_visibility_timeout_fails_before_tagging_and_a_rerun_resumes() {
    DOUBLE_INDEX_DELAY=5 assert_fails run_release
    assert_output_contains "nuget.org still does not serve Chaos.RepoBaseline 1.2.3. Re-run the failed jobs to keep waiting."
    assert_no_remote_tag tool/v1.2.3
    assert_succeeds run_release release
    assert_complete_release
}

release_test_package_from_another_commit_after_the_push_fails_before_tagging() {
    DOUBLE_PACKED_COMMIT="${OTHER_COMMIT}" assert_fails run_release
    assert_output_contains "nuget.org serves Chaos.RepoBaseline 1.2.3 from commit '${OTHER_COMMIT}' instead of '${GITHUB_SHA}'."
    assert_no_remote_tag tool/v1.2.3
    assert_no_release tool/v1.2.3
}

release_test_failed_tag_push_is_completed_by_rerunning_failed_jobs() {
    local stream
    for stream in tool profiles; do
        STREAM="${stream}" DOUBLE_GIT_FAIL_PUSH=1 assert_fails run_release
        STREAM="${stream}" assert_succeeds run_release release
        STREAM="${stream}" assert_complete_release
    done
}

release_test_missing_release_is_created_for_an_existing_tag() {
    STREAM=profiles
    seed_tag v1.2.3 "${GITHUB_SHA}"
    assert_succeeds run_release
    assert_release v1.2.3 false true profiles.tar.gz profiles.tar.gz.sha256
    assert_output_contains "The tag v1.2.3 already exists at ${GITHUB_SHA}."
    assert_calls 0 '^git( .*)? (push|tag)( |$)'
}

release_test_tag_conflict_after_preflight_stops_the_release() {
    STREAM=profiles
    assert_succeeds run_release prepare ci
    seed_tag v1.2.3 "${PREVIOUS_COMMIT}"
    assert_fails run_release release
    assert_output_contains "The tag 'v1.2.3' points to commit '${PREVIOUS_COMMIT}' instead of '${GITHUB_SHA}'."
    assert_no_writes
}

release_test_failed_final_tag_lookup_stops_the_release() {
    STREAM=profiles
    assert_succeeds run_release prepare ci
    checkout
    git_real remote set-url origin "${T}/missing-origin.git"
    assert_fails script release.sh
    assert_output_contains "Could not list the tags of origin."
    assert_no_writes
}

release_test_draft_hidden_during_preparation_stops_tag_creation() {
    STREAM=profiles
    seed_release v1.2.3 true "Earlier draft" profiles.tar.gz
    assert_succeeds run_release prepare ci
    assert_fails run_release release
    assert_output_contains "The release v1.2.3 exists, but its tag does not."
    assert_no_remote_tag v1.2.3
    assert_no_writes
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/draft" true
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/body" "Earlier draft"
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/assets/profiles.tar.gz" original
}

release_test_failed_upload_leaves_a_draft_that_a_rerun_completes() {
    STREAM=profiles
    DOUBLE_GH_FAIL_UPLOAD=profiles.tar.gz.sha256 assert_fails run_release
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/draft" true
    # release-drafter skips pre-release drafts, so it cannot take this draft for its own.
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/prerelease" true
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/latest" false
    assert_succeeds run_release release
    assert_complete_release
    assert_output_contains "The release v1.2.3 already has profiles.tar.gz."
    assert_calls 1 '^gh release upload v1\.2\.3 .*/profiles\.tar\.gz$'
    assert_calls 1 '^gh release create '
}

release_test_failed_upload_is_completed_by_a_new_dispatch() {
    STREAM=profiles
    DOUBLE_GH_FAIL_UPLOAD=profiles.tar.gz assert_fails run_release
    # The prepare job cannot see the draft and uses the drafted notes; the draft keeps its own.
    DRAFTED_BODY='- Newer notes' assert_succeeds run_release
    assert_complete_release
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/body" "${DRAFTED_BODY}"
}

release_test_incomplete_asset_is_uploaded_again() {
    STREAM=profiles
    DOUBLE_GH_STARTER_UPLOAD=profiles.tar.gz assert_fails run_release
    assert_succeeds run_release release
    assert_complete_release
    assert_output_contains "The release v1.2.3 has the incomplete asset profiles.tar.gz in state 'starter'. Uploading it again."
    assert_calls 1 '^gh release delete-asset v1\.2\.3 profiles\.tar\.gz --yes$'
}

release_test_published_release_counts_as_complete() {
    STREAM=profiles
    seed_tag v1.2.3 "${GITHUB_SHA}"
    seed_release v1.2.3 false "Published notes" profiles.tar.gz profiles.tar.gz.sha256
    assert_succeeds run_release
    assert_output_contains "The release v1.2.3 is already published."
    assert_no_writes
    assert_file_content "${DOUBLE_STATE}/releases/v1.2.3/assets/profiles.tar.gz" original
}

# Runs every function release_test_* in its own subshell, so that each test starts from a fresh environment.
for name in $(declare -F | sed -n 's/^declare -f \(release_test_.*\)$/\1/p'); do
    test_case "release scripts: ${name#release_test_}"
    # Not in a condition, so that errexit applies inside the subshell.
    set +e
    log="$({
        set -euo pipefail
        setup_release_test "${name}"
        "${name}"
        # Whatever happened, nothing published may be overwritten or deleted.
        if grep -E -- "${DESTRUCTIVE_CALLS}" "${DOUBLE_CALLS}"; then
            fail_test "destructive calls"
        fi
        for record in deleted-uploaded-assets changed-published-releases; do
            [[ ! -e ${DOUBLE_STATE}/${record} ]] || fail_test "${record}: $(< "${DOUBLE_STATE}/${record}")"
        done
    } 2>&1)"
    status=$?
    set -e
    if [ "${status}" -ne 0 ]; then
        OUTPUT="${log}"
        fail "${name}"
    fi
done

echo
if [ "${FAILURES}" -eq 0 ]; then
    # A passing run leaves nothing behind. The fixture is a Git repository, which IDEs pick up as a VCS root.
    cd "${ROOT}"
    rm -rf "${WORK}"
    echo "All ${TESTS} tests passed."
else
    echo "${FAILURES} check(s) failed in ${TESTS} tests. The test repository is kept in ${WORK}."
    exit 1
fi
