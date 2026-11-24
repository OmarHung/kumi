#!/bin/bash
# 用目前的程式碼更新本機的那一套：先備份（標籤 pre-deploy），確保執行用的角色，再 docker compose up --build -d，然後等就緒。
# 附件的資料夾（ATTACHMENTS_DIR，預設 data/attachments）不存在就建立，並確認 api 的使用者寫得進去。
# 用法：bash scripts/ops/deploy-local.sh [--no-build]
#   --no-build  不建置映像檔，用現有的（只改了 compose.yaml 或 .env 時）
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

readonly DB_START_TIMEOUT_SECONDS=120
# 失敗時列出幾份最近的 pre-deploy 備份，讓人挑對的那一份。
readonly RECENT_PRE_DEPLOY_COUNT=5

usage() {
  sed -n '2,4p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# 部署前的備份。有沒有資料要看資料卷，不能看容器：docker compose down（不帶 -v）會刪掉容器，資料卷還在。
# 資料卷在、資料庫沒在跑時，先只起 db 做備份，新版的遷移才不會在沒有備份的真實資料上跑。
backup_before_deploy() {
  step "部署前的備份"
  BACKUP_FAILURE_NOTE="沒有更新：服務和資料都和執行之前一樣。"
  if ! service_running db; then
    if ! db_volume_exists; then
      info "還沒有資料庫的資料卷（${PROJECT}_db-data，第一次部署），跳過部署前的備份。"
      return 0
    fi
    info "資料庫沒有在跑，但資料卷 ${PROJECT}_db-data 還在：先只啟動資料庫，備份之後才部署。"
    compose up -d --no-build db
    wait_db_healthy "$DB_START_TIMEOUT_SECONDS" ||
      die "資料庫 $DB_START_TIMEOUT_SECONDS 秒內沒有就緒，沒有更新（資料庫已經被啟動了，其他服務沒動）。看記錄：$(compose_hint) logs --tail 50 db"
    BACKUP_FAILURE_NOTE="沒有更新：資料庫原本停著，現在被啟動了；其他服務與資料都沒動。"
  fi
  make_labeled_backup pre-deploy
  PRE_DEPLOY_BACKUP="$BACKUP_PATH"
  info "已備份：$PRE_DEPLOY_BACKUP"
}

# 全新的與既有的資料庫都一樣：每次部署都用同一段 SQL（deploy/postgres/app-role.psql）確保執行用的角色在、
# 密碼和 env 檔一致（改了密碼，這裡就更新）。可以重跑。
# 先只啟動（或換新）資料庫、等它就緒，再設角色，最後才換 api：api 不會在角色還沒好的時候用它連線。
ensure_app_role() {
  local password
  step "資料庫與執行用的角色（${APP_DB_USER}）"
  compose up -d --no-build db ||
    die "資料庫起不來（見上面的訊息），api 沒有更新。資料在部署前的備份裡：${PRE_DEPLOY_BACKUP:-（第一次部署，沒有備份）}"
  wait_db_healthy "$DB_START_TIMEOUT_SECONDS" ||
    die "資料庫 $DB_START_TIMEOUT_SECONDS 秒內沒有就緒，api 沒有更新。看記錄：$(compose_hint) logs --tail 50 db"
  [[ -r "$APP_ROLE_SQL" ]] || die "讀不到 ${APP_ROLE_SQL}，api 沒有更新。"
  password="$(env_get POSTGRES_APP_PASSWORD)"
  # 密碼經由標準輸入交給 psql（printf 是 bash 的內建指令），不出現在任何指令列上。
  { printf '\\set app_password %s\n' "$password"; cat "$APP_ROLE_SQL"; } |
    compose exec -T db psql -X -q -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" >/dev/null ||
    die "沒辦法在資料庫裡建立或更新角色 ${APP_DB_USER}（見上面的訊息），api 沒有更新。"
  info "角色 ${APP_DB_USER} 已經在資料庫裡，密碼和 ${ENV_FILE} 一致。"
}

# 最近幾份 pre-deploy 備份，新的在前，每行「時間  路徑」，這一次做的那份加註。
list_recent_pre_deploys() {
  local all name shown=0 path note
  all="$(list_backups all)"
  while IFS=$'\t' read -r _ name; do
    [[ "$name" == *-pre-deploy.dump ]] || continue
    path="$(backup_dir)/$name"
    note=""
    [[ "$path" != "$PRE_DEPLOY_BACKUP" ]] || note="  ← 這一次部署前做的"
    printf '     %s  %s%s\n' "$(format_mtime "$path")" "$path" "$note"
    shown=$((shown + 1))
    ((shown < RECENT_PRE_DEPLOY_COUNT)) || break
  done <<<"$all"
  ((shown > 0)) || printf '     （沒有 pre-deploy 備份）\n'
}

explain_failure() {
  local url="$1"
  printf '\n' >&2
  printf '錯誤：api %s 秒內沒有就緒（%s）。下面是 api 最後 50 行記錄：\n' "$READY_TIMEOUT_SECONDS" "$url/health/ready" >&2
  compose logs --no-color --tail 50 api >&2 || true
  cat >&2 <<EOF

接下來可以這樣做：
1. 看上面的記錄找原因。只是比較慢的話，等一下再跑 bash scripts/ops/status.sh。
2. 要退回上一版：先找出上一個能用的提交（git log --oneline -5），
   切過去（git switch --detach <提交>），再跑一次 bash scripts/ops/deploy-local.sh。
3. 如果退回之後還是起不來（新版的遷移已經改了資料表），把資料換回「往前更新」之前的樣子。
   要選的是從舊版更新到新版的那一次、這支腳本一開始印的那份 pre-deploy 備份。
   退回舊版的那次部署也會做一份，可是那時資料表已經被新版改過了，不要選它。
   最近的 pre-deploy 備份（新的在前）：
EOF
  list_recent_pre_deploys >&2
  cat >&2 <<EOF
   選好之後：bash scripts/ops/restore.sh <那一份的路徑>
EOF
  exit 1
}

main() {
  local build_flag=--build url
  case "${1:-}" in
    "") ;;
    --no-build) build_flag=--no-build ;;
    -h | --help) usage; return 0 ;;
    *) usage >&2; die "不認得的參數「$1」。" ;;
  esac
  require_setup
  claim_backup_dir
  # 附件的資料夾要在 api 與 backup 起來之前建好、api 的使用者寫得進去（第一次部署會建立 data/attachments）。
  ensure_attachments_dir
  PRE_DEPLOY_BACKUP=""

  info "專案 ${PROJECT}，設定檔 $ENV_FILE"
  if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    info "要部署的版本：$(git -C "$ROOT" log -1 --format='%h %s')"
  fi

  backup_before_deploy
  ensure_app_role

  step "啟動（docker compose up $build_flag -d）"
  compose up "$build_flag" -d || die "docker compose up 失敗（見上面的訊息），服務可能停在一半。修好之後再跑一次這支腳本；資料在部署前的備份裡：${PRE_DEPLOY_BACKUP:-（第一次部署，沒有備份）}"
  # backup 的腳本是從 repo 掛進去的，設定沒變時 up 不會重建它，跑著的迴圈還是舊版的腳本。重新啟動才會換成新版。
  # 它啟動時只檢查一次，最新的排程備份還不到 BACKUP_INTERVAL_HOURS 就不會多做一份。
  compose restart backup >/dev/null 2>&1 || info "（backup 服務沒有重新啟動成功，之後用 bash scripts/ops/status.sh 確認它在跑。）"

  step "等 api 就緒（最多 $READY_TIMEOUT_SECONDS 秒）"
  url="$(api_url)"
  wait_ready "$url/health/ready" "$READY_TIMEOUT_SECONDS" || explain_failure "$url"
  info "完成：$url"
  info "看狀態：bash scripts/ops/status.sh"
}

# 和 main 寫在同一行：執行中這個檔案被改寫時，bash 不會接著讀到改了一半的內容。
main "$@"; exit $?
