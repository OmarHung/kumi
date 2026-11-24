#!/bin/bash
# 列出 api 記錄裡 Warning 以上的事件：時間、等級、類別、訊息、例外的第一行、請求路徑。網頁回報的錯誤也在裡面。
# 用法：bash scripts/ops/errors.sh [--since 24h]     --since 收 docker logs 認得的寫法：30m、24h、2026-11-17T09:00
# 記錄每份 10 MB、留 5 份，更早的已經輪替掉了；容器重建（部署）之後也只剩新容器的記錄。
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  sed -n '2,4p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

main() {
  local since=24h
  while (($# > 0)); do
    case "$1" in
      --since)
        [[ -n "${2:-}" ]] || die "--since 後面要接時間，例如 --since 2h。"
        since="$2"
        shift 2
        ;;
      --since=*) since="${1#--since=}"; shift ;;
      -h | --help) usage; return 0 ;;
      *) usage >&2; die "不認得的參數「$1」。" ;;
    esac
  done
  require_setup
  service_exists api || die "專案 ${PROJECT} 沒有 api 的容器，沒有記錄可以看。先執行 bash scripts/ops/deploy-local.sh。"

  info "專案 ${PROJECT} 的 api，過去 ${since} 的 Warning、Error、Critical："
  info ""
  # 記錄最多有 50 MB，先寫到暫存檔再讀，不放進 shell 變數。
  LOG_FILE="$(mktemp)"
  trap 'rm -f -- "$LOG_FILE"' EXIT
  api_logs_since "$since" >"$LOG_FILE" || die "讀不到 api 的記錄。--since 的寫法對嗎（例如 30m、24h）？"
  api_log_report list <"$LOG_FILE"
}

# 和 main 寫在同一行：執行中這個檔案被改寫時，bash 不會接著讀到改了一半的內容。
main "$@"; exit $?
