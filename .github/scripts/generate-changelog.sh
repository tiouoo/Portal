#!/usr/bin/env bash
set -euo pipefail

REPO_URL="${1:-https://github.com/tiouoo/Portal}"
GITHUB_SHA="${GITHUB_SHA:-}"

STABLE_TAG=$(git tag -l --sort=-v:refname 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | head -n1 || true)

if [ -n "$STABLE_TAG" ]; then
  RANGE="${STABLE_TAG}..HEAD"
  echo "相对 \`${STABLE_TAG}\` 的更改："
  echo ""
else
  RANGE="-20"
  echo "最近提交："
  echo ""
fi

git log --no-merges --pretty=tformat:"- %s ([%h](${REPO_URL}/commit/%H))" "$RANGE"

if [ -n "$STABLE_TAG" ] && [ -n "$GITHUB_SHA" ]; then
  echo ""
  echo "**完整对比**: ${REPO_URL}/compare/${STABLE_TAG}...${GITHUB_SHA}"
fi