# 本機自用的操作手冊

這份寫給在自己的 Mac 上跑 Kumi、自己照顧它的人。

- 指令都可以直接複製，在 repo 的根目錄執行。腳本在 repo 的哪個目錄下執行都一樣。
- 這一套只綁本機（`127.0.0.1`），沒有 HTTPS，不要直接放到網路上。要上雲端或給別人用，看 `docs/DEPLOYMENT.md`。

## 常用指令

| 要做什麼 | 指令 |
|---|---|
| 看這一套好不好 | `bash scripts/ops/status.sh` |
| 更新到目前的程式碼 | `bash scripts/ops/deploy-local.sh` |
| 馬上備份一份 | `bash scripts/ops/backup-now.sh` |
| 確認備份讀得回來（不碰正在用的） | `bash scripts/ops/restore-drill.sh` |
| 把資料換回某份備份 | `bash scripts/ops/restore.sh backups/<檔名>` |
| 看錯誤的細節 | `bash scripts/ops/errors.sh` |

## 1. 第一次啟動

1. 安裝 Docker Desktop，並設成開機自動啟動（見第 8 節）。
2. 建立設定檔，填上三個密碼：

   ```
   cp .env.example .env
   openssl rand -hex 24     # 產生一組，貼到 POSTGRES_PASSWORD
   openssl rand -hex 24     # 再產生一組，貼到 POSTGRES_APP_PASSWORD
   ```

   - 兩個資料庫的密碼是兩個角色的（見第 11 節），只用英文字母與數字。
   - `BOOTSTRAP_ADMIN_PASSWORD` 是你登入用的密碼，至少 10 個字元。
   - `.env` 不進版控，不要傳給別人。
3. 啟動。第一次要建置映像檔，會花幾分鐘：

   ```
   bash scripts/ops/deploy-local.sh
   ```

   - `ATTACHMENTS_DIR`（附件的檔案放哪裡，預設 `./data/attachments`）一般不用改；要改的話看第 3 節的「附件」。
4. 開 <http://localhost:8080>，用 `.env` 裡的 `BOOTSTRAP_ADMIN_EMAIL` 與 `BOOTSTRAP_ADMIN_PASSWORD` 登入。
5. 確認一切正常：

   ```
   bash scripts/ops/status.sh
   ```

第一次啟動時：

- 還沒有資料庫，所以沒有「部署前的備份」，腳本會說它跳過了。
- 附件的資料夾（`data/attachments`）由腳本建立，並確認 api 的使用者寫得進去（見第 3 節的「附件」）。
- 執行用的角色 `pmtool_app` 由 `deploy-local.sh` 在啟動 api 之前建立或更新（`deploy/postgres/app-role.psql`），全新的資料庫也一樣。所以第一次啟動也要用 `deploy-local.sh`，不要直接 `docker compose up`。
- backup 服務一起來就做第一份排程備份。這時資料庫可能還是空的，沒關係，24 小時後會有下一份。
- 第一個擁有者只在資料庫裡還沒有任何帳號時建立。之後改 `.env` 裡的 `BOOTSTRAP_*` 不會有作用。

## 2. 更新

拿到新的程式碼之後，用這支腳本更新，不要直接 `docker compose up`：

```
git pull
bash scripts/ops/deploy-local.sh
```

它依序做這幾件事：

1. 備份一份，標籤 `pre-deploy`。備份失敗就停下來，什麼都不更新。
   資料庫沒在跑（例如之前 `docker compose down` 過），但資料還在時，它會先只啟動資料庫、備份，才往下做。
2. 只啟動（或換新）資料庫，確保執行用的角色 `pmtool_app` 在、密碼和 `.env` 的 `POSTGRES_APP_PASSWORD` 一樣。可以重跑：角色已經在就只把密碼設回來。
3. 建置新的 api 映像檔（`docker compose build api`；`--no-build` 時跳過）。
4. **遷移**：停下 api，跑一次 `docker compose run --rm migrate`。`migrate` 是只跑一次的服務（同一個映像檔、`dotnet PmTool.Api.dll migrate`）：遷移資料表、把讀寫的權限授予 `pmtool_app`、資料庫裡還沒有帳號時建立第一個擁有者，做完就結束，不常駐，`docker compose up` 也不會帶起它。**結束代碼不是 0 就停在這裡**，不啟動新的 api；腳本印出 migrate 最後 40 行記錄，說明 api 是停著的、資料在部署前的備份裡。
5. `docker compose up -d`：換掉有變動的容器。
6. 等 `/health/ready` 回應，最多 180 秒。

**為什麼遷移要分開**：擁有者的連線字串（`pmtool`，能改資料表結構）與第一個擁有者的密碼（`BOOTSTRAP_ADMIN_PASSWORD`）只在 `migrate` 的環境裡；`api` 只有執行用的角色 `pmtool_app` 的連線、`Database__MigrateOnStartup=false`。api 被攻破時，拿到的環境變數改不了資料表結構，也不含第一個擁有者的密碼。代價：**不能直接 `docker compose up`**：新版有沒套用的遷移時，api 起得來但 `/health/ready` 回 503、記一筆 Error 說要跑 migrate。永遠用 `deploy-local.sh`；要手動跑遷移：`docker compose run --rm --no-deps migrate`。

**容器硬化**（`api` 與 `backup`）：唯讀的根檔案系統（`read_only`）、`/tmp` 是記憶體的 tmpfs、`cap_drop: [ALL]`、`no-new-privileges`、程序數與記憶體的上限；要寫的只有附件資料夾（api）、備份目錄（backup）與 `/tmp`。上限可以在 `.env` 調：`API_MEM_LIMIT`（預設 1g，api 平常用不到 200 MB）、`API_PIDS_LIMIT`（512）、`BACKUP_MEM_LIMIT`（512m）、`BACKUP_PIDS_LIMIT`（128）；api 一直被 OOM 殺掉（`docker inspect` 的 `OOMKilled`）才調高。`backup` 在 Linux 上要多兩個 capability（`DAC_OVERRIDE`、`FOWNER`）才讀得到 api 的使用者建的附件、改得了備份目錄的權限，macOS 的 Docker Desktop 不看檔案的擁有者，用不到。`db` 只加 `no-new-privileges`：PostgreSQL 的映像檔要以 root 起來再降權，拿掉 capability 會起不來。

只改了 `compose.yaml` 或 `.env`、程式碼沒變時，可以不建置：

```
bash scripts/ops/deploy-local.sh --no-build
```

**`.env` 少了 `POSTGRES_APP_PASSWORD` 時**，用到 `docker compose` 的腳本（`restore-drill.sh` 以外都是）一開始就停下來，說明怎麼補（產生一組、加在哪裡），什麼都不動，也不會自己改 `.env`。`docker compose` 本身也會拒絕執行，連 `docker compose ps` 都不行。

**更新失敗時**，腳本會印出 api 最後 50 行記錄與接下來的做法：

1. 看記錄找原因。只是比較慢的話，等一下再跑 `bash scripts/ops/status.sh`。
2. 退回上一版：`git log --oneline -5` 找出上一個能用的提交，`git switch --detach <提交>`，再跑一次 `bash scripts/ops/deploy-local.sh`。
3. 退回之後還是起不來（新版的遷移已經改了資料表），把資料換回「往前更新」之前的樣子：`bash scripts/ops/restore.sh backups/<…-pre-deploy.dump>`。
   - **要選對那一份**：是從舊版更新到新版的那一次、`deploy-local.sh` 一開始印的那份 `pre-deploy`。
   - 第 2 步退回舊版時也會做一份 `pre-deploy`，可是那時資料表已經被新版改過了，**不要選它**。
   - 失敗的訊息會列出最近幾份 `pre-deploy` 與它們的時間，照時間挑。

### 從遷移分開之前的版本更新過來（只有一次）

這一版起 api 不再自己遷移，也拿不到擁有者的密碼。第一次更新過來時照平常那樣跑 `bash scripts/ops/deploy-local.sh`，不用改 `.env`：

- 多一步「遷移」：既有的資料庫已經遷移過、`pmtool_app` 的權限也都在，`migrate` 很快跑完（沒有新的遷移就什麼都不改，只把權限再授予一次）。第一個擁有者已經存在，`BOOTSTRAP_*` 不會有作用。
- backup 的映像檔改成自建的（資料庫的映像檔加 `age`，`deploy/pg-tools/Dockerfile`），第一次要 `apt` 安裝一次，需要網路；backup 與 api 的容器各重建一次，資料不受影響。
- 之後 `docker compose up -d --no-build`（例如 `down` 之後把容器叫回來）仍然可以：遷移只在有新版本時才需要。
- 從這一版退回舊版：舊版的 api 會自己遷移，也需要擁有者的連線字串，退回前先 `git switch --detach <舊的提交>` 再跑 `deploy-local.sh`，舊的 `compose.yaml` 會把它們補回去。

### 從兩個角色之前的版本更新過來（只有一次）

這一版起，api 平常用執行用的角色 `pmtool_app` 連資料庫，工作階段 Cookie 的金鑰改存在資料庫裡。第一次更新過來時：

1. 先在 `.env` 加上 `POSTGRES_APP_PASSWORD`（`openssl rand -hex 24` 產生）。沒加的話 `deploy-local.sh` 會停下來說明。
2. 跑 `bash scripts/ops/deploy-local.sh`。會看到：
   - 資料庫的容器重建一次：PostgreSQL 的映像檔固定到 digest（可能順便換成比較新的 18 小版本，小版本不用搬資料），健康檢查也改成用 TCP 問。執行用的角色不是靠初始化的目錄建的，而是由 `deploy-local.sh` 在啟動 api 之前建立。資料在資料卷裡，不受影響。
   - 「資料庫與執行用的角色」這一步建立 `pmtool_app`。
   - 遷移（現在由 `migrate` 做）把讀寫的權限授予 `pmtool_app`。
3. **每個人要重新登入一次**：舊的金鑰在 `api-keys` 資料卷裡，新版不讀它，換到資料庫裡的新金鑰之後，舊的 Cookie 都解不開。
4. 確認新版正常、大家都重新登入過之後，舊的資料卷可以手動刪掉（只刪這一個，資料庫的資料卷不要動）：

   ```
   docker volume rm pm-tool_api-keys
   ```

## 3. 備份

**在哪裡**：repo 根目錄的 `backups/`（`.env` 的 `BACKUP_DIR` 可以改）。

- 它在 Docker 的資料卷外面：`docker compose down -v` 刪不到它。刪掉整個 repo 資料夾就會一起刪掉。
- 目錄權限 700、檔案 600。檔案預設沒有加密，裡面是真實資料；要加密看下面的「備份加密」。
- **權限擋不住這台 Mac 的其他帳號**：repo 放在外接碟上時，macOS 預設用「忽略擁有者」（`noowners`）掛載外接碟，這台 Mac 上每個本機帳號都被當成擁有者。要更嚴格，把備份放在內接碟的家目錄底下，`.env` 寫絕對路徑（不能用 `~`），例如 `BACKUP_DIR=/Users/<你的帳號>/kumi-backups`。換目錄之後把舊的備份搬過去（`rsync -a backups/ /Users/<你的帳號>/kumi-backups/`，`.pmtool-project` 也會一起過去），再讓 backup 服務換上新的目錄：`docker compose up -d --no-build backup`。
- 被 git 忽略，不會進版控。
- 目錄裡的 `.pmtool-project` 寫著這個目錄屬於哪一套（例如 `pm-tool`）。不要刪，也不要讓兩套共用一個目錄（見第 9 節）。

**附件的檔案也在備份裡**：項目與知識庫頁面上傳的檔案放在同一個附件資料夾（下面「附件」），不在資料庫裡，所以備份分兩部分：資料庫是 `pmtool-*.dump`，檔案是同一個目錄底下的 `attachments/`。

- 每次備份在 `pg_dump` 成功之後，把附件資料夾增量複製到 `backups/attachments/`：只複製還沒有的檔案（檔案建好之後不會改，所以不用比內容）。第二次以後很快。
- **永遠不刪**：附件在網頁上被刪掉、或這份備份之後才被刪掉，備份裡的檔案都照舊留著。所以 `attachments/` 不算在「留最新 14 份」裡，只會愈來愈大；真的想清，自己確認過之後手動刪。
- 複製失敗（磁碟滿了、權限不對）時，**這一份備份算失敗**：剛做好的 dump 刪掉、記一筆錯誤，下一次檢查（一小時後）再試；`status.sh` 在「附件」那段會列出來。只有資料庫、沒有檔案的備份，還原之後會有點得開卻找不到檔案的附件，所以不留。
- 備份目錄裡的 `.pmtool-attachments-status` 記著最近一次複製的結果，給 `status.sh` 讀，不要刪。
- 每份 dump 和 `attachments/` 不是同一個時間點：`attachments/` 是「到最近一次備份為止出現過的所有檔案」。資料庫裡的每一列，它的檔案都早在 `pg_dump` 之前就寫好了，所以一般來說 `attachments/` 裡都有。唯一的例外：檔案上傳之後、在「`pg_dump` 之後、複製之前」這一小段時間就被刪掉（同一個備份週期內剛傳就刪），它從沒被備份到，那一份 dump 還原後會有資料列、沒有檔案（下載 404）。

**附件**：

- 附件的檔案放在主機的資料夾，預設 repo 根目錄的 `data/attachments/`（`.env` 的 `ATTACHMENTS_DIR` 可以改，相對路徑從 repo 根目錄算起，也可以寫絕對路徑）。被 git 忽略，`docker compose down -v` 刪不到它。裡面是使用者上傳的真實檔案，沒有加密。
- api 容器以 UID 1654 執行（不是 root），資料夾要讓它寫得進去，不然 api 啟動時會說附件資料夾不能用而失敗。`deploy-local.sh` 會先建立資料夾、用一次性的容器以 1654 試寫一個檔案：
  - **macOS（Docker Desktop）**：不管檔案的擁有者，掛進去的資料夾誰都寫得進去，通常不用做什麼。要是試寫失敗，多半是 Docker Desktop 的檔案分享沒有包含這個路徑（Settings → Resources → File sharing）；`/Volumes` 底下的外接碟預設就有。
  - **Linux**：掛進去的資料夾保留主機上的擁有者。腳本發現 1654 寫不進去時，會用一次性的容器把資料夾改成 `1654:1654`、權限 755（裡面的檔案是 600，只有 api 讀得到）；之後在主機上要自己動這個資料夾得用 `sudo`。
  - 自己建資料夾的話：`mkdir -p data/attachments`，Linux 再 `sudo chown 1654:1654 data/attachments`。**不要讓 Docker 自己建**（掛載點不存在時它會建成 root 的，api 就寫不進去）。
- backup 服務把這個資料夾**唯讀**掛到 `/attachments`：備份只複製、改不了附件。

**檔名**：`pmtool-YYYYMMDD-HHMMSS.dump` 是排程備份；`pmtool-YYYYMMDD-HHMMSS-標籤.dump` 是帶標籤的（`manual` 手動、`pre-deploy` 部署前、`pre-restore` 還原前）。時間用 `.env` 的 `TZ`。開了備份加密（下面）的檔名後面多一個 `.age`（`….dump.age`）；加密前後的備份可以並存，保留份數、清理、`status.sh`、`restore.sh`、`restore-drill.sh` 都一起算。

**自動備份**：`backup` 服務每小時檢查一次，最新的排程備份超過 24 小時（`BACKUP_INTERVAL_HOURS`）就做一份。

- Mac 睡著錯過時間也沒關係：醒來後一小時內會補做。
- 排程備份只留最新的 14 份（`BACKUP_KEEP`），多的自動刪掉。
- 帶標籤的備份不算在 14 份裡，也**不會自動刪掉**。偶爾自己清一下舊的。
- 每一份都先寫成暫存檔，用 `pg_restore` 從頭到尾讀過一遍，確認讀得回來，才改成正式的名字。目錄裡看得到的備份都是完整的。

**備份加密（選用，預設關）**：`.env` 填 `BACKUP_AGE_RECIPIENT`（[age](https://github.com/FiloSottile/age) 的公鑰，`age1…`）時，資料庫的備份在寫進備份目錄**之前**就用 age 加密成 `.dump.age`：`pg_dump` 的輸出直接送進 age，備份目錄裡從頭到尾沒有明文。沒填就照舊存明文的 `.dump`。

- **為什麼**：備份會被複製到別的磁碟、雲端硬碟，也會被備份軟體讀走；明文的備份裡有所有資料，也有 Data Protection 的金鑰（沒設憑證時是明文）與 AI 金鑰、寄信密碼的密文。加密之後，只有拿著私鑰的人讀得了。
- **開的步驟**（預設關著，需要時照下面做）：
  1. 在一台信得過的電腦上產生一對金鑰。私鑰**不要放進 repo、不要放進 `.env`、不要和備份放在同一個地方**：
     ```
     (umask 077; docker run --rm --entrypoint age-keygen pm-tool-backup > ~/kumi-backup-key.txt)   # 需要先建過 backup 映像檔（deploy-local.sh 會建）
     grep 'public key' ~/kumi-backup-key.txt      # 檔案裡的註解行：Public key: age1…（這一行是公鑰）
     ```
     **私鑰另外存一份**（密碼管理器、另一顆磁碟）。**弄丟私鑰，加密的備份就永遠解不開**。
  2. `.env` 加 `BACKUP_AGE_RECIPIENT=age1…`（公鑰；備份容器只拿得到公鑰，私鑰不在容器裡），再 `bash scripts/ops/deploy-local.sh --no-build`（backup 服務會重新啟動）。公鑰寫錯時 backup 服務啟動就失敗並說明，不會等到第一份備份。
  3. `bash scripts/ops/status.sh` 的「備份加密」那一行會寫「開著」；`bash scripts/ops/backup-now.sh` 做一份，檔名是 `….dump.age`。
  4. **馬上演練一次還原**（第 5 節）：`BACKUP_AGE_IDENTITY_FILE=$HOME/kumi-backup-key.txt bash scripts/ops/restore-drill.sh`。沒演練過，等於不知道私鑰對不對。
- **還原要私鑰**：`restore.sh`、`restore-drill.sh` 碰到 `.age` 的備份時，要在執行時設 `BACKUP_AGE_IDENTITY_FILE=<私鑰檔的絕對路徑>`（`BACKUP_AGE_IDENTITY_FILE=$HOME/kumi-backup-key.txt bash scripts/ops/restore.sh backups/<檔名>.dump.age`）。私鑰只在這個指令的環境變數裡，不寫進 `.env`。解密在一次性的容器裡做（用 backup 的映像檔，主機上不用裝 age），私鑰唯讀掛進去（`--mount type=bind,…,readonly`，路徑不存在時直接失敗，不會被 Docker 悄悄建成資料夾）、不開網路，明文只走管線送進資料庫的容器，**不寫到主機的磁碟**。精確地說：還原時資料庫的容器裡會有一份明文的暫存檔（`/tmp/pmtool-restore.dump`，在容器的可寫層，不在資料卷也不在備份目錄），讀過一遍、還原完成就刪掉；中途失敗時 `restore.sh` 也會在結束時刪（刪不掉時，容器重建就沒了）。
- **開了加密之後，舊的明文 `.dump` 怎麼處理**：加密只管「開了之後新做的」備份，目錄裡以前的 `.dump` 還是明文，不會自己消失，也不會被自動清成加密的（`BACKUP_KEEP` 的清理只算同一種格式：明文的那 14 份要等到比較新的明文備份把它們擠掉才會刪，開了加密之後不會再有新的明文，所以它們會一直留著）。`status.sh` 會在加密開著、目錄裡還有明文 `.dump` 時列一行「提醒」。處理方式（照順序）：
  1. 確認新的加密備份能還原（上面第 4 步的演練做過了）。
  2. 還需要保留的舊備份（例如某個 `pre-deploy`）：用公鑰加密成 `.dump.age`，確認讀得回來，再刪掉明文：
     ```
     age -r age1… -o backups/<檔名>.dump.age backups/<檔名>.dump     # age 可以裝在主機上，或用 docker run --rm -i --entrypoint age pm-tool-backup -r age1… < 檔案 > 輸出
     ```
  3. 不需要的明文備份直接刪掉。刪除不等於抹除：Time Machine、雲端硬碟同步、其他磁碟的複本都要一併處理（第 4 節複製出去的那些尤其要記得）。
  4. 已經外流過的明文備份，裡面的東西（Data Protection 的金鑰在沒設憑證時是明文）要視為外流：照第 20 節換金鑰。
- **每一份加密的備份也先讀過一遍**：加密時把同一份串流分一條給 `pg_restore` 讀完，讀不完就算失敗、刪掉暫存檔（加密的檔案本身在容器裡沒有私鑰可以讀）。
- **附件不加密**：`backups/attachments/` 是附件原樣的增量複製（只補新的、永遠不刪），逐檔加密會讓這個機制與還原的補檔都得重做，也讓備份目錄看不出哪些檔案已經有了。要保護附件的備份，把備份目錄放在加密的磁碟上（macOS 的 FileVault 或加密的外接碟）。
- **關掉**：拿掉 `BACKUP_AGE_RECIPIENT` 再部署。之前的 `.age` 備份留著，照樣用私鑰還原；私鑰要留到最後一份 `.age` 備份被清掉為止。
- **換金鑰**：換公鑰之後新的備份用新的公鑰；舊的備份還是要舊的私鑰。

**確認它有在跑**：

```
bash scripts/ops/status.sh                 # 「最新的排程備份」是幾小時前
docker compose logs --tail 20 backup       # 每一次檢查、備份、刪除都有一行
```

`status.sh` 的「附件」那一段：附件資料夾裡有幾個檔案、多大；備份目錄的 `attachments/` 裡有幾個；還沒備份到的有幾個。上傳超過一天還沒進備份目錄、或最近一次附件複製失敗，會列在「有問題」裡（結束代碼 1）。剛上傳的檔案在下一次備份之前一定是「還沒備份到」，一天內不算問題。

**馬上備份一份**（例如要大量修改資料之前）：

```
bash scripts/ops/backup-now.sh                 # 標籤 manual
bash scripts/ops/backup-now.sh before-import   # 自己取標籤：小寫英文字母、數字與 -
```

## 4. 把備份複製到別的磁碟（建議）

`backups/` 和 repo 在同一顆磁碟上。那顆磁碟壞了，資料和備份會一起不見。至少每週複製一次到另一顆磁碟：

```
rsync -a backups/ "/Volumes/<另一顆磁碟>/kumi-backups/"
```

- `rsync -a` 會保留 600 的權限與檔案的修改時間。不加 `--delete`，舊的備份在另一邊會一直留著，自己清。
- `backups/` 底下的 `attachments/` 是附件的備份，一起複製出去就好（上面那行已經包含）。不加 `--delete` 也剛好符合它「只增不刪」的做法。
- 從別的磁碟複製回來時也要保留修改時間：用 `rsync -a` 或 `cp -p`。哪一份比較新、要刪哪一份、演練預設用哪一份，都是看修改時間；用一般的 `cp`，複製回來的舊備份會被當成最新的。
- 外接碟預設不在 Time Machine 的備份範圍裡，不要以為它有備到。
- 要放到雲端硬碟，先想清楚：備份檔預設沒有加密。開了備份加密（第 3 節）之後，資料庫的備份檔放哪都可以，但 `attachments/` 還是原樣的檔案。

## 5. 還原演練（建議每個月一次）

確認備份真的讀得回來。演練在另外起的、用完就丟的資料庫裡做，不碰正在用的那一套。

```
bash scripts/ops/restore-drill.sh                            # 用最新的一份
bash scripts/ops/restore-drill.sh backups/<檔名>              # 指定一份
```

它會列出每張表的筆數，最後說「演練成功」，然後自己清掉演練的容器。不帶 `--with-api` 時只驗資料庫，不碰附件的檔案。備份是 `.dump.age`（加密的）時要設 `BACKUP_AGE_IDENTITY_FILE=<私鑰檔的絕對路徑>`（第 3 節）。

**想登入看看資料**：

```
bash scripts/ops/restore-drill.sh --with-api
```

- 開 <http://127.0.0.1:8081>，用原本的帳號登入。只看，不要改：演練的資料用完就丟。
- 演練環境不會寄信（背景的寄信工作是關著的）。但它裡面有和正式環境相同的寄信設定與 AI 連線設定：用完就 `--down`，不要對外開放。
- **用 `127.0.0.1`，不要用 `localhost`。** Cookie 不分連接埠，用 `localhost:8081` 登入會把 `localhost:8080` 的登入蓋掉。
- **附件**：演練的 api 用自己的一份附件資料夾，放在暫存目錄（`$TMPDIR/<專案>-drill-attachments`，macOS 是 `/var/folders/…`），從備份目錄的 `attachments/` 複製來；演練的 api 的 `Attachments__Directory` 指到它，不碰正在用的那個資料夾。可以在演練的網頁上打開附件，確認備份裡的檔案真的讀得到。`--down` 時一起刪掉。
- 它用 `deploy-local.sh` 建好的 API 映像檔（`pm-tool-api`）。找不到時會叫你先建置。流程和部署一樣：先跑一次 `migrate`（用演練資料庫的超級使用者）、再起 api（`MigrateOnStartup=false`，也套用唯讀根目錄、`cap_drop`、`no-new-privileges`），所以演練也驗證了「備份能被新版遷移、api 在硬化的條件下能跑」。
- 演練的資料庫沒有 `pmtool_app`，演練的 api 兩個連線都用演練資料庫的超級使用者。
- 8081 被占用時換一個：`DRILL_PORT=8099 bash scripts/ops/restore-drill.sh --with-api`。
- 看完清掉：

  ```
  bash scripts/ops/restore-drill.sh --down
  ```

## 6. 真的還原

資料被改壞、誤刪了，要把正在用的那一套換回某份備份時用。只換一兩筆的話，不要整套還原：用 `restore-drill.sh --with-api` 開起舊的資料，照著在 8080 上改回來。

```
bash scripts/ops/restore.sh backups/<檔名>
```

- 它先把備份檔複製進資料庫的容器、從頭到尾讀過一遍。讀不完就停下來，什麼都不動。備份是 `.dump.age` 時，要設 `BACKUP_AGE_IDENTITY_FILE`（第 3 節），解密後直接送進資料庫的容器。
- 接著說明要做什麼，要你打出專案名稱（`pm-tool`）確認。打錯就什麼都不做。
- 順序：先替現況備份一份（`pre-restore`）→ 停 api 與 backup → 刪掉並重建資料庫 → 還原 → 跑 `migrate`（把舊版備份升到目前的資料表結構、把權限授予 `pmtool_app`；api 不再自己遷移，沒有這一步它讀不了剛還原的資料庫）→ 作廢所有的登入 → 把 `backups/attachments/` 的檔案補回附件資料夾 → 啟動 api 與 backup → 等就緒。
- 備份之後新增或修改的資料都會不見。
- **附件的檔案**：還原只「補」不「換」：備份目錄裡有、附件資料夾裡沒有的檔案複製回去；已經有的不覆蓋，什麼都不刪。
  - 資料庫退回備份的樣子之後，備份之後才上傳的附件沒有資料列了，它們的檔案還留在資料夾裡，是沒人用的孤兒檔案，只佔空間（不會被網頁看到）。要不要清由你決定；因為備份會持續複製，這些檔案多半也已經在 `backups/attachments/` 裡（`pre-restore` 那一步也會複製一次）。
  - 備份之後才被刪掉的附件，資料列回來了，檔案也在備份裡，會一併補回，所以網頁上又看得到。
  - 備份目錄還沒有 `attachments/`（這一套還沒備份過附件）時，這一步跳過。
- **所有人都要重新登入；備份之後做的停用帳號、改密碼、重設密碼都退回了，要再做一次。** 登入的紀錄也跟著退回備份的時候，所以還原時把所有的登入都作廢，免得備份之後已經登出或被作廢的登入又能用。
- 完成時會印出 `pre-restore` 那份的位置。換錯了，用它換回來：

  ```
  bash scripts/ops/restore.sh backups/<…-pre-restore.dump>
  ```

**中途失敗時**，腳本會停下來，說明現況（附件補不回去時也是，再跑一次同一個還原就會接著補）：資料庫是不是空的、api 是不是停著、`pre-restore` 那份在哪，以及換回去的指令。照它印的做。

還原在一個交易裡做：失敗時整個撤銷，資料庫是空的，不會留下一半的資料。

還原與演練都不還原備份裡的權限（`pg_restore --no-privileges`）：

- 演練的資料庫沒有 `pmtool_app` 這個角色，備份裡授予它的權限（GRANT）會讓還原失敗。
- 真的還原時，權限由 `restore.sh` 還原後跑的 `migrate` 重新授予 `pmtool_app`，和備份是哪一版做的、當時有沒有這個角色都無關。

還原到**新的**資料庫伺服器（演練、換機器、上雲）時，事件流自己處理交易編號：

- 事件的位置是（交易編號、流水號）。新的伺服器交易編號從頭算起，比備份裡的事件與讀者存的位置都小，新的事件會一直排在讀者的位置前面，通知就停了（2026-11-19 演練時發現）。
- 現在 api 讀事件時，若發現有事件的交易編號比資料庫現在的還大（同一台伺服器上不可能），就把這些歷史事件的交易編號歸成 1；讀的人（通知）讀出自己存的位置時也先換算，比現在還大的當成（1、原本的流水號）並存回去。執行的角色對事件表只能新增與讀，所以歸一是 `migrate` 用資料表的擁有者做的：**還原之後一定要跑一次 `migrate`**（`restore.sh`、`restore-drill.sh` 會自己跑；手動還原時記得跑），不然 api 的通知與即時更新每一輪都會記 `permission denied for table events`。
- 本機的 `restore.sh` 是在同一台伺服器上重建資料庫，交易編號會接著往上，不會遇到。
- 邊界：備份當下若剛好有兩個寫入同時進行、順序交錯，還原後那一兩筆通知可能重送或漏送一次。

## 7. 看錯誤

`status.sh` 會列出 api 過去 24 小時有幾筆 Error／Critical。有的話看細節：

```
bash scripts/ops/errors.sh               # 過去 24 小時的 Warning 以上
bash scripts/ops/errors.sh --since 2h    # 也可以寫 30m、2026-11-17T09:00
```

每一筆有時間、等級、類別、訊息、例外的第一行、請求的路徑。網頁回報的錯誤也在裡面：標著「網頁回報」，改列種類、網頁的訊息與網頁的路徑。網頁回報的記成 Warning（網頁的例外不是伺服器出錯），同一個人、同一則訊息一分鐘只記一次。`status.sh` 的「錯誤」只數伺服器自己的 Error 與 Critical，網頁回報的 Warning 另外寫一個筆數，不算進去。

- api 的記錄是一行一筆的 JSON。看原始的記錄：`docker compose logs --since 1h api`。
- 每個容器的記錄每份 10 MB、留 5 份，更早的會被輪替掉。部署換掉 api 的容器之後，舊容器的記錄也跟著不見。要留證據，先存下來：`docker compose logs api > api.log`。
- 這幾種每次啟動都會出現，可以不管：
  - Warning「沒有設定 DataProtection:CertificatePath：工作階段 Cookie 的加密金鑰以明文存在資料庫裡…」（產生新金鑰時還有一筆「No XML encryptor configured」）：沒有設憑證。設憑證的步驟在第 20 節；上雲端要設（`docs/DEPLOYMENT.md` 第 1.4 節）。
  - 不是 JSON 的「Cannot load library libgssapi_krb5.so.2」：資料庫的連線套件在找 Kerberos，這一套不用它。
  - 空的資料庫第一次啟動時，查不到遷移紀錄表的兩筆 Error。`status.sh` 與 `errors.sh` 已經不算它們。

## 8. 讓它自己跑起來

**Docker Desktop 設成開機自動啟動**：Docker Desktop → Settings → General → 勾「Start Docker Desktop when you sign in to your computer」。

- 三個服務都是 `restart: unless-stopped`：Docker 起來，它們就跟著起來。
- 自己用 `docker compose stop` 停掉的不會自己起來。要叫回來：`docker compose start`。
  不要為了這個跑 `deploy-local.sh`：它每次都多做一份不會自動刪掉的 `pre-deploy` 備份。
- 容器被 `docker compose down` 刪掉了（資料卷還在）：`docker compose up -d --no-build`。
- repo 放在外接碟上時：backup 服務要讀外接碟上的 `scripts/ops/`、`backups/` 與 `data/attachments/`，api 也掛了 `data/attachments/`。Docker 啟動時外接碟還沒接上的話，backup 與 api 很可能起不來（附件資料夾不存在時，Docker 在 Linux 會建成 root 的空資料夾；接上之後要確認它還是原來那個，而不是一個空的）；db 不掛 repo 裡的任何東西，照常起來（資料在 Docker 的資料卷裡，不受影響）。接上之後跑一次 `bash scripts/ops/status.sh`，有沒在跑的就執行 `docker compose up -d --no-build`。
- 養成習慣：每週跑一次 `bash scripts/ops/status.sh`。它不會主動通知你。

## 9. 換連接埠、另起一套隔離的環境

`.env` 的 `PMTOOL_PORT` 可以換網頁的連接埠。

要另外起一套完全隔離的環境（測試、試新版本），用自己的專案名稱、env 檔、連接埠與**備份目錄**。所有腳本都認這兩個環境變數：

```
cp .env.example /tmp/pmtool-test.env
# 編輯它：填三個密碼，PMTOOL_PORT=8090，BACKUP_DIR=/tmp/pmtool-test-backups，ATTACHMENTS_DIR=/tmp/pmtool-test-attachments
export COMPOSE_PROJECT_NAME=pmtool-test PMTOOL_ENV_FILE=/tmp/pmtool-test.env
bash scripts/ops/deploy-local.sh
bash scripts/ops/status.sh
```

用完整套刪掉（只刪這一套，不碰 8080 那一套）：

```
docker compose -p pmtool-test --env-file /tmp/pmtool-test.env down -v
unset COMPOSE_PROJECT_NAME PMTOOL_ENV_FILE
```

- **備份目錄一定要換**（`BACKUP_DIR`）。沿用預設的 `./backups` 會和 8080 那一套共用：測試的備份寫進正式的目錄、清理把兩套合起來只留 14 份、一套看到另一套很新的備份就不做自己的、演練與還原也可能拿到另一套的備份。
- **附件資料夾也要換**（`ATTACHMENTS_DIR`）。沿用預設的 `./data/attachments` 會讓測試的 api 寫進 8080 那一套的檔案；專案名稱不是 `pm-tool` 又沒設 `ATTACHMENTS_DIR` 時，`deploy-local.sh` 與 `backup-now.sh` 會直接拒絕。
- 所以每個備份目錄裡都有 `.pmtool-project`，寫著它屬於哪一套。目錄還沒有這個檔案時，第一個用到它的那一套會寫上自己的名字（8080 那一套的 `backups/` 會被標成 `pm-tool`）。之後別的專案用到這個目錄，backup 服務、`backup-now.sh`、`restore-drill.sh`、`restore.sh`、`deploy-local.sh` 都會拒絕，並說明要換 `BACKUP_DIR`；`status.sh` 會把它列成問題。
- `COMPOSE_PROJECT_NAME` 預設 `pm-tool`，`PMTOOL_ENV_FILE` 預設 repo 根目錄的 `.env`。
- 想測附件的備份、還原，不需要 api 真的會上傳：把檔案直接放進這一套的 `ATTACHMENTS_DIR`（例如 `mkdir -p $ATTACHMENTS_DIR/ab && echo x > $ATTACHMENTS_DIR/ab/0001`），再跑 `backup-now.sh`、`status.sh`、`restore.sh`、`restore-drill.sh --with-api`。映像檔用現有的就好：`docker tag pm-tool-api:latest <專案>-api:latest`，然後 `deploy-local.sh --no-build`。
- 演練的連接埠用 `DRILL_PORT`（預設 8081），可以放在 env 檔或環境變數裡。演練的容器與網路都帶專案名稱當前綴（`pmtool-test-drill-…`）。

## 10. 這一套還缺什麼

這是給一個人或一個小團隊在本機自用的設定。程式已經準備好放在反向代理與 HTTPS 後面、用代管的資料庫、遷移與啟動分開，做法在 `docs/DEPLOYMENT.md`；本機這一套沒有用到那些設定。開放給別的組織之前，還要做：

- Email 改成每個組織各自唯一。
- 限流跨容器共用（每個 api 程序各算各的額度；工作階段的作廢、權限變動與收件匣的即時更新已經用資料庫的 `LISTEN/NOTIFY` 互相通知，見第 14 節）。
- 權杖失敗的限流是依來源位址算的、記在記憶體裡：同時有非常多不同的來源在試錯時，記錄的數量有上限，超過之後最舊的來源會被忘掉、重新算起。前面要有反向代理或防火牆擋大量的來源。
- 強制改密碼、登入中的裝置清單、雙重驗證。
- 錯誤監控接外部服務（例如 Sentry），出錯時主動通知；現在要自己跑 `status.sh` 才知道。
- 備份自動複製到別的地方；現在要自己複製（第 4 節）。附件的檔案也一樣（`backups/attachments/` 要一起複製）。資料庫的備份可以選用 age 加密（第 3 節），附件的備份不加密。
- 附件沒有病毒掃描、沒有縮圖、沒有版本管理；備份裡的附件只增不刪，空間要自己留意（`status.sh` 的「附件」列出大小與磁碟的剩餘）。

## 11. 資料庫的兩個角色

| 角色 | 密碼（`.env`） | 誰用 | 能做什麼 |
|---|---|---|---|
| `pmtool` | `POSTGRES_PASSWORD` | `migrate`（只跑一次的遷移服務）、backup 服務、`restore.sh` | 資料表的擁有者，本機也是超級使用者；api 的環境裡沒有它 |
| `pmtool_app` | `POSTGRES_APP_PASSWORD` | api 平常的每一個請求 | 只能讀寫資料：不能改資料表結構、不能建資料庫與角色 |

- api 被攻破時，用的是 `pmtool_app`，改不了資料表結構。
- 讀寫的權限不是手動給的：每次 `migrate`（`deploy-local.sh` 與 `restore.sh` 都會跑）遷移完，把每個模組的資料表授予 `pmtool_app`（`Database__RuntimeRole`）。
- **換 `pmtool_app` 的密碼**：改 `.env` 的 `POSTGRES_APP_PASSWORD`，再跑 `bash scripts/ops/deploy-local.sh --no-build`。它會更新資料庫裡的密碼，再換掉 api 的容器（只重建 api：`POSTGRES_APP_PASSWORD` 現在只在 api 的環境裡，資料庫的容器不受影響）。
- **`POSTGRES_PASSWORD` 不要直接改**：它只在資料庫第一次建立時設定，之後改 `.env` 不會改到資料庫裡的密碼，api 的遷移與備份反而會連不上。

## 12. 換一台機器

把整套搬到另一台機器（或重灌）時，資料在三個地方，三個都要帶走：

1. 資料庫：用最新的備份還原（`backups/pmtool-*.dump`）。
2. **附件的檔案**：`backups/attachments/`（備份裡的）或 `data/attachments/`（正在用的），兩個都帶最保險；只帶一個的話帶 `backups/attachments/`，它是「出現過的所有檔案」。
3. `.env`（密碼，不進版控）。

做法：

1. 新機器安裝 Docker Desktop、clone 這個 repo、放好 `.env`。
2. 把舊機器的 `backups/` 整個複製到新機器的 `backups/`（保留修改時間：`rsync -a`），`.pmtool-project` 也一起過去。
3. 在新機器跑一次 `bash scripts/ops/deploy-local.sh`（全新的資料庫，會建立空的 `data/attachments/`）。
4. `bash scripts/ops/restore.sh backups/<最新的那份>`：資料庫還原之後，`backups/attachments/` 的檔案自動補進 `data/attachments/`。
5. 打開網頁，隨便開幾個附件確認讀得到；跑 `bash scripts/ops/status.sh`。

只搬資料夾而不還原資料庫的話（例如資料庫的資料卷是整個搬過去的）：把 `data/attachments/` 複製過去，Linux 上要是 1654 的擁有者（`sudo chown -R 1654:1654 data/attachments`），然後跑 `deploy-local.sh`，它會檢查寫入的權限。

## 13. Email 通知（寄信）

**設定**：組織管理員在網頁的「寄信設定」（側欄的「設定」裡）設定，存起來之前可以先按「寄測試信給我」。每個人可以在「設定 → 通知」關掉自己的 Email 通知。

- **Gmail**：用一個專門寄信的 Gmail 帳號。先在那個 Google 帳戶開兩步驟驗證，再到「應用程式密碼」（https://myaccount.google.com/apppasswords）產生一組 16 碼的密碼貼進設定頁；不是登入密碼。Google Workspace 的管理員可能關掉了應用程式密碼，那就改用公司的 SMTP 轉信。
- **其他 SMTP**：主機、連接埠（只能 25、465、587、2525）、加密方式（STARTTLS、SSL／TLS、不加密）、帳號密碼、寄件人地址。憑證一律驗證，自簽憑證的主機要換正式的憑證；不加密只適合內網裡不用登入的轉信。
- **主機不能在內網**：主機是私有網段、本機或 link-local 的 IP 時存不進去；主機名稱解析出來是這類位址時寄不出去（「寄測試信給我」會說「這個部署不連內網與本機的位址」）。公司內網的轉信主機要打開 `Notifications__Email__AllowPrivateHosts`：本機這一套是在 `.env` 加 `EMAIL_ALLOW_PRIVATE_HOSTS=true` 再跑 `deploy-local.sh`（`docs/DEPLOYMENT.md` 第 1.7 節）。
- **一天的上限**：Gmail 個人帳號一天約 500 封、Workspace 約 2,000 封。同一個人 5 分鐘內的通知併成一封，一般團隊用不到上限。

**什麼時候寄**：通知產生 5 分鐘後還沒讀才寄；超過 24 小時還寄不出去的就不寄了。開啟寄信之前的通知不補寄。

**寄不出去時**：

1. 看「寄信設定」頁上方：有「上次寄送失敗（時間）：原因」就照原因處理（帳號密碼、主機、連接埠）。頁面上的原因是整理過的分類，對方主機回的原文在 `errors.sh` 看得到的記錄裡。改好存起來，下一輪（30 秒內）就會再試；沒改的話，失敗後會等 1、2、5、10、30 分鐘再試。
2. `bash scripts/ops/errors.sh`：寄送的錯誤與警告在 `PmTool.Notifications` 開頭的類別。某一個收件人被拒收（信箱不存在、地址不對）只會略過那個人，記一筆 Warning，不影響別人。
3. 還原到新的機器、或弄丟 DataProtection 的金鑰之後，設定頁可能顯示「存的密碼解不開，請重新輸入」：重新輸入密碼存起來。

**在本機試寄信**（不寄到真的信箱）：起一個 Mailpit，接在演練的網路上，設定頁選「其他 SMTP」、主機填容器名稱、587、不加密：

Mailpit 的容器在私有網段，演練要帶 `DRILL_EMAIL_ALLOW_PRIVATE_HOSTS=true` 起（`DRILL_EMAIL_ALLOW_PRIVATE_HOSTS=true bash scripts/ops/restore-drill.sh --with-api`），不然設定頁不給存。

```
docker run -d --name pmtool-mailpit --network <演練的網路，例如 pm-tool-drill> -p 127.0.0.1:8026:8025 \
  -e MP_SMTP_BIND_ADDR=0.0.0.0:587 axllent/mailpit:v1.29
# 收到的信在 http://127.0.0.1:8026 看；用完 docker rm -f pmtool-mailpit
```

8080 那一套的人員是真的 Email：在那裡開啟寄信、指到真的寄信服務，裡面的人會真的收到信。只想試寄信的話，用演練環境加 Mailpit。


## 14. 即時更新

網頁開著時，別人改了項目（建立、修改、改狀態、留言、附件、刪除、復原），畫面不必重新整理就會更新；收件匣的未讀數也是。專案的設定（藍圖、成員）、週期與里程碑本身、專案說明不在裡面，照舊是切回分頁或換頁時重抓。

- **看它有沒有在動**：瀏覽器的開發者工具 → Network → 找 `live`，WebSocket 的那一筆是 101。斷線時網頁自己重連（0、2、5、10、30 秒，之後每 30 秒），重連之後把畫面上的資料全部重抓一次。
- **兩個人同時編輯同一段說明**：後存的會被擋下，網頁提示「這段說明在你編輯時被別人改過了」，可以看新的內容或用自己的版本覆蓋。其他欄位是後改的蓋掉先改的（以欄位為單位，改不同欄位不會互相影響）。
- **被移出專案、帳號被停用、登出、改密碼**：馬上斷線或不再收到那個專案的變動（多個 api 程序之間用資料庫的 `LISTEN/NOTIFY` 互相通知；那條通知的連線斷掉時退回每 2 分鐘檢查一次）。
- **不讀資料的連線會被斷開**：每一條連線各自送、各有 5 秒的期限，送不出去就斷開那一條，網頁自己重連再把畫面重抓一次。
- 本機的開發伺服器（Vite）已經把 `/v1/live` 的 WebSocket 代理到 8080。

## 15. AI 助理

**設定**：組織管理員在網頁的「AI 助理設定」（側欄）接上供應商（Anthropic、OpenAI、Google Gemini、OpenRouter，或 OpenAI 相容的端點）、填 API 金鑰（OpenRouter 也可以按「用 OpenRouter 登入」，登入後自動換到一把金鑰），按「讀取模型」勾選要開放的模型，再到「開放給成員」選預設的模型與思考深度、勾「開放組織的 API 金鑰給成員使用」。設定頁最上面的「AI 的四種接法」對照說明組織的 API 金鑰、主機 AI 代理、成員自己的 API 金鑰與本機代理各是誰能用、費用算誰。專案的成員在專案頁列的「AI 助理」使用，或按每一頁右下角的「AI 助理」叫出浮動的對話框（不在專案頁時先選專案），輸入框下面可以換模型與思考深度。

**自己的 AI 帳號**：管理員在「AI 助理設定」勾「允許成員使用自己的 AI 帳號」（預設不允許：專案的設定會送到成員自己選的供應商）之後，每個人可以在「設定 → 我的 AI 助理」接自己的供應商，只有自己能用、費用算在自己的帳號，模型選單裡標「（我的）」。組織沒開 AI 時，允許了的成員照樣能用自己的。關掉允許之後，已經接的列得出來、可以刪，但選不到。一個人最多 5 個。個人的「OpenAI 相容」端點一律不准連內部網路（`AI_ALLOW_PRIVATE_ENDPOINTS` 只放寬組織的連線）。

- 金鑰到供應商的主控台建立：Anthropic `https://platform.claude.com/settings/keys`、OpenAI `https://platform.openai.com/api-keys`、Google `https://aistudio.google.com/apikey`。費用算在那個帳號。
- 助理只讀專案的設定、模板與統計數字，**看不到項目的內容**；它能做的最多是提出變更集，要有權限的人在「變更集」頁核准才會生效（AI 提出的會標「AI 助理提出」）。
- 對話只有本人看得到，存到自己刪掉為止。

**用不了時**：

1. 專案頁列沒有「AI 助理」：組織沒開、沒有開放任何模型，或自己沒有讀專案設定的權限。
   右下角沒有「AI 助理」的按鈕：組織沒開（或沒有開放任何模型），而且也不允許使用自己的 AI 帳號。
2. 回覆下面顯示失敗的原因：「金鑰不對」「額度用完」「連不上」照原因到「AI 助理設定」頁處理（可以先按「重新讀取模型」試金鑰）；「模型拒絕了這個要求」換一個模型或換個說法。
3. `bash scripts/ops/errors.sh`：AI 的錯誤與警告在 `PmTool.Ai` 開頭的類別，記錄裡不會有金鑰與對話的內容。
4. 還原到新的機器、或弄丟 DataProtection 的金鑰之後：「存的金鑰解不開，請管理員重新輸入」，到「AI 助理設定」頁編輯連線重新輸入金鑰。
5. 「用 OpenRouter 登入」回來顯示沒有完成：登入的連結 10 分鐘內有效、只能由開始登入的那個帳號完成；在 OpenRouter 按了拒絕也會這樣。重新按一次登入。登入成功但連線數已滿時，OpenRouter 那邊已經建好的金鑰要到 `https://openrouter.ai/settings/keys` 封存。OpenRouter 回「額度不夠」（402）時到 OpenRouter 加值。

**選項目、建議修改**：在項目頁或開著項目的側邊面板時，AI 助理自動帶那個項目（也可以在助理的頁首搜尋換一個）。助理能讀項目的說明與留言（會送到選的 AI 供應商），要改的話只會提出「修改建議」卡片，使用者按「套用」才以自己的權限寫入，按「不要」就作廢；建議之後有人改了說明，套用會被擋下、什麼都不改，請重新請助理建議。本機代理要讀項目，權杖要有 `items:read`（「建立本機代理用的權杖」會帶；之前建立的要重建）。

**本機代理（成員自己電腦上的 Claude Code／Codex）**：成員在自己的電腦執行 `kumi agent start`（權杖要有 `agent:run`、`blueprint:read`、`blueprint:write`，「設定 → 我的 AI 助理」有按鈕建一把），網頁的模型選單就多出「Claude Code（電腦名稱）」。它只往外連（長輪詢 `/v1/agent/check-in`），伺服器不連進成員的電腦，主機不用開任何埠。用不了時：

1. 選單裡沒有：組織沒有允許使用自己的 AI 帳號，或那台電腦 60 秒內沒有登記（`kumi agent start` 沒開、網路斷了）；「設定 → 我的 AI 助理」看得到每台電腦在線或最後在線的時間。
2. 回覆失敗「你電腦上的 Kumi 代理沒有回應」：送出後 20 秒沒被領走（代理剛好關掉）；領走後 120 秒沒有回報（代理被關、網路斷了）。
3. 本機的錯誤看執行 `kumi agent start` 的那個終端機（每件工作一行，不印對話內容）；常見的是那台電腦的 `claude`／`codex` 沒登入、版本太舊（缺必要的旗標時不提供那個引擎）。
4. 伺服器的記錄在 `PmTool.Ai` 開頭的類別。殘留的工作與進度在回覆存完後刪掉，不需要手動清。

**連本機的模型（Ollama）**：8080 那一套的 api 在容器裡，`localhost` 連不到主機。供應商選「OpenAI 相容」，基底網址填 `http://host.docker.internal:11434/v1`，並在 `.env` 加 `AI_ALLOW_PRIVATE_ENDPOINTS=true` 後用 `deploy-local.sh` 重啟。

## 16. 用自己的 Claude Code／Codex 連（MCP）

使用者在自己的電腦上用 Claude Code 或 Codex（自己的訂閱）連 Kumi 的 MCP 伺服器，第一次連線時在瀏覽器登入 Kumi、按「允許」。步驟、範圍與撤銷見 `docs/MCP.md`；網頁「設定 → 命令列與整合」有這個部署的網址與可以複製的指令。

**連不上時**：

1. 用戶端說授權失敗：確認瀏覽器開的是 Kumi 的授權頁、登入的是對的帳號；授權頁顯示錯誤時照錯誤處理（例如「resource 不對」通常是用戶端的網址打錯，要是 `https://<網域>/mcp`）。
2. 授權過、之後連不上：到「設定 → 命令列與整合」的「已授權的 AI 代理」看還在不在（被撤銷、30 天沒用的要重新授權）；帳號停用也會失效。
3. `bash scripts/ops/errors.sh`：OAuth 的錯誤在 `PmTool.Identity` 開頭的類別、MCP 在 `PmTool.Api.Mcp`。記錄裡不會有權杖。
4. 本機（8080，沒有 HTTPS）也能接：`claude mcp add --transport http kumi http://localhost:8080/mcp`。


## 17. 主機 AI 代理（全組織共用的 Claude Code／Codex）

組織擁有者把 Claude Code／Codex 放在這台主機的 `ai-worker` 容器上，全組織的成員（擁有者、管理員、一般成員；訪客不行）在 AI 助理的模型選單選「主機上的 Claude Code」就能用，不必自己登入供應商、設金鑰、裝 CLI 或執行 `kumi agent start`。AI 讀寫 Kumi 的資料時用的是**發問的人自己的權限**，修改一樣只會提出變更集或修改建議，要人核准、按套用。規格見 `internal/HANDOFF-host-agent.md`。

**計費**：只能用 API 用量計費的登入（Claude Code 的 Anthropic Console、Codex 的 OpenAI API 金鑰），費用算在那個 Console 組織或 OpenAI 帳號。Claude（Pro／Max／Team／Enterprise）與 ChatGPT 的訂閱帳號依兩家的條款不能給全組織共用：在主機上用訂閱帳號登入，伺服器會拒絕、worker 會立刻登出。**這項判斷是信任 worker 自己回報的計費方式**（它讀 CLI 的登入狀態）：伺服器看不到 CLI 的登入內容，拿到機器憑證或掌控主機的人可以謊報。所以第一道防線是主機本身與機器憑證不外流（見下面「機器憑證」），不是這個檢查。

只有組織擁有者看得到「設定 → AI 助理主機代理」；管理員與一般成員沒有這個入口，直接打 API 也是 403。

### 第一次設定

1. 在 `.env` 加一行 `COMPOSE_PROFILES=ai-worker`，執行 `bash scripts/ops/deploy-local.sh`（會建 ai-worker 與出口代理 `ai-egress` 的映像檔並啟動；第一次要下載 Claude Code 與 Codex，幾分鐘）。`docker compose ps` 看得到 `ai-worker` 與 `ai-egress` 都是 Up；`docker compose logs ai-worker` 會一直說「等待配對」，這是正常的。
2. **配對**：擁有者在網頁「設定 → AI 助理主機代理」按「新增主機代理」、取個名字，畫面出現一次性的配對碼（15 分鐘內有效、只顯示一次）。在主機的終端機執行：

   ```
   docker compose exec -it ai-worker kumi host pair
   ```

   照提示輸入配對碼（碼不要寫在指令後面）。一分鐘內網頁上那台會變成「在線」。
3. **登入 Claude Code**：在那台的 Claude Code 一列按「開始登入」，畫面出現一次性的登入碼。在主機執行：

   ```
   docker compose exec -it ai-worker kumi host login claude-code
   ```

   輸入登入碼之後，接下來是 **Claude Code 自己的登入**（Anthropic Console）：它印出一個網址，在瀏覽器打開、登入 Console、授權，再把頁面上給的授權碼貼回這個終端機。網址與授權碼只在你和 Claude Code 之間，Kumi 不經手。完成後終端機說「已經授權給 Kumi」，一分鐘內網頁上那一列變成「可以接工作」。
4. **登入 Codex**（要用的話）：一樣按「開始登入」，在主機執行 `docker compose exec -it ai-worker kumi host login codex`，輸入登入碼後貼上 OpenAI 的 API 金鑰、按 Ctrl-D。金鑰只交給 codex（存在 ai-worker 的資料卷），不經過 Kumi。
5. **開來源**：按「新增來源」，選代理與引擎、名稱（成員在選單看到的，例如「主機上的 Claude Code」）、開放的模型、預設的模型與思考深度、上限（每人每天、每月的則數、每月的 token，空白是不限），勾「我了解這會以 API 用量計費」，打開「開放給成員使用」，儲存。
6. 任何一個成員開 AI 助理，模型選單就有「主機上的 Claude Code」。

### 平常

- 一個來源同時跑 1 件，其他人的排隊（畫面寫「排隊中，前面還有 N 件」）；每人在一個來源同時只能等 1 件；排隊超過 3 分鐘沒輪到就失敗、不算用量；開始之後最多跑 5 分鐘。
- 狀態：網頁的那一段每 5 秒更新（在線、引擎能不能接工作、執行中與排隊的件數、這個月的則數與 token）；「用量」看依成員、依專案的則數與 token（沒有對話的內容）。
- 記錄：`docker compose logs ai-worker` 每件工作一行（時間、專案、引擎、模型、結果、秒數），不印對話的內容、憑證與權杖。`docker compose exec ai-worker kumi host status` 看配對與兩個引擎的登入、計費（不印帳號資料）。
- 伺服器那一邊的錯誤在 `bash scripts/ops/errors.sh` 的 `PmTool.Ai` 類別。

### 重啟、重新授權、停用與復原

- **重啟**：`docker compose restart ai-worker`。正在跑的回覆以「中斷」結束（使用者看到「主機代理被關掉了」）；排隊中的等它回來（3 分鐘內）。主機重開機後 compose 自己把它帶起來（`restart: unless-stopped`），機器編號與登入都在資料卷裡，不用重新配對。
- **更新**：`bash scripts/ops/deploy-local.sh` 照常會重建 ai-worker；資料卷不受影響。
- **登入失效或被換掉**：網頁上那一列會變成「需要登入」或「需要擁有者重新授權」，來源停止派工作（成員的選單顯示「主機上的 … 需要組織擁有者重新授權」）。重做第一次設定的第 3（或 4）步即可。有人在 Kumi 以外直接登入（例如進容器用引擎帳號跑 `claude auth login`）換了帳號，worker 會發現帳號變了、不接工作，也要從網頁重新開始登入。
- **登出**：引擎那一列的「登出」：worker 會執行 CLI 自己的登出，排隊中的取消、執行中的停下。
- **停用**：主機的「更多 → 停用」（不派工作、排隊的取消、執行中的停下，再「啟用」就好）；只停一個來源用來源的「開放給成員使用」。**停用期間伺服器不認它的機器憑證**（一律 401）：worker 不會結束，記一句「伺服器不認這把機器憑證」並每 60 秒試一次，重新啟用之後同一把憑證自己接上，不必重新配對。
- **重新配對**（換了主機、懷疑機器憑證外流）：「更多 → 重新配對」，舊的憑證立刻失效，照第 2 步配對。
- **移除**：「更多 → 移除」，撤銷憑證與工作授權、刪掉它的來源；容器還開著的話會一直說「伺服器不認這把機器憑證」，用 `docker compose stop ai-worker` 停掉或重新新增、配對。
- **結果不明**：worker 在工作中途斷線（被強制結束、網路斷了）60 秒，那一則失敗並寫「結果不明」：它可能已經提出了變更集或修改建議，請到變更集與項目確認；**不會自動重跑**。

### 機器憑證

- **期限與換新**：配對時發的機器憑證（`kha_…`）90 天到期。worker 在線時每次登記，伺服器看到剩不到 30 天就在回應裡附一把新的，worker 等沒有工作在跑時存進狀態檔（權限 600）並改用它，舊的在新的第一次被用到時作廢；平常不用做任何事。主機關機太久、憑證真的過期了，網頁那台會顯示「憑證過期」，要重新配對。
- **機器編號**：worker 的每個請求（登記、領登入碼、回報、結束都一樣）都帶配對時的機器編號（標頭 `X-Kumi-Machine`，存在資料卷，重建容器也不變），登記的內容裡也有。沒帶、或和配對時的不同，一律被拒絕（401），並算進失敗額度；報了別的編號的，網頁那台顯示「有別台電腦拿這把憑證來登記」（登記的內容有電腦名稱時一併顯示）：憑證可能外流，請重新配對（重新配對會清掉警告）。**別台電腦只是被拒絕並留下警告，不會讓憑證失效：原本那台照常可用**；只有「配對的人不再是擁有者」與「憑證過期」會讓憑證真的不能用。舊版 kumi 不帶這個標頭，要先更新到 0.9.0 再用。
- **配對的人不再是擁有者**：配對這台的人被降成管理員或停用之後，憑證立刻不認（每個請求都問身分模組），網頁顯示「配對的人已經不是組織擁有者」，要由現在的擁有者重新配對（開始登入、認領登入的人同樣每次檢查現在是不是擁有者）。
- 這三種狀況在網頁上都是那台旁邊的警告（`warning`：`other_machine`、`paired_by_former_owner`、`credential_expired`）。拿到別台電腦的請求一律是 401；原本那台只有在後兩種狀況才會收到 401。

### 資料放在哪裡、怎麼備份

- 資料庫（`ai` schema 的 `host_*` 表：代理、來源、登入紀錄、用量）在平常的備份裡。對話與工作的內容在回覆存完後刪掉，用量只留則數與 token。
- `ai-worker-home` 資料卷（compose 專案的 `pm-tool_ai-worker-home`）放 kumi 的機器憑證（`/home/kumi/.kumi-host/host-agent.json`，kumi 帳號擁有、600）與 Claude Code 的登入（`/home/kumi/engine-claude/.claude`、`.claude.json`，Claude Code 的帳號擁有、700）與 Codex 的登入（`/home/kumi/engine-codex/.codex/auth.json`，Codex 的帳號擁有、700）。**刻意不備份、不要複製**：裡面是供應商的登入憑證，複製一份等於多一個能用帳號的地方。弄丟了（`docker compose down -v` 會刪掉它，所以不要用）就重新配對、重新登入，資料庫裡的設定與用量都還在。
- 還原到另一台主機：資料庫照第 6 節還原，ai-worker 的資料卷是空的，照第一次設定的第 2～4 步重新配對與登入（舊的那台會變成離線，用「移除」清掉）。

### 安全加固：三個帳號、出口代理、固定版本

AI 引擎讀的是別人寫在項目、留言裡的內容，可能被提示注入，所以假設它被騙去做壞事：不讓它讀到機器憑證，也不讓它把資料送到任意的地方。

**1. 三個帳號（kumi、Claude Code、Codex）**

| | kumi（uid 10001） | Claude Code（uid 10002，`engine-claude`） | Codex（uid 10003，`engine-codex`） |
|---|---|---|---|
| 跑什麼 | `kumi host run`（長輪詢、領工作、回報） | `claude`（每件工作一個程序，結束就沒了） | `codex`（同左） |
| 家目錄 | `/home/kumi`，狀態檔 `.kumi-host`（700）放機器憑證 `kha_…` | `/home/kumi/engine-claude`（700）：`.claude`、`.claude.json` | `/home/kumi/engine-codex`（700）：`.codex`（`CODEX_HOME`，登入是 `auth.json`） |
| 能做的事 | 讀寫自己的狀態檔；讀 Codex 的登入檔（比對帳號有沒有被換）；停掉引擎的程序 | 讀寫自己的家目錄；讀自己這件工作的 MCP 設定與系統提示；連 api 與出口代理 | 同左，家目錄是自己的 |
| 讀不到 | 引擎的內容之外沒有限制 | kumi 的狀態檔、kumi 的程序、**Codex 的家目錄與工作資料夾** | kumi 的狀態檔、kumi 的程序、**Claude Code 的家目錄與工作資料夾** |

三個帳號都沒有任何 Linux 能力（kumi 只留 4 個）、不能再提權；引擎之間沒有共用的檔案，連 uid、gid 都不同。

**為什麼兩個引擎各用自己的帳號，以及它成立的前提**：引擎讀的是別人寫的內容，可能被提示注入。如果兩個引擎共用一個帳號，同時在跑的兩件工作（一個 Claude Code、一個 Codex）就互相讀得到對方的家目錄（登入檔、API 金鑰）、工作資料夾與 `/proc/<pid>/environ`（裡面有那件工作的 MCP 權杖）。分開之後，被注入的那一件讀不到另一件的任何東西。**前提**：同時跑的兩件工作一定是不同的帳號。這由排程保證：每個來源同時只跑 1 件、一個引擎最多一個來源，所以同一個引擎同時最多 1 件；兩件同時跑的一定是不同引擎，也就是不同帳號。**如果之後改成同一個引擎可以同時跑多件，這個前提就破了**（兩件會共用同一個帳號），要先改成每件工作一個帳號或一個容器。

怎麼做到的（`deploy/ai-worker/libexec/`）：
- 容器以 root 啟動，ENTRYPOINT 與 `docker compose exec … kumi …` 都經過 `/usr/local/bin/kumi`：它先整理資料卷（擁有者與權限，見下面的升級），再用 `setpriv` 降成 kumi 帳號，**只留 4 個能力**（`setuid`、`setgid`、`kill`、`dac_read_search`），並附加群組 10002、10003（只用來把工作資料夾的群組改成那個引擎的，見下）。root 只存在那幾毫秒，compose 也只給容器 `CHOWN`、`DAC_OVERRIDE`、`DAC_READ_SEARCH`、`FOWNER`、`KILL`、`SETUID`、`SETGID`，其他全拿掉，另外 `no-new-privileges`、唯讀根目錄。
- `KUMI_AGENT_CLAUDE_PATH`／`KUMI_AGENT_CODEX_PATH` 指到 `libexec/claude`、`codex` 這兩個包裝檔，它們呼叫 `as-engine <claude|codex> …`，依引擎選帳號與家目錄（`HOME`、`CODEX_HOME=/home/kumi/engine-codex/.codex` 都由它設），再用 `setpriv` 切換成那個帳號、清掉所有能力才執行引擎。`CODEX_HOME` 也寫在映像檔的環境變數裡，kumi 讀 Codex 的 `auth.json`（帳號比對）用的是同一個路徑，kumi 的程式不用改。探測（`--version`、`auth status`）、跑工作、`kumi host login` 全走同一條路。
- **工作資料夾只開給那個引擎**：kumi 替每件工作建的暫存資料夾（`/tmp/kumi-agent-…`，700，裡面是 MCP 設定與系統提示，沒有機密，權杖在環境變數），`as-engine` 在執行前把它的群組改成那個引擎的帳號並開群組讀取（`chgrp`、`g+rX`，**其他人一律沒有權限**，另一個引擎也讀不到）。開放前檢查：路徑是 `/tmp` 底下名稱 `kumi-agent-` 開頭的直接子資料夾、是真的資料夾（不是符號連結）、擁有者是 kumi 自己；裡面有符號連結時只改連結本身、不跟隨。引擎對它只有讀取，不用寫入（2026-11-26 用真的 Claude Code 2.1.330、Codex 0.163.0 在唯讀的工作資料夾實測：兩個都正常連到供應商、只因為假金鑰被 401 拒絕，沒有任何寫入失敗）。
- **每次執行引擎之前，先清掉會被自動載入的持久檔案**（`as-engine` 以那個引擎的帳號刪）：被提示注入的引擎寫得進自己的家目錄，可以放一份全域記憶檔或指令，之後**每一件工作（包括別人發問的）都會載入它**，注入就跨工作持續下去。kumi 的旗標（`--setting-sources project`、`--ignore-user-config` 等）擋了大部分，但 Codex 的 `$CODEX_HOME/AGENTS.md` 沒有任何旗標關得掉，所以只能刪。清單：Claude Code 的 `~/.claude/CLAUDE.md`、`commands`、`skills`、`agents`、`rules`、`hooks`、`output-styles`、`plugins`、`settings.json`、`settings.local.json`、`projects`；Codex 的 `$CODEX_HOME/AGENTS.md`、`AGENTS.override.md`、`skills`、`prompts`、`rules`、`memories`、`agents`、`hooks.json`、`config.toml`。**登入檔不碰**（`.claude.json`、`.claude/.credentials.json`、`.codex/auth.json`）。實測：工作寫了 `AGENTS.md`、`CLAUDE.md`、指令與 hook，下一次執行引擎之前都不見了（連探測用的 `--version` 也算一次，所以幾乎是立刻）。限制：同一件工作裡引擎自己寫的檔案、與同一個引擎「同時」進行中的另一次呼叫仍然看得到；刪的是持久的部分。清單以兩個引擎實際會載入的東西為準，升級引擎版本時（第 3 點）要重看一次有沒有新的自動載入位置。
- 取捨：不用 sudo／setuid 程式（`no-new-privileges` 擋掉它們）、不拆成多個容器（kumi 要把工作的檔案和權杖交給引擎，同一個容器最簡單）。代價是 kumi 帶著 `setuid`／`setgid`，它被攻破時能變成容器裡的任何帳號（含 root，但 root 沒有其他能力、根目錄唯讀）；這個程式不讀引擎的輸出以外的任何外部輸入，風險比引擎低得多。
- **驗 kumi 端的能力**（部署後做一次）：`docker compose exec ai-worker sh -c 'for p in /proc/[0-9]*; do [ "$(cat $p/comm)" = kumi ] && grep -E "^(Uid|Groups|CapEff|CapAmb)" $p/status; done'`。該看到 `Uid` 是 10001、`Groups` 是 10002 10003、**`CapEff` 與 `CapAmb` 都是 `00000000000000e4`**：把它拆成位元就是 2（`dac_read_search`）、5（`kill`）、6（`setgid`）、7（`setuid`）這四個，多一個或少一個都不對。（`docker-init` 與 `sh` 是 root，會顯示容器一開始的 `00000000000000ef`，那是正常的，只有 kumi 的程序要比。）

**2. 出口代理（`ai-egress`）**

- `ai-worker` 只接 `ai-worker` 這個網路，它在 compose 裡是 `internal: true`：沒有通往外網的路，連 DNS 都解析不到外面的名字。`ai-egress`（squid，固定 digest 的 Alpine，非 root、唯讀根目錄）同時接這個內部網路與 `ai-egress` 網路（唯一通外網的），ai-worker 的 `HTTPS_PROXY`、`HTTP_PROXY` 指到它，`NO_PROXY=api` 讓 MCP 的流量直接連 api。Claude Code 與 Codex 都照環境變數走代理（2026-11-26 實測，連 Codex 的 WebSocket 也走 `CONNECT`）。
- 代理只放行 **CONNECT 到 443 埠、目的是「網域名稱」且在 `deploy/ai-worker/egress-proxy/allowed-domains.txt` 清單裡**的連線，其他一律 403；**目的是 IP（IPv4、IPv6、帶中括號的、十進位或十六進位的寫法）一律拒絕**，不進網域比對；解析到私有、本機或 link-local 位址的也拒絕（它不是通往內網的跳板）。為什麼：squid 對 IP 做網域比對時會去反查 IP 的名稱（PTR），那筆紀錄是 IP 的擁有者說了算，攻擊者可以把自己主機的 PTR 設成 `api.anthropic.com`，用 IP 直接連進來繞過清單。實測：經代理連 `https://api.anthropic.com` 通（404，是 API 自己回的）、連它的 IP（IPv4 與 IPv6）與十進位的 IP 都是 `403`。現在放行：`api.anthropic.com`、`console.anthropic.com`、`platform.claude.com`（Console 登入的授權）、`api.openai.com`。
- 引擎還會順便連別的網域（遙測、更新檢查：`http-intake.logs.us5.datadoghq.com`、`downloads.claude.ai`、`chatgpt.com`、`api.github.com`），被擋下不影響使用，**不要為了讓這些記錄變乾淨而放行**。
- **要連新的網域**（例如 `AI_WORKER_SERVER` 換成對外的網址，或供應商換了網域）：編輯 `allowed-domains.txt`（一行一個，開頭加點是連子網域），`docker compose restart ai-egress`，不用重建。想放行之前先想清楚：這份清單就是 AI 引擎能把資料送去的地方。**清單上的供應商本身也是出口**：被注入的引擎可以拿「別的帳號的金鑰」打供應商的 API（例如把資料放進它自己準備好的 Anthropic 或 OpenAI 帳號的對話），代理分不出來，網域清單擋不住這件事。能做的是縮小範圍：引擎只拿得到自己那件工作的資料與自己的金鑰（上面的兩個帳號分開、工作資料夾只開給自己、每次清掉持久檔案），拿不到另一件工作或另一個引擎的東西；金鑰用專用的、設了用量上限的 API 帳號。`AI_WORKER_SERVER` 設成對外的 https 網址時，kumi 自己也走代理，那個主機名稱一定要加進清單。
- **確認**：
  - 看代理的記錄：`docker compose logs ai-egress`，`TCP_TUNNEL/200` 是放行、`TCP_DENIED/403` 是擋下，每一行有目的網域。
  - 不經代理連不出去（在容器裡以引擎帳號測，下面兩行都該失敗）：`docker compose exec ai-worker setpriv --reuid=10002 --regid=10002 --clear-groups --inh-caps=-all --ambient-caps=-all node -e "require('net').connect(443,'1.1.1.1').on('error',e=>console.log(e.code))"`（`ENETUNREACH`）、把 `1.1.1.1` 換成 `example.com`（`EAI_AGAIN`）。
  - 目的是 IP 被拒絕：`curl -x http://<ai-egress 的位址>:3128 -k -o /dev/null -w '%{http_code}\n' https://<api.anthropic.com 的 IP>/` 該是 403（`docker compose logs ai-egress` 看得到 `TCP_DENIED/403 … CONNECT <IP>:443`），把網址換成 `https://api.anthropic.com/` 才通。
  - 引擎帳號讀不到憑證：`docker compose exec ai-worker setpriv --reuid=10002 --regid=10002 --clear-groups --inh-caps=-all --ambient-caps=-all cat /home/kumi/.kumi-host/host-agent.json`（Permission denied）；換成 `--reuid=10003 --regid=10003` 讀 `/home/kumi/engine-claude/.claude.json`（Claude Code 的登入）也是 Permission denied，兩個引擎讀不到彼此。
  - `docker compose exec ai-worker ps -eo user,pid,args`：kumi 的程序是 `kumi`，跑工作時引擎的程序是 `engine-claude` 或 `engine-codex`。

**3. 引擎的版本（lockfile）**

`deploy/ai-worker/package.json`（釘死的版本）與 `package-lock.json`（整棵依賴樹與每個檔案的雜湊，含各平台的原生套件）；Dockerfile 用 `npm ci` 照 lockfile 安裝，所以什麼時候建都是同一份內容。**升級引擎**（本機代理的旗標與輸出格式是在這兩個版本上驗過的，升級前要重驗，見 `internal/HANDOFF-local-agent.md` 第 5 節）：改 `package.json` 的版本，在 `deploy/ai-worker/` 執行 `npm install --package-lock-only --ignore-scripts`（用和 Dockerfile 同一版的 node 比較保險：`docker run --rm -v "$PWD":/w -w /w node:24-bookworm-slim npm install --package-lock-only --ignore-scripts`），重建映像檔，`kumi host status` 看版本與探測結果。

**4. 從舊版升上來（資料卷已經有東西）**

有兩種舊的資料卷，新版的啟動器**每次啟動都會**處理，兩種都不用重新配對、重新登入（重跑沒有副作用）：
- **最舊的**（引擎與 kumi 同一個帳號，登入的 `.claude`、`.claude.json`、`.codex` 直接放在 `/home/kumi`）；
- **上一版**（兩個引擎共用一個帳號與 `/home/kumi/engine`）。

啟動器把 `.claude`、`.claude.json` 搬進 `/home/kumi/engine-claude`、`.codex` 搬進 `/home/kumi/engine-codex`（目的地已經有同名的就不覆蓋），搬進去的東西裡的符號連結一律刪掉，兩個家目錄各自改成自己的擁有者（700），`.kumi-host` 設成 kumi 專用（700／600），`/home/kumi` 是 711。上一版的 `/home/kumi/engine` 搬完後如果還有剩的東西（沒搬走的），改成 root 擁有、700：不屬於任何引擎帳號，也不刪。

**照平常更新就好**：`bash scripts/ops/deploy-local.sh`（`.env` 有 `COMPOSE_PROFILES=ai-worker`）。2026-11-26 在演練環境（專案 `pmtool-fx`、連接埠 18083，不碰 8080）從上一個提交 `ae694b1` 的 compose 換到這一版，實測的結果：
- 舊版的 `ai-worker` 網路不是 `internal`，新版是。`up` 會停掉 `api` 與 `ai-worker`、刪掉舊的網路、建新的網路再重建容器（compose 自己處理，**沒有卡住**，不用手動 `down` 或 `docker network rm`）。`api` 因此會短暫停一下（和平常換版一樣）。
- 資料卷 `ai-worker-home` 與配對**都還在**：換版之後 `kumi host run` 說「已配對」，網頁上那台一分鐘內回到在線，版本變新；舊佈局的 `.claude`、`.codex/auth.json` 分別出現在 `engine-claude`、`engine-codex`，Codex 的登入照樣被認得（`billing: api`）。不需要任何額外的步驟。
- 舊版的 `.codex/AGENTS.md`（人為放進去的）在第一次探測 Codex 時被清掉（上面第 1 點）。如果你自己在引擎的家目錄放過全域記憶檔想保留，換版之前先備份起來；之後引擎的家目錄不是拿來放這種東西的。
- 手動進容器操作時用 `docker compose exec ai-worker kumi …`（會降成 kumi 帳號）。**不要**用 root 直接跑 `claude`、`codex` 或 `/opt/kumi/kumi`：它們會在 `/root` 或以 root 擁有的檔案寫下登入、狀態。

### 用不了時

| 網頁上看到 | 怎麼辦 |
|---|---|
| 一直「等待配對」或「離線」 | `docker compose ps` 看 ai-worker 有沒有起來；`docker compose logs ai-worker` 看最後幾行（等待配對、伺服器不認機器憑證、連不上伺服器、伺服器要求 https）。 |
| 那台旁邊有警告（別台電腦、配對的人不再是擁有者、憑證過期） | 「更多 → 重新配對」，照第一次設定的第 2 步。別台電腦的警告表示憑證可能外流，重新配對之後也檢查一下主機的存取。 |
| 需要登入、需要擁有者重新授權 | 重新「開始登入」、在主機執行 `kumi host login`。 |
| 訂閱帳號不能共用、判斷不出計費方式 | 改用 Console／API 金鑰登入。 |
| 版本不支援、沒有安裝 | 映像檔裡的 CLI 壞了或被換掉：`bash scripts/ops/deploy-local.sh` 重建。 |
| 工作都失敗、引擎說連不上供應商 | `docker compose ps` 看 `ai-egress` 有沒有起來；`docker compose logs ai-egress` 看供應商的網域是不是被 `TCP_DENIED` 擋了（要加進 `allowed-domains.txt`，見上面）。 |
| 成員送出時「排隊滿了」「次數已經用完」 | 等前一則結束；上限在來源的設定裡調。 |
| 成員在「不選專案」的對話送出時收到 `ai.agent_update_required`（409） | ai-worker 裡的 kumi 太舊：不選專案的對話要 0.12.0 以上，舊版會被擋下、什麼都不存。更新映像檔重建 ai-worker（`bash scripts/ops/deploy-local.sh`）；專案裡的對話不受影響。 |

## 18. 附件的容量

項目的附件與知識庫的附件合在一起算，有兩個上限（`docs/DEPLOYMENT.md` 第 1.6 節）：

| 上限 | 預設 | 本機的 `.env` |
|---|---|---|
| 每個組織的總量 | 20 GiB | `ATTACHMENT_QUOTA_ORGANIZATION_GIB` |
| 每個人過去 24 小時上傳的量 | 2 GiB | `ATTACHMENT_QUOTA_PERSON_DAILY_GIB` |

- 組織管理員在網頁的「設定 → 儲存空間」看用量；到了上限，新的上傳會被擋下並說明是哪一個上限，已經上傳的照常讀得到。
- **算進去的是資料庫裡還在的附件**：刪掉項目的附件會讓出空間；知識庫的附件沒有單獨刪除，要管理者在垃圾桶「永久刪除」那一批頁面才會讓出來。
- 要調高：改 `.env` 再跑 `bash scripts/ops/deploy-local.sh --no-build`。值是 0 以上的整數，`0` 是不限；寫錯時 api 起不來，訊息會說是哪一個設定。
- **上限不等於磁碟還有空間**：`status.sh` 的「附件資料夾的磁碟剩餘」低於 5 GiB 或 10% 時列為問題。附件、資料庫與備份在同一顆碟時，滿了整套會一起停。

## 19. 帳號可能被別人用著時

懷疑有人拿到自己的密碼或登入狀態時：

1. 到「我的帳號」改密碼：別的裝置全部登出，預設連個人存取權杖一起撤銷，OAuth 的授權一律撤銷；還沒兌換的命令列登入碼作廢。
2. 只想把別的裝置登出、不改密碼：「登出其他裝置」（要輸入目前的密碼）。
3. 到「命令列與整合」看存取權杖的清單（有建立時的來源位址與電腦名稱），不認得的撤銷；收件匣的帳號通知會列出最近建立的權杖、命令列登入與授權。
4. 組織擁有者在人員頁下方的稽核紀錄看得到登入、改密碼、建立與撤銷權杖的紀錄。

**改密碼一直被擋（「太頻繁」）時**：改密碼與登出其他裝置共用每個人每分鐘的次數，拿著登入狀態的另一個人不斷嘗試時，本人的操作也會被擋。這時直接登出再登入（登入的額度是分開算的）之後馬上改密碼；還是不行就請組織管理員重設密碼或先停用帳號（兩者都會立刻作廢所有的工作階段與登入碼）。

## 20. Data Protection 憑證（加密 Cookie 與 AI、寄信密碼的金鑰）

工作階段 Cookie 的簽章金鑰存在資料庫裡（`identity.data_protection_keys`），同一組金鑰也加密 AI 供應商的金鑰與寄信的密碼。**沒有設憑證時這些金鑰是明文的**：拿到資料庫或備份的人解得開所有人的 Cookie、AI 金鑰與寄信密碼（記錄裡每次啟動都有一筆 Warning）。設了憑證，之後產生的金鑰用憑證加密，備份外流時不會跟著外流。上雲端的版本與換憑證的細節看 `docs/DEPLOYMENT.md` 第 1.4 節；這一節是本機這一套的做法。

**限制**：憑證檔與它的密碼放在同一台主機上（密碼在 `.env`）。它防的是「資料庫或備份外流」，不是「主機被入侵」；`docker inspect` 看得到 api 的環境變數裡的密碼（程式只認環境變數），所以能操作這台 Docker 的人拿得到它。

### 產生與放哪裡

放在 repo **外面**、只有自己讀得到的資料夾（不要放在 `backups/` 或 repo 底下）。RSA（Data Protection 用憑證的 RSA 金鑰，不能用 EC）：

```
mkdir -m 700 ~/kumi-secrets
(
  umask 077
  cd ~/kumi-secrets
  openssl rand -hex 24 | tr -d '\n' > dataprotection.password
  openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
    -subj "/CN=Kumi DataProtection" -keyout dp.key -out dp.crt
  openssl pkcs12 -export -inkey dp.key -in dp.crt \
    -passout file:dataprotection.password -out dataprotection.pfx
  rm dp.key dp.crt
)
```

- 密碼檔結尾不能有換行（上面用 `tr -d '\n'` 拿掉了）：它整個是密碼。
- **另外保存一份**（密碼管理器，或另一顆磁碟）：`dataprotection.pfx` 和 `dataprotection.password` 兩個都要。**憑證丟了，用它加密的金鑰就解不開**：所有人重新登入，AI 金鑰與寄信密碼要重新輸入（資料不受影響）。不要和 `backups/` 放在同一個地方，不然加密就白做了。
- Linux：api 容器以 UID 1654 執行，**資料夾要讓它進得去、檔案要讓它讀得到**。只改檔案的擁有者不夠：資料夾是 700、擁有者是你，1654 連資料夾都進不去，api 會因為「憑證掛不上」起不來（`REQUIRE_CERTIFICATE=true` 時是啟動失敗）。兩種做法擇一：
  - 擁有者換成 1654（只有 1654 與 root 進得去）：`sudo chown -R 1654 ~/kumi-secrets && sudo chmod 500 ~/kumi-secrets && sudo chmod 400 ~/kumi-secrets/dataprotection.pfx`。之後你自己要改這個資料夾要用 sudo；`dataprotection.password` 是給你自己存放用的，api 不讀它（密碼在 `.env`），可以留在這裡，只是同樣只有 1654 與 root 讀得到。
  - 擁有者留給自己，只開給 1654（要有 ACL 工具）：`chmod 711 ~/kumi-secrets && sudo setfacl -m u:1654:r ~/kumi-secrets/dataprotection.pfx`。資料夾 711 讓別的帳號只能「穿過去」、列不出裡面有什麼；檔案只有你與 1654 讀得到。沒有 ACL 時用上一種。
  macOS 的 Docker Desktop 不看擁有者，不用做。

### 設上去

1. **先備份**：`bash scripts/ops/backup-now.sh before-dp-cert`。
2. 在 `.env` 加（`DATAPROTECTION_DIR` 寫絕對路徑，資料夾會唯讀掛進 `api` 與 `migrate` 的 `/run/secrets/dp`；`DATAPROTECTION_CERT_FILE` 是裡面的檔名）：

   ```
   DATAPROTECTION_DIR=/Users/<你的帳號>/kumi-secrets
   DATAPROTECTION_CERT_FILE=dataprotection.pfx
   DATAPROTECTION_CERT_PASSWORD=<dataprotection.password 的內容>
   DATAPROTECTION_REQUIRE_CERTIFICATE=true
   ```

   `REQUIRE_CERTIFICATE=true` 讓「憑證掛不上」變成啟動失敗，而不是悄悄退回明文。密碼只用英文字母與數字（上面 `openssl rand -hex` 產生的就是）。
3. `bash scripts/ops/deploy-local.sh --no-build`。api 起得來、記錄裡沒有「沒有設定 DataProtection:CertificatePath」的 Warning，就設好了。

沒設 `DATAPROTECTION_CERT_FILE` 時照舊不用憑證（`DATAPROTECTION_DIR` 預設掛 repo 裡一個空的資料夾）。

### 設了之後會怎樣（2026-11-26 在演練環境實測）

**只設憑證**：什麼都沒壞：
- 沒有人被登出：設之前建立的工作階段照常有效。
- AI 金鑰、寄信密碼不用重新輸入：現有的那把金鑰仍是明文、仍在使用，照樣解得開。
- Warning 消失。**但現有的金鑰不會被重新加密**：資料庫與舊備份裡還是明文的那一把，要等它到期（金鑰的有效期 90 天，到期前約 2 天產生新的）、新的那把才用憑證加密。

**要現有的金鑰也立刻換成加密的**（資料庫或備份可能已經外流過，或不想等 90 天）：在上面三步之後，另外做這一段。**後果：所有人被登出、AI 供應商的金鑰與寄信密碼要重新輸入**（舊金鑰刪掉之後，用它加密的密文解不開；實測：新的金鑰是憑證加密的，寄信設定還顯示「已有密碼」但解不開，要重新輸入）：

1. 先 `bash scripts/ops/backup-now.sh before-dp-rotate`；把 AI 連線與寄信設定的內容（供應商、帳號、網址）記下來，金鑰與密碼要準備好重新貼。
2. 用擁有者連上資料庫，一次做完：

   ```
   docker compose exec db psql -U pmtool -d pmtool -c "BEGIN; DELETE FROM identity.data_protection_keys; UPDATE identity.sessions SET revoked_at = now(), revoked_reason = 'key-rotated' WHERE revoked_at IS NULL; COMMIT;"
   docker compose restart api
   ```

3. 重新登入；組織管理員到「AI 助理設定」把每個連線的金鑰重新貼一次、到「寄信設定」重新輸入密碼。
4. 舊的備份裡還有舊的明文金鑰：不再需要的就刪掉，或用備份加密（第 3 節）保護；第 3 節新做的備份是加密金鑰的版本。

### 換憑證

定期換（沒有外流）：用**同一個密碼**產生新的 PFX，新的當成 `DATAPROTECTION_CERT_FILE`、舊的檔名填 `DATAPROTECTION_PREVIOUS_CERT_FILE`（放在同一個 `DATAPROTECTION_DIR` 裡），再 `deploy-local.sh --no-build`。舊憑證留到 97 天之後（金鑰有效期 90 天加工作階段的絕對期限 7 天）；不確定就一直留著。憑證外流的做法照 `docs/DEPLOYMENT.md` 第 1.4 節的 B（上面「立刻換成加密的」那一段就是它的本機版）。
