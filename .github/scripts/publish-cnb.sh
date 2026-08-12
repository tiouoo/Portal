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
# CNB_FILES 为空时仅创建 Release/Tag，不上传附件（附件可后续手动上传）
if [ -z "${CNB_FILES:-}" ]; then
  echo "[cnb] CNB_FILES 未配置，仅创建 Release/Tag，跳过附件上传。"
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

cnb_git_auth() {
  printf '%s:%s' 'cnb' "${CNB_TOKEN}" | base64 | tr -d '\n'
}

# 标签是 Release 的载体：推不上 CNB，就没有版本号可用，必须致命终止。
ensure_cnb_tag() {
  local commit push_out host target
  commit="${CNB_COMMIT:-$(git rev-parse HEAD)}"
  if [ -n "$commit" ]; then
    git tag -f "${CNB_TAG}" "${commit}" >/dev/null 2>&1 || true
  fi
  host="$(printf '%s' "${CNB_GIT_URL}" | sed -E 's#^[a-z]+://([^/]+)/.*#\1#')"
  target="${CNB_TAG}"
  push_out="$(GIT_TERMINAL_PROMPT=0 git -c "http.extraHeader=Authorization: Basic $(cnb_git_auth)" \
    push --force "${CNB_GIT_URL}" "refs/tags/${target}" 2>&1)" || {
    push_out="$(GIT_TERMINAL_PROMPT=0 git \
      push --force "https://cnb:${CNB_TOKEN}@${host}/${CNB_REPO}.git" \
      "refs/tags/${target}" 2>&1)"
  }
  if [ -n "$push_out" ]; then
    printf '[cnb] 标签推送详情：\n%s\n' "${push_out}" >&2
  fi
  echo "[cnb] 标签已推送：${CNB_TAG} -> ${commit}"
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

# CNB 资产域名已知海外节点（腾讯云香港 CLB：129.226.78.124，实测境外 DoH 亦返回该 IP）。
# 上传前强制解析到“实测最快可达”节点，绕过地理 DNS 给大陆 runner 下发大陆 IP 的上传慢问题。
CNB_DOH_URL="${CNB_DOH_URL:-https://cloudflare-dns.com/dns-query}"
CNB_ASSET_IP_OVERSEAS="${CNB_ASSET_IP_OVERSEAS:-129.226.78.124}"

asset_host_of() {
  echo "${1}" | sed -E 's#^[a-z]+://([^/:]+).*#\1#'
}

probe_ip() {
  local ip="$1"
  python3 - "$ip" <<PY 2>/dev/null || echo 99999
import socket, sys, time
ip = sys.argv[1]
t = time.time()
try:
    s = socket.create_connection((ip, 443), timeout=3)
    s.close()
    print(int((time.time() - t) * 1000))
except Exception:
    print(99999)
PY
}

pick_best_ip() {
  local host="$1" ips="" t best btime
  [ -n "${CNB_ASSET_IP:-}" ] && { echo "${CNB_ASSET_IP}"; return; }
  # 1) 本地解析
  ips+="$(python3 - "$host" <<PY 2>/dev/null || true
import socket, sys
try:
    print(" ".join(socket.gethostbyname_ex(sys.argv[1])[2]))
except Exception:
    pass
PY
)"
  # 2) 境外 DoH 结果 + 已知海外节点 IP
  ips+=" $(curl -fsS --max-time 8 "${CNB_DOH_URL}?name=${host}&type=A" -H "accept: application/dns-json" 2>/dev/null \
    | python3 -c 'import sys,json
try:
    d=json.load(sys.stdin)
    print(" ".join(a["data"] for a in d.get("Answer",[]) if a.get("type")==1))
except Exception: print("")' 2>/dev/null)"
  case "${host}" in
    *.cnb.cool) ips+=" ${CNB_ASSET_IP_OVERSEAS}" ;;
  esac
  best=""
  btime=99999999
  while read -r ip; do
    [ -z "${ip}" ] && continue
    t="$(probe_ip "${ip}")"
    if [ "${t}" -lt "${btime}" ]; then btime="${t}"; best="${ip}"; fi
  done <<<"$(printf '%s\n' ${ips} | awk 'NF && !seen[$0]++')"
  echo "${best}"
}

upload_asset() {
  local file="$1" release_id="$2" name size json upload_url verify_url host scheme port best resolve_args
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
  scheme="${upload_url%%:*}"; port=443; [ "${scheme}" = "http" ] && port=80
  host="$(asset_host_of "${upload_url}")"
  best="$(pick_best_ip "${host}")"
  resolve_args=()
  if [ -n "${best}" ]; then
    resolve_args=(--resolve "${host}:${port}:${best}")
    echo "[cnb] 上传节点（强制解析）：${host} -> ${best}"
  fi
  curl -g -sS -X PUT "${resolve_args[@]}" \
    --connect-timeout 10 --max-time "${CNB_UPLOAD_MAX_TIME:-3600}" \
    --retry 3 --retry-delay 2 --retry-connrefused --retry-all-errors \
    --data-binary "@${file}" "${upload_url}" >/dev/null
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
  # CNB_FILES 未配置时仅创建 Release/Tag，不影响手动上传附件
  if [ -n "${CNB_FILES:-}" ]; then
    shopt -s nullglob
    for file in ${CNB_FILES}; do
      upload_asset "${file}" "${release_id}"
    done
  else
    echo "[cnb] 已跳过附件上传，可在 CNB Release 页面手动上传。"
  fi
  echo "[cnb] CNB 同步完成：${CNB_TAG}"
}

main