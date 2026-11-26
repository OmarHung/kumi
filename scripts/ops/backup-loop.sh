#!/bin/bash
# backup 服務（compose.yaml）在容器裡跑的迴圈。不要直接在主機上執行：它要用容器裡的 pg_dump 與 /backups。
#
#   backup-loop.sh             啟動時與之後每 BACKUP_CHECK_SECONDS 秒檢查一次，最新的排程備份
#                              超過 BACKUP_INTERVAL_HOURS 小時就做一份，再只留最新的 BACKUP_KEEP 份排程備份。
#   backup-loop.sh --once 標籤  馬上做一份帶標籤的備份就結束，標準輸出只印檔名（給 backup-now.sh 用）。
#
# 檔名：pmtool-YYYYMMDD-HHMMSS.dump（排程）或 pmtool-YYYYMMDD-HHMMSS-標籤.dump（手動、部署前、還原前）。
# 設了 BACKUP_AGE_RECIPIENT（age 的公鑰）時，檔名再加 .age（.dump.age）：資料在寫進備份目錄之前就加密，目錄裡不會出現明文的資料庫備份。
# 附件不加密（見 docs/OPERATIONS.md 第 3 節）。私鑰不在這個容器裡：備份只需要公鑰，還原才要私鑰。
# 連線用 PGHOST、PGUSER、PGDATABASE、PGPASSWORD 這幾個 PostgreSQL 的標準環境變數；
# 附件：pg_dump 成功後，把唯讀掛進來的 /attachments 增量複製到 /backups/attachments/（copy-missing.sh：只補新的、永遠不刪），
# 複製失敗這一份備份就算失敗。附件不算在 BACKUP_KEEP 的清理裡。
# PMTOOL_PROJECT 是 compose 的專案名稱，用來確認備份目錄是這一套的（見 claim_dir）。
set -euo pipefail

readonly DIR=/backups
readonly SCHEDULED_PATTERN='^pmtool-[0-9]{8}-[0-9]{6}[.]dump([.]age)?$'
readonly LABEL_PATTERN='^[a-z0-9][a-z0-9-]{0,31}$'
# 暫存檔超過這麼久還在，就是之前被中斷的備份留下來的：手動備份可能正在寫，所以不能一看到就刪。
readonly STALE_PARTIAL_MINUTES=180
# 寫著這個目錄屬於哪個 compose 專案。主機上的腳本（scripts/ops/lib.sh）用同一個檔名。
readonly MARKER="$DIR/.pmtool-project"
# 附件的來源（唯讀掛進來）與備份裡的位置。status.sh 用同樣的名字。
readonly ATTACHMENTS_SRC=/attachments
readonly ATTACHMENTS_DST="$DIR/attachments"
# 最近一次附件複製的結果：一行「時間（秒）<tab>ok 或 failed<tab>複製幾個<tab>說明」。status.sh 讀它。
readonly ATTACHMENTS_STATE="$DIR/.pmtool-attachments-status"

current_tmp=""
verify_fifo=""

log() {
  printf '%s %s\n' "$(date '+%Y-%m-%dT%H:%M:%S%:z')" "$*" >&2
}

fail_config() {
  log "設定錯誤：$*"
  exit 1
}

check_number() {
  local name="$1" value="$2"
  [[ "$value" =~ ^[1-9][0-9]*$ ]] || fail_config "$name 要是正整數，現在是「${value}」。改 .env 之後重新啟動 backup 服務。"
}

human_size() {
  local bytes="$1"
  if ((bytes >= 1073741824)); then
    printf '%d.%d GB' $((bytes / 1073741824)) $((bytes % 1073741824 * 10 / 1073741824))
  elif ((bytes >= 1048576)); then
    printf '%d.%d MB' $((bytes / 1048576)) $((bytes % 1048576 * 10 / 1048576))
  elif ((bytes >= 1024)); then
    printf '%d KB' $((bytes / 1024))
  else
    printf '%d B' "$bytes"
  fi
}

# 排程備份，新的在前：每行「修改時間（秒）<tab>檔名」。用修改時間而不是檔名排序，時區改過也不會排錯。
list_scheduled() {
  local mtime name
  find "$DIR" -maxdepth 1 -type f \( -name 'pmtool-*.dump' -o -name 'pmtool-*.dump.age' \) -printf '%T@\t%f\n' |
    while IFS=$'\t' read -r mtime name; do
      if [[ "$name" =~ $SCHEDULED_PATTERN ]]; then
        printf '%s\t%s\n' "${mtime%%.*}" "$name"
      fi
    done | sort -rn
}

# 刪掉沒做完的暫存檔；有給第一個參數時也一起刪（pg_dump 的錯誤訊息檔）。
discard_tmp() {
  rm -f -- "$current_tmp"
  current_tmp=""
  if [[ -n "${1:-}" ]]; then
    rm -f -- "$1"
  fi
}

record_attachments_state() {
  local result="$1" copied="$2" note="$3" tmp="$ATTACHMENTS_STATE.tmp"
  printf '%s\t%s\t%s\t%s\n' "$(date +%s)" "$result" "$copied" "$note" >"$tmp" && mv -f -- "$tmp" "$ATTACHMENTS_STATE" || true
}

# 把附件增量複製到備份目錄。檔案建好之後不會改，所以只補新的；永遠不刪（api 刪掉的附件，備份裡照舊留著）。
# 在 pg_dump 之後做：資料庫裡的每一列，它的檔案都早在 pg_dump 之前就寫好了，所以通常備份裡找得到；
# 反過來（pg_dump 之後才上傳的檔案先被複製進來）只是多一個沒有資料列的檔案，無害。
# 例外：檔案在 pg_dump 之前上傳、卻在「pg_dump 之後、複製之前」就被刪掉（上傳後在同一個備份週期內就刪），
# 這個檔案從沒被備份到，那一份 dump 還原後有資料列、沒有檔案（下載會 404）。只影響這種剛傳就刪的檔案。
backup_attachments() {
  local out copied
  if ! out="$(bash /ops/copy-missing.sh "$ATTACHMENTS_SRC" "$ATTACHMENTS_DST")"; then
    record_attachments_state failed 0 "附件複製失敗"
    log "備份失敗：附件沒有複製成功（見上面的訊息）。"
    return 1
  fi
  copied="${out##*copied=}"
  [[ "$copied" =~ ^[0-9]+$ ]] || copied=0
  record_attachments_state ok "$copied" ""
  log "附件：這次複製了 ${copied} 個新檔案到 ${ATTACHMENTS_DST}。"
}

# 沒有加密：pg_dump 寫暫存檔，再用 pg_restore 從頭到尾讀過一遍（輸出丟掉）。
dump_plain() {
  local errors="$1"
  if ! pg_dump --format=custom --file="$current_tmp" 2>"$errors"; then
    log "備份失敗：pg_dump 沒有成功（$(head -n 3 "$errors" | tr '\n' ' ')）。"
    return 1
  fi
  if ! pg_restore --file=/dev/null "$current_tmp" 2>"$errors"; then
    log "備份失敗：剛做好的檔案讀不回來（$(head -n 3 "$errors" | tr '\n' ' ')），已經刪掉。"
    return 1
  fi
}

# 加密：pg_dump 的輸出直接送進 age，備份目錄裡從頭到尾沒有明文。
# 讀回來的檢查不能對加密的檔案做（容器裡沒有私鑰），所以把同一份串流分一條給 pg_restore 讀：
# 它能讀完整份、不報錯，才算好。pg_dump、age、pg_restore 任何一個失敗都算失敗。
dump_encrypted() {
  local errors="$1" verify_errors dump_status age_status verify_status verify_pid pipe_status
  verify_errors="$(mktemp)" || { log "備份失敗：建不了暫存檔。"; return 1; }
  verify_fifo="$(mktemp -u)"
  mkfifo "$verify_fifo" || { rm -f -- "$verify_errors"; log "備份失敗：建不了暫存的管線。"; return 1; }
  pg_restore --file=/dev/null <"$verify_fifo" 2>"$verify_errors" &
  verify_pid=$!
  # 每個指令的結果自己看 PIPESTATUS：pg_restore 提早結束時，tee 寫不進管線，pg_dump 會跟著收到 SIGPIPE。
  set +e
  pg_dump --format=custom 2>"$errors" | tee -- "$verify_fifo" | age --recipient "$BACKUP_AGE_RECIPIENT" --output "$current_tmp" 2>>"$errors"
  pipe_status=("${PIPESTATUS[@]}")
  dump_status=${pipe_status[0]}
  age_status=${pipe_status[2]}
  wait "$verify_pid"
  verify_status=$?
  set -e
  rm -f -- "$verify_fifo"
  verify_fifo=""
  if ((dump_status != 0)); then
    log "備份失敗：pg_dump 沒有成功（$(head -n 3 "$errors" | tr '\n' ' ')）。"
  elif ((age_status != 0)); then
    log "備份失敗：age 加密沒有成功（$(head -n 3 "$errors" | tr '\n' ' ')）。確認 BACKUP_AGE_RECIPIENT 是 age 的公鑰（age1 開頭）。"
  elif ((verify_status != 0)); then
    log "備份失敗：剛做好的串流讀不回來（$(head -n 3 "$verify_errors" | tr '\n' ' ')），已經刪掉。"
  fi
  rm -f -- "$verify_errors"
  ((dump_status == 0 && age_status == 0 && verify_status == 0))
}

# 做一份備份，成功時在標準輸出印出檔名。
# 先寫暫存檔，讀過一遍才改成正式的名字，所以目錄裡看得到的備份都是完整的。
# 只讀目錄（--list）不夠：資料的部分壞了也看不出來。
# 每一步都自己檢查結果：呼叫端用 || 接住失敗時，bash 會在函式裡關掉 set -e。
make_backup() {
  local label="$1" name errors size extension=dump
  [[ -z "${BACKUP_AGE_RECIPIENT:-}" ]] || extension=dump.age
  name="pmtool-$(date '+%Y%m%d-%H%M%S')${label:+-$label}.$extension"
  # 同一秒做了兩份同標籤的（例如連按兩次）：等一秒換個名字，不蓋掉前一份。
  while [[ -e "$DIR/$name" ]]; do
    sleep 1
    name="pmtool-$(date '+%Y%m%d-%H%M%S')${label:+-$label}.$extension"
  done
  current_tmp="$DIR/.$name.partial"
  errors="$(mktemp)" || { log "備份失敗：建不了暫存檔。"; return 1; }

  if [[ -n "${BACKUP_AGE_RECIPIENT:-}" ]]; then
    dump_encrypted "$errors" || { discard_tmp "$errors"; return 1; }
  else
    dump_plain "$errors" || { discard_tmp "$errors"; return 1; }
  fi
  rm -f -- "$errors"
  # 附件複製失敗時整份備份算失敗：只有資料庫、沒有檔案的備份，還原之後會有找不到檔案的附件。剛做好的 dump 刪掉，下一次檢查再做一份。
  if ! backup_attachments; then
    discard_tmp ""
    return 1
  fi
  if ! chmod 600 "$current_tmp" || ! mv -- "$current_tmp" "$DIR/$name"; then
    log "備份失敗：暫存檔改不成正式的名字 ${name}（目錄的權限或磁碟空間？）。"
    discard_tmp ""
    return 1
  fi
  current_tmp=""
  size="$(human_size "$(stat -c %s "$DIR/$name")")"
  log "備份完成：${name}（${size}）"
  printf '%s\n' "$name"
}

# 只動排程備份：帶標籤的不算在保留的份數裡，也不會被刪。
prune_scheduled() {
  local keep="$1" index=0 mtime name scheduled
  scheduled="$(list_scheduled)"
  while IFS=$'\t' read -r mtime name; do
    [[ -n "$name" ]] || continue
    index=$((index + 1))
    ((index > keep)) || continue
    if rm -f -- "$DIR/$name"; then
      log "刪掉超過保留份數（$keep 份）的排程備份：$name"
    else
      log "刪不掉超過保留份數的排程備份：$name"
    fi
  done <<<"$scheduled"
}

remove_stale_partials() {
  local path partials
  partials="$(find "$DIR" -maxdepth 1 -type f -name '.pmtool-*.partial' -mmin "+$STALE_PARTIAL_MINUTES")"
  while IFS= read -r path; do
    [[ -n "$path" ]] || continue
    if rm -f -- "$path"; then
      log "刪掉之前被中斷的備份留下的暫存檔：${path##*/}"
    fi
  done <<<"$partials"
}

check_once() {
  local interval_hours="$1" keep="$2" scheduled newest mtime name age_seconds
  remove_stale_partials
  scheduled="$(list_scheduled)"
  newest="${scheduled%%$'\n'*}"
  if [[ -z "$newest" ]]; then
    log "還沒有排程備份，現在做一份。"
    make_backup "" >/dev/null || return 1
  else
    IFS=$'\t' read -r mtime name <<<"$newest"
    age_seconds=$(($(date +%s) - mtime))
    if ((age_seconds < interval_hours * 3600)); then
      log "最新的排程備份 $name 是 $((age_seconds / 3600)) 小時前，還不用做。"
    else
      log "最新的排程備份 $name 是 $((age_seconds / 3600)) 小時前，超過 $interval_hours 小時，現在做一份。"
      make_backup "" >/dev/null || return 1
    fi
  fi
  prune_scheduled "$keep"
}

on_exit() {
  # 被停掉時正在寫的暫存檔不留下來。
  if [[ -n "$current_tmp" ]]; then
    rm -f -- "$current_tmp"
  fi
  if [[ -n "$verify_fifo" ]]; then
    rm -f -- "$verify_fifo"
  fi
}

# 兩套環境（例如測試的那一套沿用預設的 BACKUP_DIR）共用同一個備份目錄時，備份會混在一起：
# 清理把兩套合起來只留 BACKUP_KEEP 份，一套看到另一套很新的備份就不做自己的。
# 所以目錄裡放一個寫著專案名稱的標記檔：沒有就寫上自己的，是別的專案的就拒絕。
claim_dir() {
  local project="${PMTOOL_PROJECT:-}" owner=""
  [[ -n "$project" ]] || fail_config "沒有 PMTOOL_PROJECT：compose.yaml 的 backup 服務要帶它（專案名稱）。"
  if [[ -f "$MARKER" ]]; then
    IFS= read -r owner <"$MARKER" || true
    [[ "$owner" == "$project" ]] ||
      fail_config "備份目錄（BACKUP_DIR）是專案「${owner}」的，這一套是「${project}」。兩套共用一個備份目錄，備份會混在一起、互相清掉。把這一套 env 檔的 BACKUP_DIR 換成自己的目錄（見 docs/OPERATIONS.md 第 9 節），再重新啟動 backup 服務。"
    return 0
  fi
  printf '%s\n' "$project" >"$MARKER" || fail_config "寫不進備份目錄的標記檔 .pmtool-project。"
  chmod 600 "$MARKER"
  log "備份目錄標記成專案 ${project} 的（.pmtool-project）。"
}

# 公鑰寫錯時，每一份備份都會失敗：啟動時就用它加密一小段資料試一次，錯了直接停下來，不要等到第一份備份。
check_age_recipient() {
  [[ -n "${BACKUP_AGE_RECIPIENT:-}" ]] || return 0
  command -v age >/dev/null 2>&1 || fail_config "設了 BACKUP_AGE_RECIPIENT，但備份容器裡沒有 age。用 docker compose build backup 重建備份的映像檔。"
  printf 'x' | age --recipient "$BACKUP_AGE_RECIPIENT" --output /dev/null 2>/dev/null ||
    fail_config "BACKUP_AGE_RECIPIENT 不是有效的 age 公鑰（要 age1 開頭，用 age-keygen 產生）。改 .env 之後重新啟動 backup 服務。"
  log "備份加密已開啟：寫進備份目錄之前先用 age 加密（檔名 .dump.age）。"
}

prepare_dir() {
  umask 077
  mkdir -p "$DIR"
  chmod 700 "$DIR"
  claim_dir
}

run_once() {
  local label="${1:-}"
  [[ "$label" =~ $LABEL_PATTERN ]] || fail_config "標籤只能用小寫英文字母、數字與 -，最多 32 個字，現在是「${label}」。"
  check_age_recipient
  prepare_dir
  make_backup "$label"
}

run_loop() {
  local interval_hours="${BACKUP_INTERVAL_HOURS:-24}" keep="${BACKUP_KEEP:-14}" check_seconds="${BACKUP_CHECK_SECONDS:-3600}"
  check_number BACKUP_INTERVAL_HOURS "$interval_hours"
  check_number BACKUP_KEEP "$keep"
  check_number BACKUP_CHECK_SECONDS "$check_seconds"
  check_age_recipient
  prepare_dir
  # bash 當 PID 1 時不理會沒設處理的 SIGTERM，docker stop 會等 10 秒才硬砍；sleep 放背景再 wait，才會馬上結束。
  trap 'log "收到停止的訊號，結束。"; exit 0' TERM INT
  log "備份服務啟動：每 $check_seconds 秒檢查一次，排程備份超過 $interval_hours 小時就做一份，留最新 $keep 份。"
  while true; do
    # 一次失敗（例如資料庫正在重新啟動）不讓服務結束：下一次檢查會再試。
    check_once "$interval_hours" "$keep" || log "這一次沒有備份成功，$check_seconds 秒後再試。"
    sleep "$check_seconds" &
    wait $!
  done
}

main() {
  trap on_exit EXIT
  case "${1:-}" in
    --once) run_once "${2:-}" ;;
    "") run_loop ;;
    *) fail_config "不認得的參數「$1」。用法：backup-loop.sh [--once 標籤]" ;;
  esac
}

# 整支腳本包在 main 裡、同一行結束：服務跑著的時候更新這個檔案（例如 git pull），bash 不會讀到改了一半的內容。
main "$@"; exit $?
