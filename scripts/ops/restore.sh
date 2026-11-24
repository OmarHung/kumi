#!/bin/bash
# 把正在用的那一套換回某份備份。只是想確認備份讀得回來的話，用 restore-drill.sh，不要用這支。
# 用法：bash scripts/ops/restore.sh <備份檔> [--yes]
#   --yes  不問，直接還原（給別的腳本用）。沒帶時要打出專案名稱確認。
# 順序：備份檔先複製進資料庫的容器並讀過一遍 → 確認 → 替現況備份（標籤 pre-restore）→ 停 api 與 backup
#       → 刪掉並重建資料庫 → 還原 → 作廢所有工作階段 → 把備份裡的附件補回附件資料夾（只補缺的）→ 啟動 api 與 backup → 等就緒。
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

readonly DUMP_IN_CONTAINER=/tmp/pmtool-restore.dump
# 還原時要停下、完成後一定要讓它們跑起來的服務（不管開始之前有沒有在跑：上一次還原失敗時它們是停著的）。
readonly APP_SERVICES="api backup"
readonly AFTER_RESTORE_NOTICE="所有人都要重新登入；備份之後做的停用帳號、改密碼、重設密碼都退回了，要再做一次。"

# 還原之後作廢所有還有效的工作階段：工作階段的表跟著退回備份的時候，備份之後登出、改密碼、
# 停用而作廢的工作階段又會變回有效。舊的備份沒有這張表（identity.sessions），就不用做。
readonly SESSIONS_TABLE_EXISTS_SQL="SELECT to_regclass('identity.sessions') IS NOT NULL;"
readonly REVOKE_SESSIONS_SQL="
WITH revoked AS (
  UPDATE identity.sessions SET revoked_at = now(), revoked_reason = 'restored' WHERE revoked_at IS NULL
  RETURNING 1
)
SELECT count(*) FROM revoked;"

usage() {
  sed -n '2,6p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# 走到哪一步了：失敗時依它說明現況。
STAGE=checking
PRE_RESTORE_BACKUP=""
DUMP_COPIED=false

how_back() {
  if [[ -n "$PRE_RESTORE_BACKUP" ]]; then
    printf 'bash scripts/ops/restore.sh "%s" --yes' "$PRE_RESTORE_BACKUP"
  else
    printf 'bash scripts/ops/restore.sh <另一份備份> --yes（開始之前資料庫就不存在，沒有 pre-restore 備份）'
  fi
}

explain_state() {
  local start_again create_db
  start_again="$(compose_hint) up -d --no-build --no-deps ${APP_SERVICES}"
  create_db="$(compose_hint) exec db psql -U ${DB_USER} -d postgres -c 'CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};'"
  printf '\n還原沒有完成。現況：\n' >&2
  case "$STAGE" in
    checking | backing-up)
      printf '  - 什麼都沒動：資料庫、api 都和開始之前一樣。\n' >&2
      ;;
    stopping)
      printf '  - 資料庫沒動。api 或 backup 可能停了一半。\n' >&2
      printf '  - 讓它們回來：%s\n' "$start_again" >&2
      ;;
    dropping)
      printf '  - 資料庫 %s 可能還在，也可能已經刪掉了（看上面的訊息）。api 與 backup 停著。\n' "$DB_NAME" >&2
      printf '  - 還原前的資料在：%s\n' "${PRE_RESTORE_BACKUP:-（沒有）}" >&2
      printf '  - 再試一次：%s\n' "$(how_back)" >&2
      ;;
    creating)
      printf '  - 資料庫 %s 已經刪掉了，還沒建回來。api 與 backup 停著，現在開網頁會連不上。\n' "$DB_NAME" >&2
      printf '  - 還原前的資料在：%s\n' "${PRE_RESTORE_BACKUP:-（沒有）}" >&2
      printf '  - 先手動把資料庫建回來：%s\n' "$create_db" >&2
      printf '  - 再換回還原前的樣子：%s\n' "$(how_back)" >&2
      printf '    （資料庫不存在時也可以直接跑這一行：它會跳過 pre-restore，自己建資料庫。）\n' >&2
      ;;
    restoring)
      printf '  - 資料庫 %s 是空的：還原在一個交易裡做，失敗時整個撤銷了。\n' "$DB_NAME" >&2
      printf '  - api 與 backup 停著，現在開網頁會連不上。\n' >&2
      printf '  - 還原前的資料在：%s\n' "${PRE_RESTORE_BACKUP:-（沒有）}" >&2
      printf '  - 換回還原前的樣子：%s\n' "$(how_back)" >&2
      ;;
    revoking)
      printf '  - 資料已經換成備份的內容，但工作階段沒有作廢：備份之後登出或被作廢的登入可能又能用。\n' >&2
      printf '  - api 與 backup 停著。不要先啟動 api：再跑一次同一個還原，或換回還原前的樣子：%s\n' "$(how_back)" >&2
      ;;
    attachments)
      printf '  - 資料已經換成備份的內容，工作階段也作廢了，但備份裡的附件沒有（全部）補回 %s。\n' "$(attachments_dir)" >&2
      printf '  - api 與 backup 停著。再跑一次同一個還原會再補（只補缺的），或換回還原前的樣子：%s\n' "$(how_back)" >&2
      ;;
    starting)
      printf '  - 資料已經換成備份的內容，工作階段也作廢了，但 api 或 backup 沒有起來。\n' >&2
      printf '  - 再試一次：%s，然後 bash scripts/ops/status.sh\n' "$start_again" >&2
      printf '  - 還原前的資料在：%s\n' "${PRE_RESTORE_BACKUP:-（沒有）}" >&2
      ;;
    waiting)
      printf '  - 資料已經換成備份的內容，服務也啟動了，但 api 沒有就緒。看 bash scripts/ops/errors.sh --since 10m\n' >&2
      printf '  - 要換回還原前的樣子：%s\n' "$(how_back)" >&2
      ;;
  esac
}

remove_copied_dump() {
  if [[ "$DUMP_COPIED" == true ]]; then
    compose exec -T db rm -f "$DUMP_IN_CONTAINER" >/dev/null 2>&1 || true
    DUMP_COPIED=false
  fi
}

on_exit() {
  local status=$?
  # 備份檔裡是真實資料：不管成功、失敗或取消，都不留在資料庫的容器裡。
  remove_copied_dump
  if ((status != 0)) && [[ "$STAGE" != checking ]]; then
    explain_state
  fi
}

# 先把備份檔複製進資料庫的容器，從頭到尾讀過一遍（輸出丟掉）：
# 確認之後，就算這份檔案在主機上被清理刪掉或被改掉，還原的也是剛才讀過的這一份，而且在刪資料庫之前就知道它是好的。
copy_and_check_dump() {
  local file="$1"
  DUMP_COPIED=true
  compose cp "$file" "db:${DUMP_IN_CONTAINER}" >/dev/null 2>&1 ||
    die "沒辦法把 $file 複製進資料庫的容器。什麼都沒動。"
  compose exec -T db pg_restore --file=/dev/null "$DUMP_IN_CONTAINER" >/dev/null 2>&1 ||
    die "$file 不是讀得回來的備份檔（pg_restore 讀不完）。什麼都沒動。換一份；備份在 $(backup_dir)。"
}

confirm() {
  local file="$1" answer
  info "要把專案 ${PROJECT} 正在用的資料庫換成這份備份："
  info "  ${file}（$(human_size "$(file_size "$file")")，$(format_mtime "$file")）"
  info "備份之後新增或修改的資料都會不見。${AFTER_RESTORE_NOTICE}"
  info "附件的檔案：把 $(backup_attachments_dir) 裡的檔案補回 $(attachments_dir)（只補缺的、不刪）。"
  info "備份之後才上傳的檔案會留在附件資料夾裡，但資料庫退回之後沒有它們的紀錄，成了沒人用的孤兒檔案（只佔空間）。"
  info "會先替現況備份一份（pre-restore），過程中 api 與 backup 會停一下。"
  if [[ "$ASSUME_YES" == true ]]; then
    return 0
  fi
  [[ -t 0 ]] || die "沒有終端機可以確認。確定要還原的話加上 --yes。什麼都沒動。"
  printf '確定的話，請輸入專案名稱 %s：' "$PROJECT"
  read -r answer || true
  [[ "$answer" == "$PROJECT" ]] || die "輸入的不是 ${PROJECT}，沒有還原。什麼都沒動。"
}

psql_postgres() {
  compose exec -T db psql -X -q -At -v ON_ERROR_STOP=1 -U "$DB_USER" -d postgres "$@"
}

database_exists() {
  [[ "$(psql_postgres -c "SELECT 1 FROM pg_database WHERE datname = '${DB_NAME}';")" == 1 ]]
}

# 上一次還原在「刪掉、還沒建回來」之間失敗時，資料庫不存在：沒有現況可以備份，跳過。
backup_current_state() {
  if ! database_exists; then
    info "資料庫 ${DB_NAME} 不存在（上一次還原中途失敗？），沒有現況可以備份，跳過 pre-restore。"
    return 0
  fi
  make_labeled_backup pre-restore
  PRE_RESTORE_BACKUP="$BACKUP_PATH"
  info "現況的備份：$PRE_RESTORE_BACKUP"
}

# backup 也要停：還原到一半時它做的排程備份會是不完整的資料，還會把好的舊備份擠掉。
# 沒有容器或已經停著的服務，compose stop 什麼都不做。
stop_services() {
  # shellcheck disable=SC2086 # 服務名稱要分成多個參數。
  compose stop $APP_SERVICES
}

# 用 up 而不是 start：還沒有容器（例如 backup 是這一版才加的）也起得來。
# --no-deps 不碰 db，--no-build 不建置：還原只換資料，不換版本。
start_services() {
  # shellcheck disable=SC2086
  compose up -d --no-build --no-deps $APP_SERVICES
}

# 在一個交易裡還原：失敗的話整個撤銷，資料庫是空的，不會留下一半的資料。
# --no-privileges：備份裡的權限（GRANT、預設權限）不還原。授予執行用的角色的權限由 api 啟動時的遷移重新給，
# 和備份是哪一版做的、當時有沒有 pmtool_app 都無關；備份裡指名的角色不存在時，GRANT 還會讓整個還原失敗。
restore_dump() {
  compose exec -T db pg_restore --exit-on-error --single-transaction --no-owner --no-privileges \
    -U "$DB_USER" -d "$DB_NAME" "$DUMP_IN_CONTAINER"
}

psql_restored() {
  compose exec -T db psql -X -q -At -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}

revoke_sessions() {
  local exists revoked
  exists="$(psql_restored -c "$SESSIONS_TABLE_EXISTS_SQL")"
  if [[ "$exists" != t ]]; then
    info "這份備份沒有 identity.sessions（比較舊的版本做的），不用作廢。"
    return 0
  fi
  revoked="$(psql_restored -c "$REVOKE_SESSIONS_SQL")"
  info "作廢了 ${revoked} 個還有效的工作階段。"
}

# 資料庫換成備份的樣子之後，附件的檔案也要在：備份裡的檔案（backups/attachments/）補回附件資料夾，只補缺的、不覆蓋、不刪。
# 備份裡沒有 attachments/（這一套還沒有備份過附件）就不用做。
restore_attachments() {
  local src
  src="$(backup_attachments_dir)"
  if [[ ! -d "$src" ]]; then
    info "備份目錄裡還沒有 attachments/，沒有附件要補。"
    return 0
  fi
  ensure_attachments_dir
  copy_attachments "$src" "$(attachments_dir)" ||
    die "附件沒有補回去（見上面的訊息）。"
  info "補回了 ${COPIED_COUNT} 個附件檔案到 $(attachments_dir)。"
}

main() {
  local file="" url
  ASSUME_YES=false
  while (($# > 0)); do
    case "$1" in
      --yes) ASSUME_YES=true ;;
      -h | --help) usage; return 0 ;;
      -*) usage >&2; die "不認得的參數「$1」。" ;;
      *) [[ -z "$file" ]] || die "只能給一份備份檔。"; file="$1" ;;
    esac
    shift
  done
  [[ -n "$file" ]] || { usage >&2; die "要給一份備份檔。備份在 $(backup_dir)，最新的幾份：ls -t \"$(backup_dir)\" | head"; }
  [[ -f "$file" && -r "$file" ]] || die "找不到備份檔，或沒有權限讀它：$file"
  file="$(cd "$(dirname "$file")" && pwd -P)/$(basename "$file")"
  require_setup
  claim_backup_dir
  service_running db || die "資料庫（專案 ${PROJECT} 的 db）沒有在跑。先把它叫起來：$(compose_hint) up -d --no-build db"
  trap on_exit EXIT
  copy_and_check_dump "$file"
  confirm "$file"

  STAGE=backing-up
  step "替現況備份（pre-restore）"
  backup_current_state

  STAGE=stopping
  step "停下 api 與 backup"
  stop_services

  STAGE=dropping
  step "刪掉並重建資料庫 ${DB_NAME}"
  psql_postgres -c "DROP DATABASE IF EXISTS ${DB_NAME} WITH (FORCE);"
  STAGE=creating
  psql_postgres -c "CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};"

  STAGE=restoring
  step "還原"
  restore_dump
  remove_copied_dump

  STAGE=revoking
  step "作廢所有的工作階段"
  revoke_sessions

  STAGE=attachments
  step "把備份裡的附件補回附件資料夾"
  restore_attachments

  STAGE=starting
  step "啟動 api 與 backup"
  start_services

  STAGE=waiting
  step "等 api 就緒（最多 ${READY_TIMEOUT_SECONDS} 秒）"
  url="$(api_url)"
  wait_ready "$url/health/ready" "$READY_TIMEOUT_SECONDS" || die "api ${READY_TIMEOUT_SECONDS} 秒內沒有就緒。"

  STAGE=finished
  info ""
  info "還原完成：資料回到 $(basename "$file") 的樣子。"
  info "$AFTER_RESTORE_NOTICE"
  info "還原前的資料在：${PRE_RESTORE_BACKUP:-（開始之前資料庫不存在，沒有做 pre-restore 備份）}"
  info "看狀態：bash scripts/ops/status.sh"
}

# 和 main 寫在同一行：執行中這個檔案被改寫時，bash 不會接著讀到改了一半的內容。
main "$@"; exit $?
