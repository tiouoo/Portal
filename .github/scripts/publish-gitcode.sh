#!/usr/bin/env bash
set -euo pipefail

GITCODE_API="${GITCODE_API:-https://gitcode.com/api/v5}"
GITCODE_WEB="${GITCODE_WEB:-https://gitcode.com}"
GITCODE_REPO="${GITCODE_REPO:-tiouo/Portal}"
GITCODE_TAG="${GITCODE_TAG:?GITCODE_TAG is required}"
GITCODE_GIT_URL="${GITCODE_GIT_URL:-${GITCODE_WEB}/${GITCODE_REPO}.git}"

if [ -z "${GITCODE_TOKEN:-}" ]; then
  echo "[gitcode] GITCODE_TOKEN 未配置，跳过 GitCode 同步。"
  exit 0
fi

GITCODE_NAME="${GITCODE_NAME:-$GITCODE_TAG}"
GITCODE_PRERELEASE="${GITCODE_PRERELEASE:-false}"

gitcode_releases() {
  echo "${GITCODE_API}/repos/${GITCODE_REPO}/releases"
}

ensure_gitcode_tag() {
  local commit auth
  commit="${GITCODE_COMMIT:-$(git rev-parse HEAD)}"
  if [ -n "$commit" ]; then
    git tag -f "${GITCODE_TAG}" "${commit}" >/dev/null 2>&1 || true
  fi
  auth="$(printf '%s:%s' "${GITCODE_GIT_USER:-oauth2}" "${GITCODE_TOKEN}" | base64 | tr -d '\n')"
  git -c "http.extraHeader=Authorization: Basic ${auth}" \
    push --force "${GITCODE_GIT_URL}" "refs/tags/${GITCODE_TAG}" >/dev/null 2>&1 || \
    echo "[gitcode] 标签推送失败（非致命，忽略）。"
}

get_release_id() {
  local json id
  json="$(curl -g -sS -X GET -H "Authorization: Bearer ${GITCODE_TOKEN}" \
    "$(gitcode_releases)/tags/${GITCODE_TAG}" 2>/dev/null || true)"
  id="$(jq -r '.id // empty' <<<"${json}" 2>/dev/null || true)"
  echo "${id}"
}

make_release() {
  local response id
  local -a args=(
    -g -sS -X POST
    -H "Authorization: Bearer ${GITCODE_TOKEN}"
    -H "Content-Type: application/x-www-form-urlencoded"
    --data-urlencode "access_token=${GITCODE_TOKEN}"
    --data-urlencode "tag_name=${GITCODE_TAG}"
    --data-urlencode "name=${GITCODE_NAME}"
    --data-urlencode "prerelease=${GITCODE_PRERELEASE}"
  )
  if [ -n "${GITCODE_BODY_FILE:-}" ] && [ -f "${GITCODE_BODY_FILE}" ]; then
    args+=(--data-urlencode "body@${GITCODE_BODY_FILE}")
  fi
  if [ -n "${GITCODE_COMMIT:-}" ]; then
    args+=(--data-urlencode "target_commitish=${GITCODE_COMMIT}")
  fi
  response="$(curl "${args[@]}" "$(gitcode_releases)")"
  id="$(jq -r '.id // empty' <<<"${response}" 2>/dev/null || true)"
  if [ -z "${id}" ]; then
    echo "[gitcode] 创建 Release 失败：${response}" >&2
    return 1
  fi
  echo "${id}"
}

remove_release() {
  local id="$1"
  curl -g -sS -X DELETE -H "Authorization: Bearer ${GITCODE_TOKEN}" \
    --data-urlencode "access_token=${GITCODE_TOKEN}" \
    "$(gitcode_releases)/${id}" >/dev/null 2>&1 || true
  echo "[gitcode] 已删除旧 Release：${id}"
}

main() {
  local release_id
  echo "[gitcode] 同步 Release ${GITCODE_TAG} -> ${GITCODE_REPO}"
  ensure_gitcode_tag
  release_id="$(get_release_id)"
  if [ -n "${release_id}" ]; then
    remove_release "${release_id}"
  fi
  release_id="$(make_release)"
  echo "[gitcode] GitCode 同步完成：${GITCODE_TAG}"
}

main