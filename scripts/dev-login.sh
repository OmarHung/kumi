#!/bin/bash
# 以本機 Compose 環境的第一個擁有者登入，把工作階段 Cookie 存進指定的檔案，之後的 curl 用 -b 帶上。
# 密碼從 repo 根目錄不進版控的 .env 讀，不會印出來。
# 用法：bash scripts/dev-login.sh /tmp/某處/jar.txt
#       curl -s -b /tmp/某處/jar.txt http://localhost:8080/v1/projects
# 會改資料的請求另外要帶 -H 'X-PmTool-Csrf: 1' -H 'Content-Type: application/json'。
set -euo pipefail
JAR="${1:?用法：dev-login.sh <cookie 檔案的路徑>}"
ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
set -a; . "$ROOT/.env"; set +a
BODY=$(python3 -c 'import json,os;print(json.dumps({"email":os.environ["BOOTSTRAP_ADMIN_EMAIL"],"password":os.environ["BOOTSTRAP_ADMIN_PASSWORD"]}))')
curl -s -o /dev/null -w 'login=%{http_code}\n' -c "$JAR" -X POST \
  -H 'X-PmTool-Csrf: 1' -H 'Content-Type: application/json' --data "$BODY" \
  "${PMTOOL_URL:-http://localhost:8080}/v1/session"
