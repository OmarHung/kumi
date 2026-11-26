#!/bin/bash
# 一眼看出這一套好不好：服務、就緒、最新的排程備份是幾小時前、備份的份數與大小、磁碟剩餘空間、
# api 過去 24 小時的 Error／Critical 筆數。有問題時列出來，結束代碼是 1。
# 附件：資料夾裡幾個、多大；備份目錄裡幾個；還沒備份到的幾個（超過一天的算問題）；最近一次附件複製有沒有失敗。
# 用法：bash scripts/ops/status.sh
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# 備份服務每小時才檢查一次，Mac 剛醒來時也要一點時間補做：超過間隔再多這麼久才算太舊。
readonly BACKUP_GRACE_HOURS=2
readonly SERVICES="db api backup"
# 附件上傳後超過這麼久還沒進備份目錄，就是備份有問題（排程備份一天一次，一天內還沒備份到是正常的）。
readonly ATTACHMENT_UNBACKED_MINUTES=1440
# 附件資料夾所在的磁碟剩餘低於這兩個門檻之一就是問題（KB、百分比）。附件與資料庫、備份在同一顆碟時，滿了整套會一起停。
readonly ATTACHMENT_DISK_MIN_FREE_KB=$((5 * 1024 * 1024))
readonly ATTACHMENT_DISK_MIN_FREE_PERCENT=10

PROBLEMS=""

problem() {
  PROBLEMS="${PROBLEMS}  - $*"$'\n'
}

# 中文字的寬度和位元組數對不上，printf 對不齊，所以不對齊，只用「標題：內容」。
row() {
  printf '%s：%s\n' "$1" "$2"
}

show_services() {
  local states service line state health
  states="$(compose ps --all --format '{{.Service}}|{{.State}}|{{.Health}}' 2>/dev/null || true)"
  info "服務"
  for service in $SERVICES; do
    line="$(printf '%s\n' "$states" | grep "^${service}|" | head -n 1 || true)"
    if [[ -z "$line" ]]; then
      printf '  %-8s%s\n' "$service" "沒有容器"
      problem "$service 沒有容器：執行 bash scripts/ops/deploy-local.sh。"
      continue
    fi
    IFS='|' read -r _ state health <<<"$line"
    printf '  %-8s%s\n' "$service" "${state}${health:+（${health}）}"
    if [[ "$state" != running ]]; then
      problem "$service 沒有在跑（${state}）：看記錄 $(compose_hint) logs --tail 50 ${service}；叫回來：$(compose_hint) start ${service}"
    elif [[ -n "$health" && "$health" != healthy ]]; then
      problem "$service 的健康檢查是 ${health}：看記錄 $(compose_hint) logs --tail 50 ${service}"
    fi
  done
}

show_ready() {
  local url
  url="$(api_url)/health/ready"
  if curl -fsS -m 5 -o /dev/null "$url" 2>/dev/null; then
    row "就緒" "正常（${url}）"
  else
    row "就緒" "沒有回應（${url}）"
    problem "api 沒有就緒：看 bash scripts/ops/errors.sh --since 1h，或 $(compose_hint) logs --tail 50 api"
  fi
}

show_backups() {
  local dir interval newest mtime name age_hours all count total=0 path owner plain
  dir="$(backup_dir)"
  interval="$(env_get BACKUP_INTERVAL_HOURS 24)"
  [[ "$interval" =~ ^[1-9][0-9]*$ ]] || { problem "env 檔的 BACKUP_INTERVAL_HOURS 不是正整數（${interval}）。"; interval=24; }
  if [[ ! -d "$dir" ]]; then
    row "備份" "目錄不存在（${dir}）"
    problem "備份目錄 $dir 不存在：backup 服務啟動時會建立它，確認它在跑。"
    return
  fi
  owner="$(backup_dir_owner)"
  if [[ -n "$owner" && "$owner" != "$PROJECT" ]]; then
    row "備份" "目錄是專案 ${owner} 的（${dir}）"
    problem "$(backup_dir_conflict_message "$owner")"
    return
  fi

  newest="$(list_backups scheduled | head -n 1 || true)"
  if [[ -z "$newest" ]]; then
    row "最新的排程備份" "還沒有"
    problem "還沒有排程備份：看 $(compose_hint) logs --tail 20 backup"
  else
    IFS=$'\t' read -r mtime name <<<"$newest"
    age_hours=$((($(date +%s) - mtime) / 3600))
    row "最新的排程備份" "${name}，${age_hours} 小時前（超過 ${interval} 小時就做新的一份）"
    if ((age_hours >= interval + BACKUP_GRACE_HOURS)); then
      problem "最新的排程備份是 $age_hours 小時前，太舊了：看 $(compose_hint) logs --tail 20 backup"
    fi
  fi

  all="$(list_backups all)"
  count=0
  while IFS=$'\t' read -r mtime name; do
    [[ -n "$name" ]] || continue
    path="$dir/$name"
    count=$((count + 1))
    total=$((total + $(file_size "$path")))
  done <<<"$all"
  row "備份" "${count} 份，共 $(human_size "$total")（${dir}）"
  if [[ -n "$(env_get BACKUP_AGE_RECIPIENT)" ]]; then
    row "備份加密" "開著（age，檔名 .dump.age；還原要用私鑰：BACKUP_AGE_IDENTITY_FILE）"
  else
    row "備份加密" "沒開（BACKUP_AGE_RECIPIENT 沒設，備份是明文的 .dump，docs/OPERATIONS.md 第 3 節）"
  fi
  row "磁碟剩餘" "$(df -Ph "$dir" | awk 'NR == 2 { print $4 }')"
  # 開了加密之後，以前的明文備份不會自己消失：留著就等於加密沒有保護到那幾份（docs/OPERATIONS.md 第 3 節）。
  if [[ -n "$(env_get BACKUP_AGE_RECIPIENT)" ]]; then
    plain="$(find "$dir" -maxdepth 1 -type f -name '*.dump' 2>/dev/null | wc -l | tr -d ' ')"
    if ((plain > 0)); then
      row "提醒" "備份加密已經開了，但 ${dir} 裡還有 ${plain} 份明文的 .dump：加密開了之後不會回頭加密它們，照 docs/OPERATIONS.md 第 3 節「開了加密之後，舊的明文備份」處理"
    fi
  fi
}

# 附件資料夾裡的檔案數與大小（KB，du 的值）。資料夾不存在就是 0 0。
attachment_stats() {
  local dir="$1" count kb
  [[ -d "$dir" ]] || { printf '0 0'; return 0; }
  count="$(find "$dir" "${ATTACHMENT_FILE_FILTER[@]}" | wc -l | tr -d ' ')"
  kb="$(du -sk "$dir" | awk '{ print $1 }')"
  printf '%s %s' "$count" "$kb"
}

# 附件資料夾所在磁碟的「總量 剩餘」（KB）。df -P 的格式在 macOS 與 Linux 一樣。
disk_total_and_free_kb() {
  df -Pk "$1" | awk 'NR == 2 { print $2, $4 }'
}

# 剩餘的空間不夠就記成問題：絕對值（GiB）或比例，任何一個低於門檻。
check_attachment_disk() {
  local dir="$1" total_kb free_kb percent
  read -r total_kb free_kb <<<"$(disk_total_and_free_kb "$dir")"
  if [[ ! "$total_kb" =~ ^[0-9]+$ || ! "$free_kb" =~ ^[0-9]+$ || "$total_kb" -eq 0 ]]; then
    row "附件資料夾的磁碟剩餘" "讀不到"
    return
  fi
  percent=$((free_kb * 100 / total_kb))
  row "附件資料夾的磁碟剩餘" "$(human_size $((free_kb * 1024)))（${percent}%）"
  if ((free_kb < ATTACHMENT_DISK_MIN_FREE_KB || percent < ATTACHMENT_DISK_MIN_FREE_PERCENT)); then
    problem "附件資料夾所在的磁碟只剩 $(human_size $((free_kb * 1024)))（${percent}%），低於 5 GiB 或 ${ATTACHMENT_DISK_MIN_FREE_PERCENT}%：附件與資料庫、備份在同一顆碟時，滿了整套會一起停。請清出空間或換更大的碟。"
  fi
}

# 數有幾個檔案在備份目錄裡還沒有：標準輸入是檔案路徑（一行一個）。
count_unbacked() {
  local dir="$1" backup="$2" path total=0
  while IFS= read -r path; do
    [[ -n "$path" ]] || continue
    [[ -e "$backup/${path#"$dir"/}" ]] || total=$((total + 1))
  done
  printf '%s' "$total"
}

# 資料夾裡有、備份目錄裡還沒有的檔案：印出「幾個 其中超過一天的幾個」。
attachments_unbacked() {
  local dir="$1" backup="$2" total old
  total="$(find "$dir" "${ATTACHMENT_FILE_FILTER[@]}" | count_unbacked "$dir" "$backup")"
  old="$(find "$dir" "${ATTACHMENT_FILE_FILTER[@]}" -mmin "+$ATTACHMENT_UNBACKED_MINUTES" | count_unbacked "$dir" "$backup")"
  printf '%s %s' "$total" "$old"
}

show_attachments() {
  local dir backup stats count kb backed_count unbacked total old state_file state_time state_result state_note
  dir="$(attachments_dir)"
  backup="$(backup_attachments_dir)"
  if [[ ! -d "$dir" ]]; then
    row "附件資料夾" "不存在（${dir}）"
    problem "附件資料夾 $dir 不存在：執行 bash scripts/ops/deploy-local.sh（它會建立並確認 api 寫得進去）。"
    return
  fi
  read -r count kb <<<"$(attachment_stats "$dir")"
  row "附件資料夾" "${count} 個檔案，約 $(human_size $((kb * 1024)))（${dir}）"
  check_attachment_disk "$dir"
  if [[ -d "$backup" ]]; then
    read -r backed_count _ <<<"$(attachment_stats "$backup")"
  else
    backed_count=0
  fi
  row "附件的備份" "${backed_count} 個檔案（${backup}）"
  read -r total old <<<"$(attachments_unbacked "$dir" "$backup")"
  row "還沒備份到的附件" "${total} 個（其中超過一天的 ${old} 個）"
  if ((old > 0)); then
    problem "有 ${old} 個附件上傳超過一天、還沒進備份目錄：bash scripts/ops/backup-now.sh，再看 $(compose_hint) logs --tail 20 backup"
  fi
  state_file="$(backup_dir)/$ATTACHMENTS_STATE_FILE"
  if [[ -f "$state_file" ]]; then
    IFS=$'\t' read -r state_time state_result _ state_note <"$state_file" || true
    if [[ "$state_result" == failed ]]; then
      row "最近一次附件備份" "失敗（${state_note}，$(date -r "$state_time" '+%Y-%m-%d %H:%M' 2>/dev/null || echo "$state_time")）"
      problem "最近一次附件備份失敗：看 $(compose_hint) logs --tail 20 backup。失敗的那一次整份備份都不算。"
    fi
  fi
}

show_errors() {
  local counts errors client
  if ! service_exists api; then
    return
  fi
  counts="$(api_logs_since 24h | api_log_report count || true)"
  read -r errors client <<<"$counts" || true
  errors="${errors:-0}"
  client="${client:-0}"
  row "api 過去 24 小時的錯誤" "${errors} 筆 Error／Critical（另外網頁回報的 Warning ${client} 筆）"
  if [[ "$errors" != 0 ]]; then
    problem "api 過去 24 小時有 $errors 筆 Error／Critical（另外網頁回報的 Warning ${client} 筆）：bash scripts/ops/errors.sh"
  fi
}

main() {
  case "${1:-}" in
    "") ;;
    -h | --help) sed -n '2,4p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; return 0 ;;
    *) die "不認得的參數「$1」。用法：bash scripts/ops/status.sh" ;;
  esac
  require_setup
  info "專案 ${PROJECT}（設定檔 ${ENV_FILE}）"
  info ""
  show_services
  info ""
  show_ready
  show_backups
  show_attachments
  show_errors
  info ""
  if [[ -n "$PROBLEMS" ]]; then
    info "有問題："
    printf '%s' "$PROBLEMS"
    exit 1
  fi
  info "一切正常。"
}

# 和 main 寫在同一行：執行中這個檔案被改寫時，bash 不會接著讀到改了一半的內容。
main "$@"; exit $?
