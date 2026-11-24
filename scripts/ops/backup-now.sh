#!/bin/bash
# 馬上備份一份，印出檔名與大小。
# 用法：bash scripts/ops/backup-now.sh [標籤]     標籤預設 manual
# 帶標籤的備份不算在排程備份的保留份數裡，也不會被自動刪掉；不要了請自己刪。
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

main() {
  local label="${1:-manual}"
  if [[ "$label" == -h || "$label" == --help ]]; then
    sed -n '2,4p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    return 0
  fi
  require_setup
  make_labeled_backup "$label"
  info "備份完成：${BACKUP_PATH}（$(human_size "$(file_size "$BACKUP_PATH")")）"
}

# 和 main 寫在同一行：執行中這個檔案被改寫時，bash 不會接著讀到改了一半的內容。
main "$@"; exit $?
