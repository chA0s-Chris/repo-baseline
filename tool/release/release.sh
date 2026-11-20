#!/usr/bin/env bash
# Tags the dispatched commit and makes sure that a published GitHub release with every asset exists.
#
# Immutable releases are on, so assets cannot be added to a published release. The release is therefore created as a
# draft, gets its assets and is published as the last action. The draft is marked as a pre-release until then, because
# release-drafter updates the first draft of its stream that is not a pre-release, whatever its tag, and would otherwise
# turn a draft left by an interrupted run into its vNext draft. Safe to rerun: an existing tag at the same commit is
# kept, a draft left by an interrupted run is completed, and a published release counts as complete. Uploaded assets
# are never replaced; only an incomplete asset, which GitHub leaves behind when an upload fails upstream, is deleted
# and uploaded again.
#
# A profile release has the assets profiles.tar.gz and profiles.tar.gz.sha256 from ASSET_DIR; a tool release has none.
#
# Environment: STREAM, VERSION, GITHUB_SHA, NOTES_FILE, GH_TOKEN, GH_REPO, and ASSET_DIR for profiles.
set -euo pipefail
# shellcheck source=lib.sh source-path=SCRIPTDIR
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_env STREAM VERSION GITHUB_SHA NOTES_FILE

readonly bot_name='github-actions[bot]'
readonly bot_email='41898282+github-actions[bot]@users.noreply.github.com'

tag="$(release_tag)"
mapfile -t flags < <(release_flags)
[[ -f $NOTES_FILE ]] || fail "The release notes file $NOTES_FILE does not exist."

assets=()
if [[ $STREAM == profiles ]]; then
    require_env ASSET_DIR
    assets=("$ASSET_DIR/profiles.tar.gz" "$ASSET_DIR/profiles.tar.gz.sha256")
    for asset in "${assets[@]}"; do
        [[ -f $asset ]] || fail "The asset $(basename "$asset") is not in $ASSET_DIR."
    done

    # The archive that CI packed must be complete and carry the version of this release.
    (cd "$ASSET_DIR" && sha256sum --check --quiet profiles.tar.gz.sha256) ||
        fail "profiles.tar.gz does not match profiles.tar.gz.sha256."
    packed_version="$(tar -xzOf "$ASSET_DIR/profiles.tar.gz" release.json | jq -r .version)" ||
        fail "Could not read release.json from profiles.tar.gz."
    [[ $packed_version == "$VERSION" ]] ||
        fail "profiles.tar.gz was packed as version '$packed_version' instead of '$VERSION'."
fi

tag_state="$(remote_tag_state)"
status=0
is_draft="$(release_query "$tag" isDraft .isDraft)" || status=$?
if ((status != 0 && status != RELEASE_NOT_FOUND)); then
    exit "$status"
fi

if [[ $tag_state == absent ]]; then
    # This job can see drafts that the read-only prepare job could not see.
    ((status == RELEASE_NOT_FOUND)) || fail "The release $tag exists, but its tag does not."
    git -c user.name="$bot_name" -c user.email="$bot_email" tag --annotate "$tag" --message "Release $tag" "$GITHUB_SHA"
    git push origin "refs/tags/$tag"
else
    log "The tag $tag already exists at $GITHUB_SHA."
fi

if ((status == RELEASE_NOT_FOUND)); then
    gh release create "$tag" --draft --verify-tag --title "$tag" --notes-file "$NOTES_FILE" --prerelease=true \
        --latest=false
    is_draft=true
fi

if [[ $is_draft == false ]]; then
    log "The release $tag is already published."
    exit 0
fi

# Only the state "uploaded" marks a complete asset. An upload that fails upstream can leave an empty asset in another
# state, which GitHub documents as safe to delete.
existing_assets="$(release_query "$tag" assets '.assets[] | [.name, .state] | @tsv')"
declare -A asset_states=()
while IFS=$'\t' read -r name state; do
    [[ -z $name ]] || asset_states[$name]="$state"
done <<<"$existing_assets"

for asset in "${assets[@]}"; do
    name="$(basename "$asset")"

    if [[ -z ${asset_states[$name]+set} ]]; then
        gh release upload "$tag" "$asset"
    elif [[ ${asset_states[$name]} == uploaded ]]; then
        log "The release $tag already has $name."
    else
        log "The release $tag has the incomplete asset $name in state '${asset_states[$name]}'. Uploading it again."
        gh release delete-asset "$tag" "$name" --yes
        gh release upload "$tag" "$asset"
    fi
done

gh release edit "$tag" --draft=false "${flags[@]}"
log "Published the release $tag."
