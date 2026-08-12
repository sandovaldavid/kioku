#!/usr/bin/env bash
set -euo pipefail

EVENT_NAME="${KIOKU_EVENT_NAME:-${GITHUB_EVENT_NAME:-}}"
REPOSITORY="${KIOKU_REPOSITORY:-${GITHUB_REPOSITORY:-}}"
PR_BASE_REF="${KIOKU_PR_BASE_REF:-${GITHUB_BASE_REF:-}}"
PR_HEAD_REF="${KIOKU_PR_HEAD_REF:-${GITHUB_HEAD_REF:-}}"
PR_BASE_SHA="${KIOKU_PR_BASE_SHA:-}"
PR_HEAD_SHA="${KIOKU_PR_HEAD_SHA:-}"
PR_HEAD_REPO="${KIOKU_PR_HEAD_REPO:-}"
PR_TITLE="${KIOKU_PR_TITLE:-}"
COMMIT_MSG_HOOK="${KIOKU_COMMIT_MSG_HOOK:-.githooks/commit-msg}"

fail() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

validate_message_file() {
    local file="$1"
    "$COMMIT_MSG_HOOK" "$file"
}

validate_title() {
    local title="$1"
    local message_file
    local status=0

    message_file="$(mktemp)"
    printf '%s\n' "$title" > "$message_file"
    validate_message_file "$message_file" || status=$?
    rm -f "$message_file"
    return "$status"
}

validate_range() {
    local range="$1"
    local message_file
    local status=0
    local -a commits=()

    mapfile -t commits < <(git rev-list --reverse "$range")
    if [[ ${#commits[@]} -eq 0 ]]; then
        printf 'No commits found in range %s; nothing to validate.\n' "$range"
        return 0
    fi

    message_file="$(mktemp)"

    local sha
    for sha in "${commits[@]}"; do
        git log -1 --format=%B "$sha" > "$message_file"
        validate_message_file "$message_file" || {
            status=$?
            break
        }
    done

    rm -f "$message_file"
    return "$status"
}

[[ -n "$EVENT_NAME" ]] || fail "event name is required"
[[ -f "$COMMIT_MSG_HOOK" ]] || fail "commit message hook not found: $COMMIT_MSG_HOOK"

if [[ "$EVENT_NAME" == "pull_request" ]]; then
    [[ -n "$PR_BASE_REF" ]] || fail "pull request base ref is required"
    [[ -n "$PR_HEAD_REF" ]] || fail "pull request head ref is required"
    [[ -n "$PR_HEAD_REPO" ]] || fail "pull request head repository is required"
    [[ -n "$REPOSITORY" ]] || fail "repository identity is required"

    # A release back-sync is the repository's own main branch returning to develop.
    # Its head necessarily contains already-published main history, so re-linting that
    # history is not evidence about the sync PR itself. Validate the explicit sync title
    # instead. Repository identity prevents a fork branch named "main" from bypassing
    # ordinary commit validation.
    if [[ "$PR_BASE_REF" == "develop" && "$PR_HEAD_REF" == "main" && "$PR_HEAD_REPO" == "$REPOSITORY" ]]; then
        [[ -n "$PR_TITLE" ]] || fail "back-sync pull request title is required"
        validate_title "$PR_TITLE"
        printf 'Validated repository back-sync PR title: %s\n' "$PR_TITLE"
        exit 0
    fi

    [[ -n "$PR_BASE_SHA" ]] || fail "pull request base SHA is required"
    [[ -n "$PR_HEAD_SHA" ]] || fail "pull request head SHA is required"
    git cat-file -e "${PR_BASE_SHA}^{commit}" 2>/dev/null || fail "pull request base SHA is not available: $PR_BASE_SHA"
    git cat-file -e "${PR_HEAD_SHA}^{commit}" 2>/dev/null || fail "pull request head SHA is not available: $PR_HEAD_SHA"

    # Use the real PR head SHA instead of GitHub's synthetic merge-ref HEAD.
    validate_range "${PR_BASE_SHA}..${PR_HEAD_SHA}"
    exit 0
fi

# Preserve the existing push/manual behavior: validate the current checked-out commit.
validate_range "HEAD~1..HEAD"
