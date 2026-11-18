#!/bin/bash
# Packs the profile content of this repository into the archive of a profile release.
#
# Usage: tool/pack-profiles.sh <version> <output-directory>
#
# Writes <output-directory>/profiles.tar.gz with the tracked files of profiles/, the LICENSE and a generated
# release.json, and <output-directory>/profiles.tar.gz.sha256 in the format of sha256sum. release.json holds the
# format version, the profile version and the minimum tool version from tool/minimum-tool-version. The archive is
# deterministic: names are sorted, every entry has the time of the last commit and owner and group 0, modes
# are 644 or 755, and gzip stores no name or time. Building the same commit twice gives the same bytes, so the
# script refuses to pack when LICENSE, profiles/ or tool/minimum-tool-version differ from HEAD.
set -euo pipefail

if [ "$#" -ne 2 ]; then
    echo "Usage: tool/pack-profiles.sh <version> <output-directory>" >&2
    exit 2
fi

VERSION="$1"
OUTPUT="$2"

# The same rule as the tool's version check.
if ! [[ "${VERSION}" =~ ^[0-9A-Za-z][0-9A-Za-z.+-]*$ ]]; then
    echo "error: '${VERSION}' is not a valid version; use letters, digits, '.', '+' and '-', starting with a letter or digit." >&2
    exit 2
fi

ROOT="$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)"

# tar reads the working tree, so uncommitted changes would end up in an archive with the commit's time.
if ! git -C "${ROOT}" diff --quiet HEAD -- LICENSE profiles tool/minimum-tool-version; then
    echo "error: LICENSE, profiles/ or tool/minimum-tool-version differ from HEAD; commit or stash the changes before packing a release." >&2
    exit 2
fi

# The oldest tool that can read these profiles. The tool refuses archives that need a newer one.
if ! git -C "${ROOT}" ls-files --error-unmatch tool/minimum-tool-version > /dev/null 2>&1; then
    echo "error: tool/minimum-tool-version is not tracked." >&2
    exit 2
fi
MINIMUM_TOOL_VERSION="$(cat "${ROOT}/tool/minimum-tool-version")"
# SemVer 2.0 without build metadata, which has no meaning for a minimum.
SEMVER_IDENTIFIER='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
if ! [[ "${MINIMUM_TOOL_VERSION}" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-${SEMVER_IDENTIFIER}(\.${SEMVER_IDENTIFIER})*)?$ ]]; then
    echo "error: tool/minimum-tool-version holds '${MINIMUM_TOOL_VERSION}', which is not a SemVer 2.0 version without build metadata." >&2
    exit 2
fi

COMMIT_TIME="$(git -C "${ROOT}" log -1 --format=%ct)"
STAGING="$(mktemp -d)"
trap 'rm -rf "${STAGING}"' EXIT

printf '{\n  "format": 2,\n  "version": "%s",\n  "minimumToolVersion": "%s"\n}\n' "${VERSION}" "${MINIMUM_TOOL_VERSION}" > "${STAGING}/release.json"

mkdir -p "${OUTPUT}"
OUTPUT="$(cd "${OUTPUT}" && pwd)"

# GNU tar also reads options from the environment variable TAR_OPTIONS, which would change the archive.
unset TAR_OPTIONS

# Only tracked files, so that stray files in a working tree never reach a release.
PACK_TAR_OPTIONS=(--sort=name --format=gnu --mtime="@${COMMIT_TIME}" --owner=0 --group=0 --numeric-owner --mode='u=rwX,go=rX')
{
    git -C "${ROOT}" ls-files -z -- LICENSE profiles | tar -c "${PACK_TAR_OPTIONS[@]}" -C "${ROOT}" --null -T - -f "${STAGING}/content.tar"
    tar -r "${PACK_TAR_OPTIONS[@]}" -C "${STAGING}" -f "${STAGING}/content.tar" release.json
}
gzip -n -9 < "${STAGING}/content.tar" > "${OUTPUT}/profiles.tar.gz"
(cd "${OUTPUT}" && sha256sum profiles.tar.gz > profiles.tar.gz.sha256)

echo "${OUTPUT}/profiles.tar.gz"
echo "${OUTPUT}/profiles.tar.gz.sha256"
