#!/usr/bin/env bash
# Waits until nuget.org serves the tool package of the version and fails unless it has the exact version and the
# dispatched commit. nuget.org accepts a push before it validates and indexes the package, and a push of a version that
# already exists only counts as a duplicate, so only this check proves that the package belongs to the commit that is
# about to be tagged.
#
# Environment: VERSION, GITHUB_SHA, optionally NUGET_FLATCONTAINER_URL, NUGET_POLL_ATTEMPTS (default 60) and
# NUGET_POLL_INTERVAL in seconds (default 30).
set -euo pipefail
# shellcheck source=lib.sh source-path=SCRIPTDIR
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_env VERSION GITHUB_SHA

attempts="${NUGET_POLL_ATTEMPTS:-60}"
interval="${NUGET_POLL_INTERVAL:-30}"
nuspec="$(mktemp)"
trap 'rm -f "$nuspec"' EXIT

for ((attempt = 1; ; attempt++)); do
    status="$(fetch_nuspec "$TOOL_PACKAGE_ID" "$VERSION" "$nuspec")" || status=error

    if [[ $status == 200 ]]; then
        check_served_nuspec "$TOOL_PACKAGE_ID" "$nuspec"
        break
    fi

    log "nuget.org does not serve $TOOL_PACKAGE_ID $VERSION yet ($status)."
    ((attempt < attempts)) ||
        fail "nuget.org still does not serve $TOOL_PACKAGE_ID $VERSION. Re-run the failed jobs to keep waiting."

    sleep "$interval"
done
