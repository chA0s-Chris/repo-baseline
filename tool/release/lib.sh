# shellcheck shell=bash
# Shared helpers of the release scripts. Source this file; it runs nothing by itself.
#
# Both release workflows use these scripts. STREAM selects the release stream:
#   tool      the .NET tool Chaos.RepoBaseline on nuget.org, tagged tool/v<version>
#   profiles  the profile archive as GitHub release assets, tagged v<version>
#
# Every value that comes from outside the repository (workflow inputs, release notes, API responses) reaches the
# scripts through environment variables or files and is only ever expanded in double quotes.

# SemVer 2.0.0 without build metadata. nuget.org ignores build metadata for package identity, so 1.0.0+a and 1.0.0+b
# would collide, and the tag would not show it. Without it, "contains -" is a correct pre-release test.
readonly RELEASE_VERSION_PATTERN='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-((0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(\.(0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?$'

# The exit status of release_query for a release that does not exist.
# shellcheck disable=SC2034 # Used by the scripts that source this file.
readonly RELEASE_NOT_FOUND=10

# The package ID of the tool, as in src/RepoBaseline/RepoBaseline.csproj.
# shellcheck disable=SC2034 # Used by the scripts that source this file.
readonly TOOL_PACKAGE_ID=Chaos.RepoBaseline

log() {
    printf '%s\n' "$*" >&2
}

fail() {
    printf 'Error: %s\n' "$*" >&2
    exit 1
}

require_env() {
    local name
    for name in "$@"; do
        [[ -n ${!name:-} ]] || fail "The environment variable $name is not set."
    done
}

# Matches the entire value, so a multi-line value whose first line is valid is rejected.
is_valid_version() {
    [[ $1 =~ $RELEASE_VERSION_PATTERN ]]
}

# Only versions with a pre-release suffix are pre-releases; 0.x versions without one are normal releases.
is_prerelease() {
    # shellcheck disable=SC2153 # VERSION comes from the environment.
    [[ $VERSION == *-* ]]
}

# Prints the tag of $VERSION in the stream $STREAM.
release_tag() {
    case "${STREAM:-}" in
        tool) printf 'tool/v%s\n' "$VERSION" ;;
        profiles) printf 'v%s\n' "$VERSION" ;;
        *) fail "The stream $(printf '%q' "${STREAM:-}") is neither 'tool' nor 'profiles'." ;;
    esac
}

# Prints the flags of gh release create and edit. Pre-releases and tool releases are never the latest release, so that
# "Latest" always points to a stable profile release.
release_flags() {
    local prerelease=false latest=false

    if is_prerelease; then
        prerelease=true
    elif [[ $STREAM == profiles ]]; then
        latest=true
    fi

    printf '%s\n' "--prerelease=$prerelease" "--latest=$latest"
}

flatcontainer_url() {
    printf '%s\n' "${NUGET_FLATCONTAINER_URL:-https://api.nuget.org/v3-flatcontainer}"
}

# Downloads URL $1 to file $2 and prints the HTTP status. curl fails only when it gets no answer at all.
fetch() {
    curl --silent --show-error --location --retry 3 --output "$2" --write-out '%{http_code}' "$1"
}

# Downloads the nuspec that nuget.org serves for package $1 in version $2 to file $3 and prints the HTTP status.
# The flat container uses lowercase IDs and versions; a 404 only means that it does not serve the version (yet).
fetch_nuspec() {
    local id="${1,,}" version="${2,,}"
    fetch "$(flatcontainer_url)/$id/$version/$id.nuspec" "$3"
}

nuspec_version() {
    sed -n 's:.*<version>\([^<]*\)</version>.*:\1:p' "$1" | head -n 1
}

# The commit that the package was built from. The project sets PublishRepositoryUrl, so that the package records it.
nuspec_commit() {
    sed -n 's:.*<repository [^>]*commit="\([^"]*\)".*:\1:p' "$1" | head -n 1
}

# Fails unless the served nuspec $2 of package $1 has exactly the requested version and the dispatched commit.
check_served_nuspec() {
    local id="$1" nuspec="$2" served_version served_commit

    served_version="$(nuspec_version "$nuspec")"
    served_commit="$(nuspec_commit "$nuspec")"

    [[ $served_version == "$VERSION" ]] ||
        fail "nuget.org serves $id as version '$served_version' instead of '$VERSION'."
    [[ $served_commit == "$GITHUB_SHA" ]] ||
        fail "nuget.org serves $id $VERSION from commit '$served_commit' instead of '$GITHUB_SHA'."

    log "nuget.org serves $id $VERSION from commit $GITHUB_SHA."
}

# Prints "present" for an annotated release tag on origin at $GITHUB_SHA, or "absent" if no tag matches.
# Fails if a tag differs only in case, because NuGet versions are case-insensitive, or points to another commit.
remote_tag_state() {
    local tag refs sha ref name direct="" peeled=""

    tag="$(release_tag)"
    refs="$(git ls-remote --tags origin)" || fail "Could not list the tags of origin."

    while IFS=$'\t' read -r sha ref; do
        [[ -n $ref ]] || continue
        name="${ref#refs/tags/}"

        if [[ $name == "$tag" ]]; then
            direct="$sha"
        elif [[ $name == "$tag^{}" ]]; then
            peeled="$sha"
        else
            name="${name%'^{}'}"
            [[ ${name,,} != "${tag,,}" ]] || fail "The tag '$name' differs from '$tag' only in case."
        fi
    done <<<"$refs"

    if [[ -z $direct ]]; then
        printf 'absent\n'
        return
    fi

    [[ -n $peeled ]] || fail "The tag '$tag' is lightweight. An annotated release tag is required."
    [[ $peeled == "$GITHUB_SHA" ]] ||
        fail "The tag '$tag' points to commit '$peeled' instead of '$GITHUB_SHA'."

    printf 'present\n'
}

# Prints field $2 of the GitHub release with tag $1, extracted with the jq expression $3. Returns RELEASE_NOT_FOUND if
# the release does not exist and fails on any other error. gh finds drafts only if the token may write the repository.
release_query() {
    local tag="$1" field="$2" expression="$3" output errors status=0

    errors="$(mktemp)"
    output="$(gh release view "$tag" --json "$field" --jq "$expression" 2>"$errors")" || status=$?

    if ((status != 0)); then
        if grep -qi 'release not found' "$errors"; then
            rm -f "$errors"
            return "$RELEASE_NOT_FOUND"
        fi

        cat "$errors" >&2
        rm -f "$errors"
        fail "Could not read the release '$tag'."
    fi

    rm -f "$errors"
    printf '%s\n' "$output"
}
