#!/usr/bin/env bash
# Fails if the version already has a tag, a GitHub release or a package on nuget.org that does not belong to the
# dispatched commit, so that a rerun can only complete the release of the same commit. For a profile release, it also
# fails unless nuget.org has the tool version in tool/minimum-tool-version. Only reads.
#
# Environment: STREAM, VERSION, GITHUB_SHA, GH_TOKEN, GH_REPO, optionally NUGET_FLATCONTAINER_URL.
set -euo pipefail
# shellcheck source=lib.sh source-path=SCRIPTDIR
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_env STREAM VERSION GITHUB_SHA

tag="$(release_tag)"
tag_state="$(remote_tag_state)"
log "Tag $tag: $tag_state."

# The tag is created before the release, so a release without its tag was not created by a release workflow.
if [[ $tag_state == absent ]]; then
    status=0
    release_query "$tag" isDraft .isDraft >/dev/null || status=$?
    ((status == RELEASE_NOT_FOUND)) || fail "The release $tag exists, but its tag does not."
fi

response="$(mktemp)"
trap 'rm -f "$response"' EXIT

case "$STREAM" in
    tool)
        status="$(fetch_nuspec "$TOOL_PACKAGE_ID" "$VERSION" "$response")" ||
            fail "Could not query nuget.org for $TOOL_PACKAGE_ID $VERSION."

        case "$status" in
            200) check_served_nuspec "$TOOL_PACKAGE_ID" "$response" ;;
            404) log "nuget.org does not serve $TOOL_PACKAGE_ID $VERSION yet." ;;
            *) fail "nuget.org answered $status for $TOOL_PACKAGE_ID $VERSION." ;;
        esac
        ;;
    profiles)
        # The profiles need this tool version. A release before it is on nuget.org would break every repository that
        # updates to it.
        minimum="$(<"$(git rev-parse --show-toplevel)/tool/minimum-tool-version")"
        is_valid_version "$minimum" ||
            fail "tool/minimum-tool-version holds $(printf '%q' "$minimum"), which is not a SemVer 2.0.0 version without build metadata."

        id="${TOOL_PACKAGE_ID,,}"
        status="$(fetch "$(flatcontainer_url)/$id/index.json" "$response")" ||
            fail "Could not query nuget.org for the versions of $TOOL_PACKAGE_ID."

        case "$status" in
            200 | 404) ;;
            *) fail "nuget.org answered $status for the versions of $TOOL_PACKAGE_ID." ;;
        esac

        # NuGet normalizes a SemVer 2.0.0 version without build metadata to lowercase.
        if [[ $status == 404 ]] || ! jq --exit-status --arg version "${minimum,,}" '.versions | index($version) != null' \
            "$response" >/dev/null; then
            fail "nuget.org does not list $TOOL_PACKAGE_ID $minimum, the version in tool/minimum-tool-version. Release that tool version first; nuget.org lists a new version a few minutes after the push."
        fi

        log "nuget.org lists $TOOL_PACKAGE_ID $minimum, the minimum tool version of the profiles."
        ;;
esac
