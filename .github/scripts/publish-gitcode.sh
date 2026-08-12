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

# GitCode Release API 按 tag 操作，返回对象不含 id，
# 因此创建/更新/存在性判断统一以响应中的 tag_name 为准。
release_exists() {
  local json
  json="$(curl -g -sS -X GET -H "Authorization: Bearer ${GITCODE_TOKEN}" \
    "$(gitcode_releases)/tags/${GITCODE_TAG}" 2>/dev/null || true)"
  jq -e --arg t "${GITCODE_TAG}" '.tag_name == $t' >/dev/null 2>&1 <<<"${json}"
}

gitcode_git_auth() {
  printf '%s:%s' "${GITCODE_GIT_USER:-oauth2}" "${GITCODE_TOKEN}" | base64 | tr -d '\n'
}

# 部分节点/重定向会丢弃 http.extraHeader，此时改推带凭据的 URL 重试。
gitcode_git_push() {
  local host
  host="$(printf '%s' "${GITCODE_GIT_URL}" | sed -E 's#^[a-z]+://([^/]+)/.*#\1#')"
  GIT_TERMINAL_PROMPT=0 git -c "http.extraHeader=Authorization: Basic $(gitcode_git_auth)" \
    push --force "${GITCODE_GIT_URL}" "refs/tags/${GITCODE_TAG}" 2>&1 || {
    GIT_TERMINAL_PROMPT=0 git \
      push --force "https://${GITCODE_GIT_USER:-oauth2}:${GITCODE_TOKEN}@${host}/${GITCODE_REPO}.git" \
      "refs/tags/${GITCODE_TAG}" 2>&1
  }
}

# 标签是 Release 的载体：推不上 GitCode，就没有版本号可用，必须致命终止。
ensure_gitcode_tag() {
  local commit push_out
  commit="${GITCODE_COMMIT:-$(git rev-parse HEAD)}"
  if [ -n "$commit" ]; then
    git tag -f "${GITCODE_TAG}" "${commit}" >/dev/null 2>&1 || true
  fi
  push_out="$(gitcode_git_push)" || {
    push_out="$(printf '%s' "${push_out}" | sed "s#${GITCODE_TOKEN}#***#g")"
    printf '[gitcode] 标签推送失败：\n%s\n' "${push_out}" >&2
    echo "[gitcode] 终止：标签 ${GITCODE_TAG} 未同步到 ${GITCODE_REPO}，无法创建 Release。" >&2
    return 1
  }
  echo "[gitcode] 标签已推送：${GITCODE_TAG} -> ${commit}"
}

create_release() {
  local response
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
  if jq -e --arg t "${GITCODE_TAG}" '.tag_name == $t' >/dev/null 2>&1 <<<"${response}"; then
    echo "[gitcode] 已创建 Release：${GITCODE_TAG}"
  else
    echo "[gitcode] 创建 Release 失败：${response}" >&2
    return 1
  fi
}

update_release() {
  local response
  local -a args=(
    -g -sS -X PATCH
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
  response="$(curl "${args[@]}" "$(gitcode_releases)/${GITCODE_TAG}")"
  if jq -e --arg t "${GITCODE_TAG}" '.tag_name == $t' >/dev/null 2>&1 <<<"${response}"; then
    echo "[gitcode] 已更新 Release：${GITCODE_TAG}"
  else
    echo "[gitcode] 更新 Release 失败：${response}" >&2
    return 1
  fi
}

main() {
  echo "[gitcode] 同步 Release ${GITCODE_TAG} -> ${GITCODE_REPO}"
  ensure_gitcode_tag
  if release_exists; then
    update_release
  else
    create_release
  fi
  echo "[gitcode] GitCode 同步完成：${GITCODE_TAG}"
}

main