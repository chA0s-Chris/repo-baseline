#!/usr/bin/env bash
# Writes the release notes to NOTES_FILE.
#
# If the release is already published, its notes are reused: release-drafter would compare against that release and
# find no changes. Otherwise the notes that release-drafter rendered for the release are used. Without a previous
# published release in the stream, release-drafter finds no changes and adds a warning; such notes are refused, and the
# workflow's from input names the starting point instead.
#
# Environment: STREAM, VERSION, NOTES_FILE, DRAFTED_BODY, GH_TOKEN, GH_REPO.
set -euo pipefail
# shellcheck source=lib.sh source-path=SCRIPTDIR
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_env STREAM VERSION NOTES_FILE

tag="$(release_tag)"
status=0
is_draft="$(release_query "$tag" isDraft .isDraft)" || status=$?

if ((status == 0)) && [[ $is_draft == false ]]; then
    log "Reusing the notes of the published release $tag."
    body="$(release_query "$tag" body .body)"
elif ((status == 0 || status == RELEASE_NOT_FOUND)); then
    body="${DRAFTED_BODY-}"
    [[ -n $body ]] || fail "release-drafter rendered no release notes."
    [[ $body != *"could not find a previous **published release**"* ]] ||
        fail "release-drafter found no previous release of $tag's stream to list the changes from. Start the workflow again with the input from, such as the last tag before this stream's first release."
else
    exit "$status"
fi

mkdir -p "$(dirname "$NOTES_FILE")"
printf '%s\n' "$body" >"$NOTES_FILE"
