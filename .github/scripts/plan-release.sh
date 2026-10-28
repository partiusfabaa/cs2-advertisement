#!/usr/bin/env bash
set -euo pipefail

before="${1:-}"
if [ -n "$before" ]; then
  range="$before..HEAD"
  changed=$(git diff --name-only "$before" HEAD)
  messages=$(git log --format=%B "$range")
else
  range=""
  changed=$(git diff-tree --root --no-commit-id --name-only -r HEAD)
  messages=$(git log -1 --format=%B)
fi

latest=$(git tag -l 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sed 's/^v//' | sort -V | tail -n1 || true)

release=false
if [ -z "$latest" ] || [ "${FORCE_FULL:-}" = true ]; then
  release=true
elif grep -qE '^Advertisement/' <<<"$changed"; then
  release=true
elif grep -qiE '\[(major|minor)\]|\[v?[0-9]+\.[0-9]+\.[0-9]+\]' <<<"$messages"; then
  release=true
fi

if [ "$release" != true ]; then
  echo "mode=none"
  echo "version="
  echo "tag="
  exit 0
fi

version=$(bash "$(dirname "$0")/next-version.sh" $range)
echo "mode=release"
echo "version=$version"
echo "tag=v$version"
