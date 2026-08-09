#!/usr/bin/env bash
# 将 GitHub Release 构建产物同步到 CNB Release 附件。
# 使用 CNB 官方 OpenAPI（https://api.cnb.cool），参考 https://api.cnb.cool 文档。
#
# 必需环境变量：
#   CNB_TOKEN  CNB 访问令牌（个人设置 -> 访问令牌，需 repo-code:rw / repo-release:rw）
#   CNB_REPO   CNB 仓库完整路径，如 tiouo/portal
#   CNB_TAG    目标 Release 标签，如 v1.0.0 / publish-nightly / publish-commit
# 可选环境变量：
#   CNB_API          API 地址，默认 https://api.cnb.cool
#   CNB_WEB          Web 地址，默认 https://cnb.cool
#   CNB_GIT_URL      CNB 仓库 git 地址，默认 ${CNB_WEB}/${CNB_REPO}.git
#   CNB_NAME         Release 标题（默认取标签名）
#   CNB_BODY_FILE    Release 描述文件路径（可选）
#   CNB_COMMIT       target_commitish（提交哈希，可选）
#   CNB_PRERELEASE  是否预发布，true/false，默认 false
#   CNB_MAKE_LATEST  是否标记为最新，true/false/legacy，默认 false
#   CNB_FILES        需要上传的附件（空格分隔的 glob），必需
set -euo pipefail

CNB_API="${CNB_API:-https://api.cnb.cool}"
CNB_WEB="${CNB_WEB:-https://cnb.cool}"
CNB_ACCEPT="application/vnd.cnb.api+json"
CNB_REPO="${CNB_REPO:?CNB_REPO is required, e.g. tiouo/portal}"
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
  local method="$1" path="$2" body="${3:-}"
  local args=(-sS -X "$method" -H "Authorization: Bearer ${CNB_TOKEN}" -H "Accept: ${CNB_ACCEPT}")
  if [ -n "$body" ]; then
    args+=(-H "Content-Type: application/json" --data "$body")
  fi
  curl "${args[@]}" "${CNB_API}${path}"
}

# 按标签查询 Release，成功时输出 release_id 到 stdout
get_release_id() {
  local json code
  json=$(curl -sS -o /tmp/cnb_release.json -w "%{http_code}" \
    -H "Authorization: Bearer ${CNB_TOKEN}" -H "Accept: ${CNB_ACCEPT}" \
    "${CNB_API}/${CNB_REPO}/-/releases/tags/${CNB_TAG}" 2>/dev/null || true)
  code="${json}"
  if [ "${code}" = "200" ]; then
    jq -r '.id // empty' /tmp/cnb_release.json 2>/dev/null || true
  else
    echo ""
  fi
}

# 把标签推到 CNB 仓库，保证创建 Release 前标签一定存在。
# 绕过 CNB「先建标签再建 Release」的限制：此处直接用 git 把 refs/tags/{CNB_TAG}
# 推到 CNB 仓库，随后 OpenAPI 创建 Release 即可成功。
ensure_cnb_tag() {
  local commit auth
  commit="${CNB_COMMIT:-$(git rev-parse HEAD)}"
  if [ -n "${commit}" ]; then
    git tag -f "${CNB_TAG}" "${commit}"
  fi

  echo "[cnb] 推送标签 ${CNB_TAG}（${commit}）到 ${CNB_GIT_URL}"
  # CNB 用户名固定为 cnb，密码为访问令牌（见 https://docs.cnb.cool/zh/guide/git-access.html）
  auth="$(printf '%s:%s' 'cnb' "${CNB_TOKEN}" | base64 | tr -d '\n')"
  git -c "http.extraHeader=Authorization: Basic ${auth}" push --force "${CNB_GIT_URL}" "refs/tags/${CNB_TAG}" >/dev/null || {
    echo "[cnb] 标签推送失败（非致命，忽略）。" >&2
  }
}

# 构造 Release 描述 JSON（含 name/body/prerelease/make_latest）
release_meta_json() {
  local body=""
  if [ -n "${CNB_BODY_FILE:-}" ] && [ -f "${CNB_BODY_FILE}" ]; then
    body="$(cat "${CNB_BODY_FILE}")"
  fi
  jq -n \
    --arg tag "${CNB_TAG}" \
    --arg name "${CNB_NAME}" \
    --arg body "${body}" \
    --argjson pre "${CNB_PRERELEASE}" \
    --arg latest "${CNB_MAKE_LATEST}" \
    '{ tag_name: $tag, name: $name, body: $body, prerelease: $pre, make_latest: $latest }'
}

# 创建或更新 Release，成功时输出 release_id 到 stdout
ensure_release() {
  local id meta response
  ensure_cnb_tag
  id="$(get_release_id)"
  meta="$(release_meta_json)"
  if [ -n "${CNB_COMMIT:-}" ]; then
    meta="$(jq --arg target "${CNB_COMMIT}" '.target_commitish = $target' <<<"${meta}")"
  fi

  if [ -n "${id}" ]; then
    echo "[cnb] Release ${CNB_TAG} 已存在（id=${id}），更新标题与描述。"
    cnb_curl PATCH "/${CNB_REPO}/-/releases/${id}" "${meta}" >/dev/null
  else
    echo "[cnb] 创建 Release ${CNB_TAG}。"
    response="$(cnb_curl POST "/${CNB_REPO}/-/releases" "${meta}")"
    id="$(jq -r '.id // empty' <<<"${response}" 2>/dev/null || true)"
    if [ -z "${id}" ]; then
      echo "[cnb] 创建 Release 失败，响应内容：" >&2
      echo "${response}" >&2
      return 1
    fi
  fi
  echo "${id}"
}

# 上传单个附件到指定 Release
upload_asset() {
  local file="$1" release_id="$2" name size body json upload_url verify_url
  [ -f "${file}" ] || { echo "[cnb] 文件不存在：${file}" >&2; return 1; }
  name="$(basename "${file}")"
  size="$(stat -c%s "${file}" 2>/dev/null || stat -f%z "${file}")"
  body="$(jq -n --arg n "${name}" --argjson s "${size}" '{ asset_name: $n, size: $s, overwrite: true }')"

  json="$(cnb_curl POST "/${CNB_REPO}/-/releases/${release_id}/asset-upload-url" "${body}")"
  upload_url="$(jq -r '.upload_url // empty' <<<"${json}" 2>/dev/null || true)"
  verify_url="$(jq -r '.verify_url // empty' <<<"${json}" 2>/dev/null || true)"
  if [ -z "${upload_url}" ] || [ -z "${verify_url}" ]; then
    echo "[cnb] 获取 ${name} 上传地址失败：${json}" >&2
    return 1
  fi

  # 上传地址有效期 30 秒，必须立即流式上传
  curl -sS -X PUT --data-binary "@${file}" "${upload_url}" >/dev/null
  cnb_curl POST "${verify_url}" "{}" >/dev/null
  echo "[cnb] 已上传附件：${name}（${size} 字节）"
}

main() {
  local release_id
  release_id="$(ensure_release)"
  if [ -z "${release_id}" ]; then
    echo "[cnb] 未能确定 CNB Release，中止附件上传。" >&2
    exit 1
  fi

  # shellcheck disable=SC2086
  for file in ${CNB_FILES}; do
    upload_asset "${file}" "${release_id}"
  done
  echo "[cnb] CNB 同步完成：${CNB_REPO} @ ${CNB_TAG}"
}

main