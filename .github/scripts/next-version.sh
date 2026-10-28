#!/usr/bin/env bash
set -euo pipefail

range="${1:-}"
if [ -n "$range" ]; then
  messages=$(git log --format=%B "$range")
else
  messages=$(git log -1 --format=%B)
fi

latest=$(git tag -l 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sed 's/^v//' | sort -V | tail -n1 || true)

explicit=$(grep -oE '\[v?[0-9]+\.[0-9]+\.[0-9]+\]' <<<"$messages" | tail -n1 | tr -d '[]v' || true)

if [ -n "$explicit" ]; then
  if [ -n "$latest" ] && [ "$(printf '%s\n%s\n' "$latest" "$explicit" | sort -V | tail -n1)" != "$explicit" -o "$latest" = "$explicit" ]; then
    echo "Requested version $explicit is not greater than the latest tag v$latest" >&2
    exit 1
  fi
  echo "$explicit"
  exit 0
fi

if [ -z "$latest" ]; then
  echo "1.0.0"
  exit 0
fi

IFS=. read -r major minor patch <<<"$latest"
if grep -qi '\[major\]' <<<"$messages"; then
  echo "$((major + 1)).0.0"
elif grep -qi '\[minor\]' <<<"$messages"; then
  echo "$major.$((minor + 1)).0"
else
  echo "$major.$minor.$((patch + 1))"
fi
