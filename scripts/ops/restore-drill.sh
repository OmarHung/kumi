#!/bin/bash
# 還原演練：把一份備份還原到另外起的、用完就丟的 PostgreSQL，列出每張表的筆數。不碰正在用的那一套。
# 用法：bash scripts/ops/restore-drill.sh [備份檔] [--with-api]    沒給備份檔時用最新的一份
#       bash scripts/ops/restore-drill.sh --down                  清掉 --with-api 留著的演練
#   --with-api  再用現有的 API 映像檔接上演練的資料庫，開在 http://127.0.0.1:${DRILL_PORT:-8081}，
#               可以登入看資料，留著直到 --down。沒帶時跑完就清掉。
#               演練的 api 用自己的一份附件資料夾（暫存目錄，從備份目錄的 attachments/ 複製來的，--down 時刪掉），
#               不碰正在用的附件資料夾。
#               要在演練裡試寄信（Mailpit 在演練的網路上，是私有網段）：加 DRILL_EMAIL_ALLOW_PRIVATE_HOSTS=true。
#               附件的容量上限照 .env 的 ATTACHMENT_QUOTA_*（沒設就是預設值）。
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# 演練用的東西都帶專案名稱當前綴，--down 只清掉這幾個名字，不會動到別的容器。
readonly DRILL_PREFIX="${PROJECT}-drill"
readonly DRILL_NETWORK="${DRILL_PREFIX}"
readonly DRILL_DB="${DRILL_PREFIX}-db"
readonly DRILL_API="${DRILL_PREFIX}-api"
readonly API_IMAGE="${PROJECT}-api"
readonly DB_START_TIMEOUT_SECONDS=90
readonly DUMP_IN_CONTAINER=/tmp/pmtool-drill.dump

# 演練的 api 用的附件資料夾：固定的暫存路徑（帶專案名稱），--down 才找得到、刪得掉。
drill_attachments_dir() {
  printf '%s/%s-attachments' "${TMPDIR:-/tmp}" "$DRILL_PREFIX" | sed 's#//#/#g'
}

# 檔案的擁有者是 api 的使用者（Linux 上主機的使用者刪不掉），所以用一次性的容器刪。
remove_drill_attachments() {
  local dir image
  dir="$(drill_attachments_dir)"
  [[ -d "$dir" ]] || return 1
  rm -rf -- "$dir" 2>/dev/null || true
  if [[ -d "$dir" ]]; then
    image="$(postgres_image)" || return 1
    docker run --rm --user 0 -v "$dir:/d" --entrypoint bash "$image" -c 'find /d -mindepth 1 -delete' >/dev/null 2>&1 || true
    rmdir -- "$dir" 2>/dev/null || true
  fi
  [[ ! -d "$dir" ]]
}

# 每張表的筆數。用 query_to_xml 在一個查詢裡數完所有的表，不用先產生 SQL 再執行。
readonly COUNT_SQL="
SELECT n.nspname || '.' || c.relname,
       (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', n.nspname, c.relname), false, true, '')))[1]::text
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
  AND n.nspname NOT LIKE 'pg_toast%'
ORDER BY 1;"

usage() {
  sed -n '2,6p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

container_exists() {
  docker container inspect "$1" >/dev/null 2>&1
}

network_exists() {
  docker network inspect "$1" >/dev/null 2>&1
}

# -v：PostgreSQL 的映像檔宣告了資料卷，不一起刪的話每次演練都會留下一個沒有名字的資料卷。
tear_down() {
  local removed=""
  for name in "$DRILL_API" "$DRILL_DB"; do
    if container_exists "$name"; then
      docker rm -f -v "$name" >/dev/null && removed="${removed} ${name}"
    fi
  done
  if network_exists "$DRILL_NETWORK"; then
    docker network rm "$DRILL_NETWORK" >/dev/null && removed="${removed} ${DRILL_NETWORK}（網路）"
  fi
  if remove_drill_attachments; then
    removed="${removed} 附件的暫存資料夾"
  fi
  if [[ -n "$removed" ]]; then
    info "已清掉演練：${removed# }"
  else
    info "沒有演練在跑。"
  fi
}

port_in_use() {
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

newest_backup() {
  local newest
  newest="$(list_backups all | head -n 1 || true)"
  [[ -n "$newest" ]] || die "$(backup_dir) 裡沒有備份。先做一份：bash scripts/ops/backup-now.sh"
  printf '%s/%s' "$(backup_dir)" "${newest#*$'\t'}"
}

start_db() {
  local deadline
  docker network create --label "pmtool.drill=${PROJECT}" "$DRILL_NETWORK" >/dev/null
  DRILL_DB_PASSWORD="$(random_secret)"
  # 密碼放在子 shell 的環境變數裡，用「-e 名稱」帶進去：不會出現在指令列（ps 看得到）上，
  # 也不會影響之後的 compose 指令（compose 讀設定時 shell 的環境變數優先）。
  (
    export POSTGRES_PASSWORD="$DRILL_DB_PASSWORD"
    docker run -d --name "$DRILL_DB" --network "$DRILL_NETWORK" --label "pmtool.drill=${PROJECT}" \
      -e POSTGRES_DB="$DB_NAME" -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD \
      --log-opt max-size=10m --log-opt max-file=5 \
      "$DRILL_DB_IMAGE" >/dev/null
  )
  # 用 TCP 問：映像檔初始化時會先起一個只聽 Unix socket 的暫時伺服器，用 socket 問會太早說好了。
  deadline=$((SECONDS + DB_START_TIMEOUT_SECONDS))
  until docker exec "$DRILL_DB" pg_isready -q -h 127.0.0.1 -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; do
    ((SECONDS < deadline)) || die "演練的資料庫 $DB_START_TIMEOUT_SECONDS 秒內沒有就緒。看記錄：docker logs --tail 50 ${DRILL_DB}"
    sleep 1
  done
}

# --no-privileges：演練的資料庫沒有執行用的角色 pmtool_app，備份裡授予它的權限（GRANT）會讓還原失敗。
# 演練的 api 用超級使用者連線，用不到那些權限。
restore_into_drill() {
  local file="$1"
  docker cp "$file" "${DRILL_DB}:${DUMP_IN_CONTAINER}" >/dev/null
  if ! docker exec "$DRILL_DB" pg_restore --exit-on-error --single-transaction --no-owner --no-privileges \
    -U "$DB_USER" -d "$DB_NAME" "$DUMP_IN_CONTAINER"; then
    die "這份備份還原失敗（上面是 pg_restore 的訊息）。它可能不完整或壞掉了：換一份備份再演練一次。"
  fi
  docker exec "$DRILL_DB" rm -f "$DUMP_IN_CONTAINER"
}

show_counts() {
  local rows table count tables=0 total=0
  rows="$(docker exec "$DRILL_DB" psql -X -At -F $'\t' -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" -c "$COUNT_SQL")"
  info "每張表的筆數："
  while IFS=$'\t' read -r table count; do
    [[ -n "$table" ]] || continue
    printf '  %-40s %s\n' "$table" "$count"
    tables=$((tables + 1))
    total=$((total + count))
  done <<<"$rows"
  ((tables > 0)) || die "還原之後一張表都沒有：這份備份是空的資料庫做的嗎？換一份備份再演練一次。"
  info "共 $tables 張表、$total 筆。"
}

# 演練自己的附件資料夾：從備份目錄的 attachments/ 複製（備份裡的檔案是這份備份當時與之前的全部附件，之後新增的也可能在）。
# 備份目錄還沒有 attachments/ 時是空的資料夾，演練的 api 照樣起得來。
prepare_drill_attachments() {
  local dir src
  dir="$(drill_attachments_dir)"
  src="$(backup_attachments_dir)"
  mkdir -p "$dir"
  if [[ -d "$src" ]]; then
    copy_attachments "$src" "$dir" || die "備份裡的附件沒有複製成功（見上面的訊息）。演練的容器會清掉。"
    info "演練的附件資料夾：${dir}（從 ${src} 複製了 ${COPIED_COUNT} 個檔案）"
  else
    # 空的資料夾也要讓 api 的使用者寫得進去。
    docker run --rm --user 0 -v "$dir:/d" --entrypoint bash "$DRILL_DB_IMAGE" -c "chown ${APP_UID}:${APP_UID} /d && chmod 700 /d" >/dev/null 2>&1 || true
    info "備份目錄裡還沒有 attachments/：演練的附件資料夾是空的（${dir}）。"
  fi
}

start_api() {
  local port="$1" url
  local tz allow_private_email quota_organization quota_person
  tz="$(env_get TZ Asia/Taipei)"
  # 預設不照 .env 的 EMAIL_ALLOW_PRIVATE_HOSTS：演練裡要連內網的寄信主機得另外明講，免得演練的 api 連到真的轉信主機。
  allow_private_email="$(env_get DRILL_EMAIL_ALLOW_PRIVATE_HOSTS false)"
  [[ "$allow_private_email" == "true" || "$allow_private_email" == "false" ]] || die "DRILL_EMAIL_ALLOW_PRIVATE_HOSTS 要是 true 或 false，現在是「${allow_private_email}」。"
  quota_organization="$(env_get ATTACHMENT_QUOTA_ORGANIZATION_GIB 20)"
  quota_person="$(env_get ATTACHMENT_QUOTA_PERSON_DAILY_GIB 2)"
  # 連線字串裡有密碼，一樣在子 shell 裡用「-e 名稱」帶進去。
  # 兩個連線都用演練資料庫的超級使用者、不設 Database__RuntimeRole：演練的資料庫沒有 pmtool_app，也不需要。
  # 不設第一個擁有者（Bootstrap:*）：備份裡已經有帳號，用原本的帳號登入。
  # 還原出來的資料裡有寄信設定，背景的寄信工作會照著寄：演練的 api 一律把它關掉。
  # 記錄格式與工作階段的金鑰都不用設：JSON 記錄是正式環境的預設，金鑰存在（演練的）資料庫裡。
  (
    export ConnectionStrings__Database="Host=${DRILL_DB};Port=5432;Database=${DB_NAME};Username=${DB_USER};Password=${DRILL_DB_PASSWORD}"
    export ConnectionStrings__Migrations="$ConnectionStrings__Database"
    docker run -d --name "$DRILL_API" --network "$DRILL_NETWORK" --label "pmtool.drill=${PROJECT}" \
      -p "127.0.0.1:${port}:8080" \
      -e ASPNETCORE_ENVIRONMENT=Production \
      -e ConnectionStrings__Database \
      -e ConnectionStrings__Migrations \
      -e Database__MigrateOnStartup=true \
      -e Notifications__Email__Worker__Enabled=false \
      -e "Notifications__Email__AllowPrivateHosts=${allow_private_email}" \
      -e "Attachments__Quota__OrganizationGiB=${quota_organization}" \
      -e "Attachments__Quota__PersonDailyGiB=${quota_person}" \
      -e Attachments__Provider=local \
      -e Attachments__Directory=/data/attachments \
      -v "$(drill_attachments_dir):/data/attachments" \
      -e "TZ=${tz}" \
      --log-opt max-size=10m --log-opt max-file=5 \
      "$API_IMAGE" >/dev/null
  )
  url="http://127.0.0.1:${port}"
  info "等演練的 api 就緒（最多 $READY_TIMEOUT_SECONDS 秒）……"
  if ! wait_ready "$url/health/ready" "$READY_TIMEOUT_SECONDS"; then
    printf '錯誤：演練的 api 沒有就緒。下面是它最後 50 行記錄：\n' >&2
    docker logs --tail 50 "$DRILL_API" >&2 || true
    die "演練失敗。演練的容器會清掉。"
  fi
  DRILL_URL="$url"
}

main() {
  local file="" with_api=false down=false port
  while (($# > 0)); do
    case "$1" in
      --with-api) with_api=true ;;
      --down) down=true ;;
      -h | --help) usage; return 0 ;;
      -*) usage >&2; die "不認得的參數「$1」。" ;;
      *) [[ -z "$file" ]] || die "只能給一份備份檔。"; file="$1" ;;
    esac
    shift
  done
  require_docker
  if $down; then
    tear_down
    return 0
  fi
  require_env_file
  claim_backup_dir

  if container_exists "$DRILL_DB" || container_exists "$DRILL_API" || network_exists "$DRILL_NETWORK"; then
    die "已經有一套演練（${DRILL_PREFIX}）。先清掉它：bash scripts/ops/restore-drill.sh --down"
  fi
  # newest_backup 失敗時只結束它自己的子 shell，要在這裡接住，不然會接著印第二則錯誤。
  if [[ -z "$file" ]]; then
    file="$(newest_backup)" || exit 1
  fi
  [[ -f "$file" && -r "$file" ]] || die "找不到備份檔，或沒有權限讀它：$file"
  # postgres_image 失敗時只結束它自己的子 shell，一樣要在這裡接住。
  DRILL_DB_IMAGE="$(postgres_image)" || exit 1
  port="$(env_get DRILL_PORT 8081)"
  [[ "$port" =~ ^[1-9][0-9]{0,4}$ ]] || die "DRILL_PORT 要是連接埠號碼，現在是「${port}」。"
  if $with_api; then
    docker image inspect "$API_IMAGE" >/dev/null 2>&1 ||
      die "找不到 API 的映像檔 ${API_IMAGE}。先建置一次：bash scripts/ops/deploy-local.sh（或 $(compose_hint) build api）。"
    ! port_in_use "$port" || die "連接埠 $port 已經有程式在用。換一個：DRILL_PORT=8099 bash scripts/ops/restore-drill.sh --with-api"
  fi

  # 任何一步失敗（或按 Ctrl-C）都把演練的東西清掉；--with-api 成功時才留著。
  trap 'status=$?; if ((status != 0)); then tear_down >&2; fi' EXIT
  trap 'exit 130' INT TERM
  info "演練用的備份：${file}（$(human_size "$(file_size "$file")")）"
  info "起一個用完就丟的資料庫（${DRILL_DB}，不開連接埠）……"
  start_db
  info "還原中……"
  restore_into_drill "$file"
  show_counts

  if ! $with_api; then
    trap - EXIT
    tear_down
    info "演練成功：這份備份讀得回來。"
    return 0
  fi
  prepare_drill_attachments
  start_api "$port"
  trap - EXIT
  info ""
  info "演練的網頁：${DRILL_URL}"
  info "用原本的帳號登入（只看，不要在這裡改資料：演練的資料用完就丟）。"
  info "請用 127.0.0.1 開，不要用 localhost：Cookie 不分連接埠，用 localhost 登入會把 localhost:$(env_get PMTOOL_PORT 8080) 那一套的登入蓋掉。"
  info "看完清掉：bash scripts/ops/restore-drill.sh --down"
}

# 和 main 寫在同一行：執行中這個檔案被改寫時，bash 不會接著讀到改了一半的內容。
main "$@"; exit $?
