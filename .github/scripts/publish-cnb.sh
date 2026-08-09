#!/usr/bin/env bash
set -euo pipefail

CNB_API="${CNB_API:-https://api.cnb.cool}"
CNB_WEB="${CNB_WEB:-https://cnb.cool}"
CNB_ACCEPT="application/vnd.cnb.api+json"
CNB_REPO="${CNB_REPO:-tiouo/portal}"
CNB_TAG="${CNB_TAG:?CNB_TAG is required}"
CNB_GIT_URL="${CNB_GIT_URL:-${CNB_WEB}/${CNB_REPO}.git}"

if [ -z "${CNB_TOKEN:-}" ]; then
  echo "[cnb] CNB_TOKEN 未配置，跳过 CNB 同步。"
  exit 0
fi
if [ -z "${CNB_FILES:-}" ]; then
  echo "[cnb] CNB_FILES 未配置，跳过 CNB 同步。"
  exit 0
fi

CNB_NAME="${CNB_NAME:-$CNB_TAG}"
CNB_PRERELEASE="${CNB_PRERELEASE:-false}"
CNB_MAKE_LATEST="${CNB_MAKE_LATEST:-false}"

cnb_curl() {
  local method="$1" path="$2" body="${3:-}" url
  local args=(-g -sS -X "$method" -H "Authorization: Bearer ${CNB_TOKEN}" -H "Accept: ${CNB_ACCEPT}")
  if [ -n "$body" ]; then
    args+=(-H "Content-Type: application/json" --data "$body")
  fi
  case "$path" in
    http://*|https://*) url="$path" ;;
    *) url="${CNB_API}${path}" ;;
  esac
  curl "${args[@]}" "$url"
}

get_release_id() {
  local json code
  json=$(cnb_curl GET "/${CNB_REPO}/-/releases/tags/${CNB_TAG}" 2>/dev/null || true)
  code=$(jq -r '.id // empty' <<<"${json}" 2>/dev/null || true)
  echo "${code}"
}

ensure_cnb_tag() {
  local commit auth
  commit="${CNB_COMMIT:-$(git rev-parse HEAD)}"
  if [ -n "$commit" ]; then
    git tag -f "${CNB_TAG}" "${commit}" >/dev/null 2>&1 || true
  fi
  auth="$(printf '%s:%s' 'cnb' "${CNB_TOKEN}" | base64 | tr -d '\n')"
  git -c "http.extraHeader=Authorization: Basic ${auth}" \
    push --force "${CNB_GIT_URL}" "refs/tags/${CNB_TAG}" >/dev/null 2>&1 || \
    echo "[cnb] 标签推送失败（非致命，忽略）。"
}

release_meta_json() {
  local body="" pre
  if [ -n "${CNB_BODY_FILE:-}" ] && [ -f "${CNB_BODY_FILE}" ]; then
    body="$(cat "${CNB_BODY_FILE}")"
  fi
  if [ "${CNB_PRERELEASE}" = "true" ]; then pre="true"; else pre="false"; fi
  jq -n \
    --arg tag "${CNB_TAG}" \
    --arg name "${CNB_NAME}" \
    --arg body "${body}" \
    --argjson pre "${pre}" \
    --arg latest "${CNB_MAKE_LATEST}" \
    '{ tag_name: $tag, name: $name, body: $body, prerelease: $pre, make_latest: $latest }'
}

make_release() {
  local meta response id
  meta="$(release_meta_json)"
  if [ -n "${CNB_COMMIT:-}" ]; then
    meta="$(jq --arg target "${CNB_COMMIT}" '.target_commitish = $target' <<<"${meta}")"
  fi
  response="$(cnb_curl POST "/${CNB_REPO}/-/releases" "${meta}")"
  id="$(jq -r '.id // empty' <<<"${response}" 2>/dev/null || true)"
  if [ -z "${id}" ]; then
    echo "[cnb] 创建 Release 失败：${response}" >&2
    return 1
  fi
  echo "${id}"
}

remove_release() {
  local id="$1"
  cnb_curl DELETE "/${CNB_REPO}/-/releases/${id}" >/dev/null 2>&1 || true
  echo "[cnb] 已删除旧 Release：${id}"
}

cleanup_assets() {
  local release_id="$1" json id name
  json="$(cnb_curl GET "/${CNB_REPO}/-/releases/${release_id}")"
  while read -r id name; do
    [ -z "${id}" ] && continue
    cnb_curl DELETE "/${CNB_REPO}/-/releases/${release_id}/assets/${id}" >/dev/null || true
    echo "[cnb] 已清理旧附件：${name}"
  done < <(jq -r '.assets[]? | "\(.id) \(.name)"' <<<"${json}" 2>/dev/null || true)
}

upload_asset() {
  local file="$1" release_id="$2" name size json upload_url verify_url
  [ -f "${file}" ] || { echo "[cnb] 文件不存在：${file}" >&2; return 1; }
  name="$(basename "${file}")"
  size="$(stat -c%s "${file}" 2>/dev/null || stat -f%z "${file}")"
  json="$(jq -n --arg n "${name}" --argjson s "${size}" '{ asset_name: $n, size: $s, overwrite: true }')"
  json="$(cnb_curl POST "/${CNB_REPO}/-/releases/${release_id}/asset-upload-url" "${json}")"
  upload_url="$(jq -r '.upload_url // empty' <<<"${json}" 2>/dev/null || true)"
  verify_url="$(jq -r '.verify_url // empty' <<<"${json}" 2>/dev/null || true)"
  if [ -z "${upload_url}" ] || [ -z "${verify_url}" ]; then
    echo "[cnb] 获取上传地址失败：${json}" >&2
    return 1
  fi
  curl -g -sS -X PUT --data-binary "@${file}" "${upload_url}" >/dev/null
  cnb_curl POST "${verify_url}" "{}" >/dev/null
  echo "[cnb] 已上传附件：${name}（${size} 字节）"
}

main() {
  local release_id
  echo "[cnb] 同步 Release ${CNB_TAG} -> ${CNB_REPO}"
  ensure_cnb_tag
  release_id="$(get_release_id)"
  if [ -n "${release_id}" ]; then
    remove_release "${release_id}"
  fi
  release_id="$(make_release)"
  cleanup_assets "${release_id}"
  shopt -s nullglob
  for file in ${CNB_FILES}; do
    upload_asset "${file}" "${release_id}"
  done
  echo "[cnb] CNB 同步完成：${CNB_TAG}"
}

main