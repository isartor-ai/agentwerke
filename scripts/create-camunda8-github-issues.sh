#!/usr/bin/env sh
set -eu

repo="${1:-isartor-ai/autofac-private}"
issue_dir="${2:-docs/github-issues/camunda8-sdlc-factory}"

if [ -n "${GH_TOKEN:-}" ]; then
  token="$GH_TOKEN"
elif [ -n "${GITHUB_TOKEN:-}" ]; then
  token="$GITHUB_TOKEN"
else
  echo "GH_TOKEN or GITHUB_TOKEN is required to create GitHub issues." >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to build GitHub API payloads." >&2
  exit 1
fi

existing_issues="$(mktemp)"
trap 'rm -f "$existing_issues"' EXIT

curl -fsS \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer $token" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  "https://api.github.com/repos/$repo/issues?state=all&per_page=100" \
  > "$existing_issues"

for file in "$issue_dir"/[0-9][0-9][0-9]-*.md; do
  title="$(sed -n '1s/^# //p' "$file")"
  if [ -z "$title" ]; then
    echo "Skipping $file because it has no markdown H1 title." >&2
    continue
  fi

  existing_url="$(jq -r --arg title "$title" '.[] | select(.pull_request | not) | select(.title == $title) | .html_url' "$existing_issues" | sed -n '1p')"
  if [ -n "$existing_url" ]; then
    echo "Skipping existing issue: $title"
    echo "$existing_url"
    continue
  fi

  payload="$(jq -n --rawfile body "$file" --arg title "$title" '{ title: $title, body: $body }')"

  echo "Creating issue: $title"
  curl -fsS \
    -X POST \
    -H "Accept: application/vnd.github+json" \
    -H "Authorization: Bearer $token" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "https://api.github.com/repos/$repo/issues" \
    -d "$payload" \
    | jq -r '.html_url'
done
