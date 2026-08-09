#!/usr/bin/env bash
set -euo pipefail

VERSION="$1"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# src/version.txt
printf '%s\n' "$VERSION" > "$ROOT/src/version.txt"

# src/Portal/Portal.csproj
sed -i -E "s#<PackageVersion>[^<]*</PackageVersion>#<PackageVersion>$VERSION</PackageVersion>#" "$ROOT/src/Portal/Portal.csproj"

# web/package.json
sed -i -E "s#(\"version\"): \"[^\"]*\"#\1: \"$VERSION\"#" "$ROOT/web/package.json"

# web/package-lock.json （仅根节点）
sed -i -E "0,/(\"version\": \"[^\"]*\")/s//\"version\": \"$VERSION\"/" "$ROOT/web/package-lock.json"

# packaging/aur
for f in "$ROOT"/packaging/aur/*.PKGBUILD; do
  sed -i -E "s#^pkgver=.*#pkgver=$VERSION#" "$f"
done

echo "$VERSION"