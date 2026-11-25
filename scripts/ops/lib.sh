#!/bin/bash
# scripts/ops/ 底下在主機上跑的腳本共用的函式。由各腳本用 source 載入，不單獨執行。
#
# 認這兩個環境變數，才能用同一份設定另外起一套隔離的環境（測試、演練）：
#   COMPOSE_PROJECT_NAME  compose 的專案名稱，預設 pm-tool
#   PMTOOL_ENV_FILE       env 檔，預設 repo 根目錄的 .env

# 用這支檔案自己的位置找 repo 根目錄：在 repo 的哪個目錄下執行都一樣，也不依賴 git。
OPS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT="$(cd "$OPS_DIR/../.." && pwd -P)"
PROJECT="${COMPOSE_PROJECT_NAME:-pm-tool}"
ENV_FILE="${PMTOOL_ENV_FILE:-$ROOT/.env}"
readonly OPS_DIR ROOT PROJECT ENV_FILE

# compose.yaml 裡寫死的資料庫名稱與使用者。這幾個常數給載入這個檔案的腳本用。
# shellcheck disable=SC2034
readonly DB_NAME=pmtool
# 資料表的擁有者：遷移、備份、還原用它。
# shellcheck disable=SC2034
readonly DB_USER=pmtool
# 執行用的角色（api 平常用它連線）。名稱也寫在 compose.yaml 與 app-role.psql 裡，三個地方要一樣。
# shellcheck disable=SC2034
readonly APP_DB_USER=pmtool_app
# 建立執行用的角色、設定它的密碼的 SQL。deploy-local.sh 每次部署都跑（全新的與既有的資料庫都一樣）。
# shellcheck disable=SC2034
readonly APP_ROLE_SQL="$ROOT/deploy/postgres/app-role.psql"
readonly LABEL_PATTERN='^[a-z0-9][a-z0-9-]{0,31}$'
# shellcheck disable=SC2034
readonly READY_TIMEOUT_SECONDS=180

die() {
  printf '錯誤：%s\n' "$*" >&2
  exit 1
}

info() {
  printf '%s\n' "$*"
}

step() {
  printf '\n== %s\n' "$*"
}

require_docker() {
  command -v docker >/dev/null 2>&1 || die "找不到 docker 指令。先安裝 Docker Desktop。"
  docker info >/dev/null 2>&1 || die "Docker 沒有在跑。打開 Docker Desktop，等它顯示 running 之後再試一次。"
}

require_env_file() {
  [[ -f "$ENV_FILE" ]] || die "找不到設定檔 ${ENV_FILE}。先執行 cp .env.example .env，填上 POSTGRES_PASSWORD、POSTGRES_APP_PASSWORD 與 BOOTSTRAP_ADMIN_PASSWORD（見 docs/OPERATIONS.md）。"
}

# 執行用的角色的密碼。compose.yaml 少了它就不肯執行任何指令（連 docker compose ps 都不行），
# 所以用到 compose 的腳本一開始就檢查，說明怎麼補，不自己改 env 檔。
require_app_password() {
  local password
  password="$(env_get POSTGRES_APP_PASSWORD)"
  if [[ -z "$password" ]]; then
    cat >&2 <<EOF
錯誤：設定檔 ${ENV_FILE} 少了 POSTGRES_APP_PASSWORD。什麼都沒動。
api 改用執行用的角色 ${APP_DB_USER} 連資料庫（只能讀寫資料，改不了資料表結構），這是它的密碼。
1. 產生一組密碼：openssl rand -hex 24
2. 用編輯器打開 ${ENV_FILE}，加一行：POSTGRES_APP_PASSWORD=<剛產生的那一串>
3. 更新：bash scripts/ops/deploy-local.sh（它會在資料庫裡建立 ${APP_DB_USER}、設定這組密碼）
說明在 docs/OPERATIONS.md 第 2 節。
EOF
    exit 1
  fi
  [[ "$password" =~ ^[A-Za-z0-9]+$ ]] ||
    die "${ENV_FILE} 的 POSTGRES_APP_PASSWORD 只能用英文字母與數字：它會被放進連線字串。換一組（openssl rand -hex 24），再試一次。什麼都沒動。"
}

require_setup() {
  require_docker
  require_env_file
  require_app_password
}

# 讀一個設定值。和 compose 一樣：shell 的環境變數優先，其次是 env 檔，都沒有就用預設值。
# shell 裡設成空值（例如 DRILL_PORT=）當成沒設，和 compose.yaml 的 ${名稱:-預設} 一致。
# 不用 source 讀 env 檔：compose 的 env 檔不是 bash，值裡有空白時 source 會出錯。
env_get() {
  local key="$1" default="${2:-}" line value
  if [[ -n "${!key:-}" ]]; then
    printf '%s' "${!key}"
    return 0
  fi
  line="$(grep -E "^[[:space:]]*(export[[:space:]]+)?${key}=" "$ENV_FILE" 2>/dev/null | tail -n 1 || true)"
  value="${line#*=}"
  value="${value%$'\r'}"
  if [[ "$value" =~ ^\"(.*)\"$ || "$value" =~ ^\'(.*)\'$ ]]; then
    value="${BASH_REMATCH[1]}"
  else
    value="${value%% #*}"
    value="${value%"${value##*[![:space:]]}"}"
  fi
  printf '%s' "${value:-$default}"
}

compose() {
  docker compose \
    --project-directory "$ROOT" \
    --file "$ROOT/compose.yaml" \
    --project-name "$PROJECT" \
    --env-file "$ENV_FILE" \
    "$@"
}

# 印在說明裡、使用者可以直接複製的 compose 指令開頭（在 repo 根目錄執行）。
compose_hint() {
  if [[ "$PROJECT" == pm-tool && "$ENV_FILE" == "$ROOT/.env" ]]; then
    printf 'docker compose'
  else
    printf 'docker compose -p %s --env-file "%s"' "$PROJECT" "$ENV_FILE"
  fi
}

# 備份目錄在主機上的絕對路徑。和 compose.yaml 一樣，相對路徑從 repo 根目錄算起。
backup_dir() {
  local dir
  dir="$(env_get BACKUP_DIR ./backups)"
  case "$dir" in
    /*) printf '%s' "$dir" ;;
    *) printf '%s/%s' "$ROOT" "${dir#./}" ;;
  esac
}

# 附件資料夾在主機上的絕對路徑（compose.yaml 的 ATTACHMENTS_DIR）。和備份目錄一樣，相對路徑從 repo 根目錄算起。
attachments_dir() {
  local dir
  dir="$(env_get ATTACHMENTS_DIR ./data/attachments)"
  case "$dir" in
    /*) printf '%s' "$dir" ;;
    *) printf '%s/%s' "$ROOT" "${dir#./}" ;;
  esac
}

# 備份目錄裡放附件的子資料夾，和標記備份結果的檔案。backup-loop.sh 用同樣的名字。
backup_attachments_dir() {
  printf '%s/attachments' "$(backup_dir)"
}
readonly ATTACHMENTS_STATE_FILE=.pmtool-attachments-status

# api 容器的使用者（Dockerfile 的 APP_UID）：附件資料夾要讓它寫得進去。
readonly APP_UID=1654
# 附件裡 api 自己的暫存檔（開頭是 . 或結尾是 .tmp、.partial）：不算檔案，也不備份。scripts/ops/copy-missing.sh 用同樣的規則。
readonly ATTACHMENT_FILE_FILTER=(-type f ! -name '.*' ! -name '*.tmp' ! -name '*.partial')

service_running() {
  compose ps --status running --services 2>/dev/null | grep -qx "$1"
}

service_exists() {
  [[ -n "$(compose ps --all --quiet "$1" 2>/dev/null)" ]]
}

# 資料庫的資料卷在不在。docker compose down（不帶 -v）會刪掉容器，但資料卷和裡面的資料都還在，
# 所以「有沒有資料」要看資料卷，不能看容器。名稱是 compose 的慣例：<專案>_<資料卷>。
db_volume_exists() {
  docker volume inspect "${PROJECT}_db-data" >/dev/null 2>&1
}

# 備份目錄的標記檔：寫著這個目錄屬於哪個 compose 專案。backup-loop.sh 用同一個檔名。
readonly BACKUP_MARKER=.pmtool-project

# 備份目錄標記的專案名稱；目錄或標記不存在時什麼都不印。
backup_dir_owner() {
  local marker owner=""
  marker="$(backup_dir)/$BACKUP_MARKER"
  [[ -f "$marker" ]] || return 0
  IFS= read -r owner <"$marker" || true
  printf '%s' "$owner"
}

backup_dir_conflict_message() {
  printf '備份目錄 %s 是專案「%s」的，這一套是「%s」。兩套共用一個備份目錄，備份會混在一起、互相清掉，還原時也可能拿到另一套的備份。把這一套 env 檔（%s）的 BACKUP_DIR 換成自己的目錄（見 docs/OPERATIONS.md 第 9 節）。' \
    "$(backup_dir)" "$1" "$PROJECT" "$ENV_FILE"
}

# 確認備份目錄是這一套的：沒有標記時寫上自己的專案名稱，是別的專案的就結束整支腳本。
# 目錄還不存在時不建立：backup 服務第一次啟動時會建立並標記它。
claim_backup_dir() {
  local dir owner
  dir="$(backup_dir)"
  [[ -d "$dir" ]] || return 0
  owner="$(backup_dir_owner)"
  if [[ -z "$owner" ]]; then
    (umask 077 && printf '%s\n' "$PROJECT" >"$dir/$BACKUP_MARKER") ||
      die "寫不進備份目錄的標記檔 $dir/${BACKUP_MARKER}。確認這個目錄的權限。"
    return 0
  fi
  [[ "$owner" == "$PROJECT" ]] || die "$(backup_dir_conflict_message "$owner")"
}

# macOS 的 stat 和 Linux 的參數不一樣。
file_mtime() {
  if [[ "$(uname)" == Darwin ]]; then
    stat -f %m "$1"
  else
    stat -c %Y "$1"
  fi
}

format_mtime() {
  if [[ "$(uname)" == Darwin ]]; then
    date -r "$(stat -f %m "$1")" '+%Y-%m-%d %H:%M'
  else
    date -r "$1" '+%Y-%m-%d %H:%M'
  fi
}

file_size() {
  if [[ "$(uname)" == Darwin ]]; then
    stat -f %z "$1"
  else
    stat -c %s "$1"
  fi
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

# 備份目錄裡的備份，新的在前：每行「修改時間（秒）<tab>檔名」。
# 只看排程備份時帶 scheduled；暫存檔（.pmtool-*.partial）一律不算。
list_backups() {
  local which="${1:-all}" dir path name
  dir="$(backup_dir)"
  [[ -d "$dir" ]] || return 0
  for path in "$dir"/pmtool-*.dump; do
    [[ -f "$path" ]] || continue
    name="${path##*/}"
    if [[ "$which" == scheduled && ! "$name" =~ ^pmtool-[0-9]{8}-[0-9]{6}[.]dump$ ]]; then
      continue
    fi
    printf '%s\t%s\n' "$(file_mtime "$path")" "$name"
  done | sort -rn
}

# 做一份帶標籤的備份，把檔案的完整路徑放進 BACKUP_PATH。失敗時結束整支腳本；
# 呼叫端可以先設 BACKUP_FAILURE_NOTE，說明失敗時現況是什麼（例如「沒有更新」）。
# 用 backup 服務的設定另外跑一個一次性的容器（--no-deps：不會順便重建或啟動別的服務），
# 所以 backup 服務本身沒在跑、或是第一次部署還沒有它的容器時也能備份。
make_labeled_backup() {
  local label="$1" output name
  [[ "$label" =~ $LABEL_PATTERN ]] || die "標籤只能用小寫英文字母、數字與 -，最多 32 個字（例如 manual、before-import），現在是「${label}」。"
  service_running db || die "資料庫（專案 $PROJECT 的 db）沒有在跑，沒辦法備份。先執行 bash scripts/ops/deploy-local.sh，再看 bash scripts/ops/status.sh。"
  claim_backup_dir
  ensure_attachments_dir
  if ! output="$(compose --progress quiet run --rm --no-deps -T backup --once "$label")"; then
    die "備份失敗，上面是備份服務的訊息。${BACKUP_FAILURE_NOTE:-}用 bash scripts/ops/status.sh 確認資料庫的狀態，再試一次。"
  fi
  name="${output##*$'\n'}"
  BACKUP_PATH="$(backup_dir)/$name"
  [[ -n "$name" && -f "$BACKUP_PATH" ]] || die "備份服務說做好了「${name}」，可是在 $(backup_dir) 找不到。確認 env 檔的 BACKUP_DIR 和 backup 服務啟動時一樣。"
}

# 和 compose.yaml 的 db 用同一個映像檔（固定到 digest）：從它的 x-postgres-image 那一行讀，更新時只改一個地方。
# 附件的一次性容器（確認權限、複製）與演練的資料庫都用它：已經拉下來了，也有 GNU 的 find 與 cp。
postgres_image() {
  local line image
  line="$(grep -m 1 -E '^x-postgres-image:' "$ROOT/compose.yaml" || true)"
  image="${line##* }"
  [[ "$image" =~ ^postgres:[^@[:space:]]+@sha256:[0-9a-f]{64}$ ]] ||
    die "在 $ROOT/compose.yaml 找不到 PostgreSQL 的映像檔（x-postgres-image: &postgres-image postgres:<標籤>@sha256:<digest> 那一行）。"
  printf '%s' "$image"
}

# 用一次性的容器把「來源有、目的沒有」的附件複製過去（scripts/ops/copy-missing.sh：只補、不覆蓋、不刪），
# 新檔案的擁有者改成 api 的使用者。主機上不一定有 GNU 的 cp，Linux 上檔案的擁有者也要對，所以不在主機上直接複製。
# 用法：copy_attachments <來源資料夾> <目的資料夾>；成功時在 COPIED_COUNT 放複製了幾個。
copy_attachments() {
  local src="$1" dst="$2" image out
  image="$(postgres_image)" || exit 1
  mkdir -p "$dst"
  if ! out="$(docker run --rm --user 0 \
    -v "$src:/from:ro" -v "$dst:/to" -v "$OPS_DIR:/ops:ro" \
    --entrypoint bash "$image" /ops/copy-missing.sh /from /to "$APP_UID")"; then
    return 1
  fi
  COPIED_COUNT="${out##*copied=}"
  [[ "$COPIED_COUNT" =~ ^[0-9]+$ ]] || COPIED_COUNT=0
}

# 確認附件資料夾存在、api 的使用者（UID 1654）寫得進去；部署與備份之前呼叫。
# 不先建好的話，Docker 在 Linux 上會把不存在的掛載點建成 root 的，api 寫不進去。
# macOS 的 Docker Desktop 不管檔案的擁有者，通常一次就過；Linux 上資料夾要是 1654 的才寫得進去，這裡會改。
ensure_attachments_dir() {
  local dir image
  dir="$(attachments_dir)"
  if [[ "$PROJECT" != pm-tool && "$dir" == "$ROOT/data/attachments" ]]; then
    die "專案 ${PROJECT} 的 ATTACHMENTS_DIR（${ENV_FILE}）沒設，會沿用 8080 那一套的附件資料夾 ${dir}。換成自己的目錄（見 docs/OPERATIONS.md 第 9 節）。"
  fi
  mkdir -p "$dir" || die "建不了附件資料夾 ${dir}。確認上一層的權限，或改 ATTACHMENTS_DIR。"
  image="$(postgres_image)" || exit 1
  if attachments_dir_writable "$dir" "$image"; then
    return 0
  fi
  info "附件資料夾 ${dir} 對 api 的使用者（UID ${APP_UID}）不可寫，改它的擁有者……"
  docker run --rm --user 0 -v "$dir:/d" --entrypoint bash "$image" -c "chown ${APP_UID}:${APP_UID} /d && chmod 755 /d" >/dev/null 2>&1 || true
  attachments_dir_writable "$dir" "$image" ||
    die "附件資料夾 ${dir} 還是讓 UID ${APP_UID} 寫不進去（api 會啟動失敗）。macOS：確認 Docker Desktop 的檔案分享包含這個路徑；Linux：sudo chown ${APP_UID}:${APP_UID} \"${dir}\"。見 docs/OPERATIONS.md 第 3 節。"
}

attachments_dir_writable() {
  docker run --rm --user "${APP_UID}:${APP_UID}" -v "$1:/d" --entrypoint bash "$2" -c 'f=/d/.pmtool-write-test.$$; : >"$f" && rm -f -- "$f"' >/dev/null 2>&1
}

# api 對主機開的網址。先問 compose 實際的連接埠，問不到才用設定值。
api_url() {
  local published
  published="$(compose port api 8080 2>/dev/null || true)"
  if [[ -n "$published" ]]; then
    printf 'http://%s' "$published"
  else
    printf 'http://127.0.0.1:%s' "$(env_get PMTOOL_PORT 8080)"
  fi
}

# 等網址回 2xx，最多等指定的秒數。
wait_ready() {
  local url="$1" seconds="$2" deadline
  deadline=$((SECONDS + seconds))
  while ((SECONDS < deadline)); do
    if curl -fsS -m 3 -o /dev/null "$url" 2>/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

wait_db_healthy() {
  local seconds="$1" id status deadline
  deadline=$((SECONDS + seconds))
  while ((SECONDS < deadline)); do
    id="$(compose ps --quiet db 2>/dev/null || true)"
    if [[ -n "$id" ]]; then
      status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$id" 2>/dev/null || true)"
      [[ "$status" == healthy ]] && return 0
    fi
    sleep 2
  done
  return 1
}

# 產生用完就丟的密碼。只用英文字母與數字：會被放進連線字串。
random_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 24
  else
    od -An -N24 -tx1 /dev/urandom | tr -d ' \n'
  fi
}

# 從 api 的 JSON 記錄挑出 Warning 以上的事件。
#   count  印兩個數字「伺服器自己的 Error 與 Critical」「網頁回報的」：網頁回報的是 Warning（網頁的例外不是伺服器出錯），
#          另外算，不併進前一個數字；舊版記成 Error 的網頁回報也算在後者
#   list   列出每一筆的細節；網頁回報的另外帶出 Kind、ErrorMessage、Path
# 空的資料庫第一次啟動時，EF Core 會在確認遷移紀錄表還不存在時記兩筆 Error，那是預期中的，不算。
# 網頁回報的錯誤的類別與屬性名稱來自 src/PmTool.Api/Endpoints/ClientErrorEndpoints.cs。
readonly CLIENT_ERROR_CATEGORY=PmTool.Api.ClientErrors
readonly API_LOG_PROGRAM='
import json, sys

mode = sys.argv[1]
client_category = sys.argv[2]
wanted = {"Warning", "Error", "Critical"}

def expected(entry):
    return entry.get("EventId") == 20102 and "__ef_migrations_history" in (entry.get("Message") or "")

def first_line(text, limit=400):
    lines = (text or "").strip().splitlines()
    if not lines:
        return ""
    line = lines[0]
    if len(line) > limit:
        return line[:limit] + "…"
    return line + ("…" if len(lines) > 1 else "")

def request_path(entry):
    for scope in entry.get("Scopes") or []:
        if isinstance(scope, dict) and scope.get("RequestPath"):
            return scope["RequestPath"]
    return ""

def client_details(entry):
    state = entry.get("State") or {}
    if not isinstance(state, dict):
        return
    labels = (("Kind", "種類"), ("ErrorMessage", "網頁的訊息"), ("Path", "網頁的路徑"))
    for key, label in labels:
        if state.get(key) not in (None, ""):
            print("  %s：%s" % (label, first_line(str(state[key]))))

count = 0
client = 0
for raw in sys.stdin:
    line = raw.rstrip("\n")
    try:
        entry = json.loads(line)
    except ValueError:
        entry = None
    if not isinstance(entry, dict):
        # 程序當掉時 .NET 直接寫到標準錯誤的訊息不是 JSON，也要算進來。
        if "Unhandled exception" in line:
            count += 1
            if mode == "list":
                print("（不是 JSON 的記錄）Critical")
                print("  " + first_line(line))
        continue
    if entry.get("LogLevel") not in wanted or expected(entry):
        continue
    from_web = entry.get("Category") == client_category
    if mode == "count" and not from_web and entry.get("LogLevel") == "Warning":
        continue
    if mode == "list" or not from_web:
        count += 1
    if from_web:
        client += 1
    if mode != "list":
        continue
    print("%s  %s  %s%s" % (entry.get("Timestamp", "（沒有時間）"), entry.get("LogLevel"), entry.get("Category", ""),
                           "（網頁回報）" if from_web else ""))
    if from_web:
        client_details(entry)
    else:
        print("  訊息：" + first_line(entry.get("Message")))
    if entry.get("Exception"):
        print("  例外：" + first_line(entry.get("Exception")))
    if request_path(entry):
        print("  請求：" + request_path(entry))

if mode == "list":
    print("共 %d 筆，其中網頁回報 %d 筆。" % (count, client))
else:
    print("%d %d" % (count, client))
'

# 沒裝 Xcode 命令列工具的 Mac 上，/usr/bin/python3 是個佔位程式，一執行就跳出安裝視窗：先確認工具裝了才用它。
has_python() {
  local python
  python="$(command -v python3 2>/dev/null)" || return 1
  if [[ "$python" == /usr/bin/python3 && "$(uname)" == Darwin ]]; then
    xcode-select -p >/dev/null 2>&1 || return 1
  fi
  python3 -I -c 'import json' >/dev/null 2>&1
}

# 讀 api 的記錄（標準輸入）做 count 或 list。沒有 python3 時退回 grep：count 一樣準，list 印原始的行。
api_log_report() {
  local mode="$1" errors web server
  if has_python; then
    python3 -I -c "$API_LOG_PROGRAM" "$mode" "$CLIENT_ERROR_CATEGORY"
    return
  fi
  if [[ "$mode" == count ]]; then
    errors="$({ grep -E '"LogLevel":"(Warning|Error|Critical)"|Unhandled exception' || true; } | { grep -v '__ef_migrations_history' || true; })"
    web="$(printf '%s' "$errors" | { grep "\"Category\":\"${CLIENT_ERROR_CATEGORY}\"" || true; })"
    server="$(printf '%s' "$errors" | { grep -E '"LogLevel":"(Error|Critical)"|Unhandled exception' || true; } | { grep -v "\"Category\":\"${CLIENT_ERROR_CATEGORY}\"" || true; })"
    printf '%s %s\n' \
      "$(printf '%s' "$server" | { grep -c . || true; })" \
      "$(printf '%s' "$web" | { grep -c . || true; })"
  else
    info "（找不到 python3，下面是原始的記錄行）"
    { grep -E '"LogLevel":"(Warning|Error|Critical)"|Unhandled exception' || true; } | { grep -v '__ef_migrations_history' || true; }
  fi
}

api_logs_since() {
  compose logs --no-color --no-log-prefix --since "$1" api 2>/dev/null
}
