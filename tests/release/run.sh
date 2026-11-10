#!/bin/bash
# Tests for tool/pack-profiles.sh, which packs the archive of a profile release.
#
# The tests copy the script into a throwaway Git repository under tmp/ with fixture profiles, so that the
# real profiles do not affect them. They need bash, Git, GNU tar, gzip and sha256sum.
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

test_case "pack: release.json holds the format and the version"
release="$(tar -xzOf "${WORK}/first/profiles.tar.gz" release.json)"
expected_release="$(printf '%s\n' '{' '  "format": 1,' '  "version": "1.2.3"' '}')"
[ "${release}" = "${expected_release}" ] || { OUTPUT="${release}"; fail "expected release.json with format 1 and version 1.2.3"; }

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

test_case "pack: changes to tracked profiles or the LICENSE are refused, staged or not, and nothing is written"
write "${FIXTURE}/profiles/common/files/CLAUDE.md" 'Uncommitted.'
pack 1.2.3 "${WORK}/dirty"
expect_status 2
expect_output "error: LICENSE or profiles/ differ from HEAD; commit or stash the changes before packing a release."
[ -e "${WORK}/dirty" ] && fail "expected nothing to be written for an uncommitted change"
git -C "${FIXTURE}" add profiles/common/files/CLAUDE.md
pack 1.2.3 "${WORK}/staged"
expect_status 2
[ -e "${WORK}/staged" ] && fail "expected nothing to be written for a staged change"
git -C "${FIXTURE}" reset --quiet --hard

test_case "pack: an invalid version or missing arguments are usage errors, and nothing is written"
for version in '' '-1.0' 'v1.0/x' '1.0 beta'; do
    pack "${version}" "${WORK}/invalid"
    expect_status 2
    [ -e "${WORK}/invalid" ] && fail "expected nothing to be written for version '${version}'"
done
pack 1.2.3
expect_status 2
expect_output "Usage: tool/pack-profiles.sh <version> <output-directory>"

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
