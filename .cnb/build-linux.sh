#!/usr/bin/env bash
# CNB Linux 构建脚本：生成 version-ci.txt 并输出 AppImage/deb/rpm。
# 由 .cnb.yml 的 web_trigger_build_linux 流水线调用。
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

BASE_VERSION=$(cat ./src/version.txt | tr -d '[:space:]')
Y_FULL=$(TZ='Asia/Shanghai' date +'%Y')
MD=$(TZ='Asia/Shanghai' date +'%m%d')
HM=$(TZ='Asia/Shanghai' date +'%H%M')
FULL_TIMESTAMP="${Y_FULL}${MD}${HM}"
START_EPOCH=$(TZ='Asia/Shanghai' date -d "2026-07-10 00:00:00" +%s 2>/dev/null || true)
NOW_EPOCH=$(date +%s)
if [ -z "$START_EPOCH" ]; then START_EPOCH="$NOW_EPOCH"; fi
TOTAL_MINUTES=$(( (NOW_EPOCH - START_EPOCH) / 60 ))
TOTAL_HOURS=$(( TOTAL_MINUTES / 60 ))
ELAPSED_MINUTES=$(( TOTAL_MINUTES % 60 ))
APP_VERSION="0.0.${TOTAL_HOURS}.${ELAPSED_MINUTES}"
COMMIT_HASH=$(git rev-parse --short=6 HEAD)
BUILD_TIME=$(TZ='Asia/Shanghai' date -Iseconds)
PID="${CNB_PIPELINE_ID:-${GITHUB_RUN_ID:-0}}"

# 依据目标标签生成与主构建链一致的渠道与标题，保证该包也能用于更新检查
case "${TAG:-publish-nightly}" in
  publish-commit) CHANNEL="commit";;
  publish-nightly) CHANNEL="nightly";;
  *) CHANNEL="nightly";;
esac
VERSION_TITLE="build-${CHANNEL}-${APP_VERSION}-${PID}-${COMMIT_HASH}"
JSON_STRING="{\"type\":\"${CHANNEL}\",\"build-time\":\"${BUILD_TIME}\",\"action\":\"${PID}\",\"commit\":\"${COMMIT_HASH}\",\"version\":\"${BASE_VERSION}\",\"timestamp\":\"${FULL_TIMESTAMP}\",\"version_title\":\"${VERSION_TITLE}\"}"

echo "$JSON_STRING" > ./src/version-ci.txt

mkdir -p ./cnb-dist

dotnet tool install -g KuiperZone.PupNet --version 1.8.0
sudo apt-get update && sudo apt-get install -y fuse rpm

for kind in appimage deb rpm; do
  case "$kind" in
    appimage) output="Portal.linux.x64.AppImage";;
    deb) output="Portal.linux.x64.deb";;
    rpm) output="Portal.linux.x64.rpm";;
  esac
  cd "$ROOT/src/Portal.Desktop"
  pupnet --runtime linux-x64 --kind "$kind" -o "$output" -y --app-version "$APP_VERSION"
  cd "$ROOT"
  cp "./src/Portal.Desktop/Deploy/OUT/$output" ./cnb-dist/
done

ls -lh ./cnb-dist/