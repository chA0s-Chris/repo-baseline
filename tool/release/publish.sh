#!/usr/bin/env bash
# Pushes the tool package that CI packed with the release version to nuget.org. Safe to rerun: a package that is
# already there counts as pushed, and verify-published.sh then checks that it belongs to the dispatched commit.
#
# Environment: VERSION, NUGET_KEY, PACKAGE_DIR, optionally NUGET_SOURCE.
set -euo pipefail
# shellcheck source=lib.sh source-path=SCRIPTDIR
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_env VERSION NUGET_KEY PACKAGE_DIR

package="$PACKAGE_DIR/$TOOL_PACKAGE_ID.$VERSION.nupkg"
[[ -f $package ]] || fail "The package $TOOL_PACKAGE_ID.$VERSION.nupkg is not in $PACKAGE_DIR."

dotnet nuget push "$package" --api-key "$NUGET_KEY" --source "${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}" \
    --skip-duplicate
