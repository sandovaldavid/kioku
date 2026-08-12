#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VALIDATOR="$ROOT/scripts/validate-conventional-commits.sh"
HOOK="$ROOT/.githooks/commit-msg"
TMP_DIR="$(mktemp -d)"
REPO="$TMP_DIR/repo"

cleanup() {
    rm -rf "$TMP_DIR"
}
trap cleanup EXIT

fail() {
    printf '[fail] %s\n' "$*" >&2
    exit 1
}

expect_success() {
    local name="$1"
    shift
    if "$@"; then
        printf '[ok] %s\n' "$name"
    else
        fail "$name"
    fi
}

expect_failure() {
    local name="$1"
    shift
    if "$@"; then
        fail "$name unexpectedly succeeded"
    else
        printf '[ok] %s\n' "$name"
    fi
}

run_pr_validation() {
    local base_ref="$1"
    local head_ref="$2"
    local base_sha="$3"
    local head_sha="$4"
    local head_repo="$5"
    local title="$6"

    (
        cd "$REPO"
        KIOKU_EVENT_NAME=pull_request \
        KIOKU_REPOSITORY=sandovaldavid/kioku \
        KIOKU_PR_BASE_REF="$base_ref" \
        KIOKU_PR_HEAD_REF="$head_ref" \
        KIOKU_PR_BASE_SHA="$base_sha" \
        KIOKU_PR_HEAD_SHA="$head_sha" \
        KIOKU_PR_HEAD_REPO="$head_repo" \
        KIOKU_PR_TITLE="$title" \
        KIOKU_COMMIT_MSG_HOOK="$HOOK" \
        bash "$VALIDATOR"
    )
}

mkdir -p "$REPO"
git -C "$REPO" init -q -b develop
git -C "$REPO" config user.name "Kioku CI"
git -C "$REPO" config user.email "kioku-ci@example.invalid"

printf 'base\n' > "$REPO/fixture.txt"
git -C "$REPO" add fixture.txt
git -C "$REPO" commit -q -m "chore(ci): seed validation fixture"
base_sha="$(git -C "$REPO" rev-parse HEAD)"

git -C "$REPO" switch -q -c feature
printf 'valid\n' >> "$REPO/fixture.txt"
git -C "$REPO" commit -q -am "fix(ci): validate real pull request head"
valid_head_sha="$(git -C "$REPO" rev-parse HEAD)"

# Simulate GitHub checking out a synthetic/current HEAD that is not the real PR head.
git -C "$REPO" switch -q -c synthetic "$base_sha"
printf 'invalid synthetic head\n' >> "$REPO/fixture.txt"
git -C "$REPO" commit -q -am "noop"
invalid_head_sha="$(git -C "$REPO" rev-parse HEAD)"

expect_success \
    "ordinary PR validates explicit head SHA instead of current HEAD" \
    run_pr_validation develop feature "$base_sha" "$valid_head_sha" sandovaldavid/kioku "fix(ci): validate real pull request head"

expect_failure \
    "ordinary PR rejects an introduced non-conventional commit" \
    run_pr_validation develop feature "$base_sha" "$invalid_head_sha" sandovaldavid/kioku "fix(ci): invalid fixture"

expect_success \
    "same-repository main to develop back-sync validates conventional PR title" \
    run_pr_validation develop main "" "" sandovaldavid/kioku "chore(develop): sync main after release"

expect_failure \
    "same-repository back-sync rejects a non-conventional PR title" \
    run_pr_validation develop main "" "" sandovaldavid/kioku "sync main"

expect_failure \
    "fork branch named main cannot use the back-sync exception" \
    run_pr_validation develop main "$base_sha" "$invalid_head_sha" example-fork/kioku "chore(develop): sync main after release"

message_file="$TMP_DIR/commit-message.txt"
printf 'Merge branch '\''release'\''\n' > "$message_file"
expect_success "existing merge commit compatibility remains accepted" bash "$HOOK" "$message_file"

printf 'chore(release): 3.1.2\n' > "$message_file"
expect_success "existing release automation compatibility remains accepted" bash "$HOOK" "$message_file"

printf '[ok] conventional commit validation contract\n'
