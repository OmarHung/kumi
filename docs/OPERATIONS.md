# 本機自用的操作手冊

這份寫給在自己的 Mac 上跑 pm-tool、自己照顧它的人。

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
3. `docker compose up --build -d`：建置新的映像檔，換掉有變動的容器。
4. 等 `/health/ready` 回應，最多 180 秒。新版的資料表遷移在這一步跑，跑完把讀寫的權限授予 `pmtool_app`。

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

### 從兩個角色之前的版本更新過來（只有一次）

這一版起，api 平常用執行用的角色 `pmtool_app` 連資料庫，工作階段 Cookie 的金鑰改存在資料庫裡。第一次更新過來時：

1. 先在 `.env` 加上 `POSTGRES_APP_PASSWORD`（`openssl rand -hex 24` 產生）。沒加的話 `deploy-local.sh` 會停下來說明。
2. 跑 `bash scripts/ops/deploy-local.sh`。會看到：
   - 資料庫的容器重建一次：PostgreSQL 的映像檔固定到 digest（可能順便換成比較新的 18 小版本，小版本不用搬資料），健康檢查也改成用 TCP 問。執行用的角色不是靠初始化的目錄建的，而是由 `deploy-local.sh` 在啟動 api 之前建立。資料在資料卷裡，不受影響。
   - 「資料庫與執行用的角色」這一步建立 `pmtool_app`。
   - api 啟動時遷移，並把讀寫的權限授予 `pmtool_app`。
3. **每個人要重新登入一次**：舊的金鑰在 `api-keys` 資料卷裡，新版不讀它，換到資料庫裡的新金鑰之後，舊的 Cookie 都解不開。
4. 確認新版正常、大家都重新登入過之後，舊的資料卷可以手動刪掉（只刪這一個，資料庫的資料卷不要動）：

   ```
   docker volume rm pm-tool_api-keys
   ```

## 3. 備份

**在哪裡**：repo 根目錄的 `backups/`（`.env` 的 `BACKUP_DIR` 可以改）。

- 它在 Docker 的資料卷外面：`docker compose down -v` 刪不到它。刪掉整個 repo 資料夾就會一起刪掉。
- 目錄權限 700、檔案 600。檔案沒有加密，裡面是真實資料。
- **權限擋不住這台 Mac 的其他帳號**：repo 放在外接碟上時，macOS 預設用「忽略擁有者」（`noowners`）掛載外接碟，這台 Mac 上每個本機帳號都被當成擁有者。要更嚴格，把備份放在內接碟的家目錄底下，`.env` 寫絕對路徑（不能用 `~`），例如 `BACKUP_DIR=/Users/<你的帳號>/pm-tool-backups`。換目錄之後把舊的備份搬過去（`rsync -a backups/ /Users/<你的帳號>/pm-tool-backups/`，`.pmtool-project` 也會一起過去），再讓 backup 服務換上新的目錄：`docker compose up -d --no-build backup`。
- 被 git 忽略，不會進版控。
- 目錄裡的 `.pmtool-project` 寫著這個目錄屬於哪一套（例如 `pm-tool`）。不要刪，也不要讓兩套共用一個目錄（見第 9 節）。

**附件的檔案也在備份裡**：項目上傳的檔案放在附件資料夾（下面「附件」），不在資料庫裡，所以備份分兩部分：資料庫是 `pmtool-*.dump`，檔案是同一個目錄底下的 `attachments/`。

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

**檔名**：`pmtool-YYYYMMDD-HHMMSS.dump` 是排程備份；`pmtool-YYYYMMDD-HHMMSS-標籤.dump` 是帶標籤的（`manual` 手動、`pre-deploy` 部署前、`pre-restore` 還原前）。時間用 `.env` 的 `TZ`。

**自動備份**：`backup` 服務每小時檢查一次，最新的排程備份超過 24 小時（`BACKUP_INTERVAL_HOURS`）就做一份。

- Mac 睡著錯過時間也沒關係：醒來後一小時內會補做。
- 排程備份只留最新的 14 份（`BACKUP_KEEP`），多的自動刪掉。
- 帶標籤的備份不算在 14 份裡，也**不會自動刪掉**。偶爾自己清一下舊的。
- 每一份都先寫成暫存檔，用 `pg_restore` 從頭到尾讀過一遍，確認讀得回來，才改成正式的名字。目錄裡看得到的備份都是完整的。

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
rsync -a backups/ "/Volumes/<另一顆磁碟>/pm-tool-backups/"
```

- `rsync -a` 會保留 600 的權限與檔案的修改時間。不加 `--delete`，舊的備份在另一邊會一直留著，自己清。
- `backups/` 底下的 `attachments/` 是附件的備份，一起複製出去就好（上面那行已經包含）。不加 `--delete` 也剛好符合它「只增不刪」的做法。
- 從別的磁碟複製回來時也要保留修改時間：用 `rsync -a` 或 `cp -p`。哪一份比較新、要刪哪一份、演練預設用哪一份，都是看修改時間；用一般的 `cp`，複製回來的舊備份會被當成最新的。
- 外接碟預設不在 Time Machine 的備份範圍裡，不要以為它有備到。
- 要放到雲端硬碟，先想清楚：備份檔沒有加密。

## 5. 還原演練（建議每個月一次）

確認備份真的讀得回來。演練在另外起的、用完就丟的資料庫裡做，不碰正在用的那一套。

```
bash scripts/ops/restore-drill.sh                            # 用最新的一份
bash scripts/ops/restore-drill.sh backups/<檔名>              # 指定一份
```

它會列出每張表的筆數，最後說「演練成功」，然後自己清掉演練的容器。不帶 `--with-api` 時只驗資料庫，不碰附件的檔案。

**想登入看看資料**：

```
bash scripts/ops/restore-drill.sh --with-api
```

- 開 <http://127.0.0.1:8081>，用原本的帳號登入。只看，不要改：演練的資料用完就丟。
- **用 `127.0.0.1`，不要用 `localhost`。** Cookie 不分連接埠，用 `localhost:8081` 登入會把 `localhost:8080` 的登入蓋掉。
- **附件**：演練的 api 用自己的一份附件資料夾，放在暫存目錄（`$TMPDIR/<專案>-drill-attachments`，macOS 是 `/var/folders/…`），從備份目錄的 `attachments/` 複製來；演練的 api 的 `Attachments__Directory` 指到它，不碰正在用的那個資料夾。可以在演練的網頁上打開附件，確認備份裡的檔案真的讀得到。`--down` 時一起刪掉。
- 它用 `deploy-local.sh` 建好的 API 映像檔（`pm-tool-api`）。找不到時會叫你先建置。
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

- 它先把備份檔複製進資料庫的容器、從頭到尾讀過一遍。讀不完就停下來，什麼都不動。
- 接著說明要做什麼，要你打出專案名稱（`pm-tool`）確認。打錯就什麼都不做。
- 順序：先替現況備份一份（`pre-restore`）→ 停 api 與 backup → 刪掉並重建資料庫 → 還原 → 作廢所有的登入 → 把 `backups/attachments/` 的檔案補回附件資料夾 → 啟動 api 與 backup → 等就緒。
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
- 真的還原時，權限由 api 啟動時的遷移重新授予 `pmtool_app`，和備份是哪一版做的、當時有沒有這個角色都無關。

還原到**新的**資料庫伺服器（演練、換機器、上雲）時，事件流自己處理交易編號：

- 事件的位置是（交易編號、流水號）。新的伺服器交易編號從頭算起，比備份裡的事件與讀者存的位置都小，新的事件會一直排在讀者的位置前面，通知就停了（2026-11-19 演練時發現）。
- 現在 api 讀事件時，若發現有事件的交易編號比資料庫現在的還大（同一台伺服器上不可能），就把這些歷史事件的交易編號歸成 1；讀的人（通知）讀出自己存的位置時也先換算，比現在還大的當成（1、原本的流水號）並存回去。log 會有一筆 Warning 說「資料庫看起來是從別處還原的」。不用另外做什麼。
- 本機的 `restore.sh` 是在同一台伺服器上重建資料庫，交易編號會接著往上，不會遇到。
- 邊界：備份當下若剛好有兩個寫入同時進行、順序交錯，還原後那一兩筆通知可能重送或漏送一次。

## 7. 看錯誤

`status.sh` 會列出 api 過去 24 小時有幾筆 Error／Critical。有的話看細節：

```
bash scripts/ops/errors.sh               # 過去 24 小時的 Warning 以上
bash scripts/ops/errors.sh --since 2h    # 也可以寫 30m、2026-11-17T09:00
```

每一筆有時間、等級、類別、訊息、例外的第一行、請求的路徑。網頁回報的錯誤也在裡面：標著「網頁回報」，改列種類、網頁的訊息與網頁的路徑。`status.sh` 的錯誤筆數也算它們，另外寫出其中網頁回報幾筆。

- api 的記錄是一行一筆的 JSON。看原始的記錄：`docker compose logs --since 1h api`。
- 每個容器的記錄每份 10 MB、留 5 份，更早的會被輪替掉。部署換掉 api 的容器之後，舊容器的記錄也跟著不見。要留證據，先存下來：`docker compose logs api > api.log`。
- 這幾種每次啟動都會出現，可以不管：
  - Warning「沒有設定 DataProtection:CertificatePath：工作階段 Cookie 的加密金鑰以明文存在資料庫裡…」（產生新金鑰時還有一筆「No XML encryptor configured」）：本機沒有設憑證。本機自用可以接受；上雲端要設（`docs/DEPLOYMENT.md` 第 1.4 節）。
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
- 限流與工作階段的作廢跨容器共用（現在只能開一個 api 容器）。
- 強制改密碼、登入中的裝置清單、雙重驗證。
- 錯誤監控接外部服務（例如 Sentry），出錯時主動通知；現在要自己跑 `status.sh` 才知道。
- 備份自動複製到別的地方並加密；現在要自己複製（第 4 節）。附件的檔案也一樣（`backups/attachments/` 要一起複製）。
- 附件沒有病毒掃描、沒有縮圖、沒有版本管理；備份裡的附件只增不刪，空間要自己留意（`status.sh` 的「附件」列出大小）。

## 11. 資料庫的兩個角色

| 角色 | 密碼（`.env`） | 誰用 | 能做什麼 |
|---|---|---|---|
| `pmtool` | `POSTGRES_PASSWORD` | api 的遷移、backup 服務、`restore.sh` | 資料表的擁有者，本機也是超級使用者 |
| `pmtool_app` | `POSTGRES_APP_PASSWORD` | api 平常的每一個請求 | 只能讀寫資料：不能改資料表結構、不能建資料庫與角色 |

- api 被攻破時，用的是 `pmtool_app`，改不了資料表結構。
- 讀寫的權限不是手動給的：api 每次啟動、遷移完，把每個模組的資料表授予 `pmtool_app`（`Database__RuntimeRole`）。
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
- **一天的上限**：Gmail 個人帳號一天約 500 封、Workspace 約 2,000 封。同一個人 5 分鐘內的通知併成一封，一般團隊用不到上限。

**什麼時候寄**：通知產生 5 分鐘後還沒讀才寄；超過 24 小時還寄不出去的就不寄了。開啟寄信之前的通知不補寄。

**寄不出去時**：

1. 看「寄信設定」頁上方：有「上次寄送失敗（時間）：原因」就照原因處理（帳號密碼、主機、連接埠）。改好存起來，下一輪（30 秒內）就會再試；沒改的話，失敗後會等 1、2、5、10、30 分鐘再試。
2. `bash scripts/ops/errors.sh`：寄送的錯誤與警告在 `PmTool.Notifications` 開頭的類別。某一個收件人被拒收（信箱不存在、地址不對）只會略過那個人，記一筆 Warning，不影響別人。
3. 還原到新的機器、或弄丟 DataProtection 的金鑰之後，設定頁可能顯示「存的密碼解不開，請重新輸入」：重新輸入密碼存起來。

**在本機試寄信**（不寄到真的信箱）：起一個 Mailpit，接在演練的網路上，設定頁選「其他 SMTP」、主機填容器名稱、587、不加密：

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
- **被移出專案、帳號被停用、登出**：最多 2 分鐘內斷線或不再收到那個專案的變動。
- 本機的開發伺服器（Vite）已經把 `/v1/live` 的 WebSocket 代理到 8080。

## 15. AI 助理

**設定**：組織管理員在網頁的「AI 設定」（側欄）接上供應商（Anthropic、OpenAI、Google Gemini、OpenRouter，或 OpenAI 相容的端點）、填 API 金鑰（OpenRouter 也可以按「用 OpenRouter 登入」，登入後自動換到一把金鑰），按「讀取模型」勾選要開放的模型，再選預設的模型與思考深度、勾「啟用 AI 助理」。專案的成員在專案頁列的「AI 助理」使用，或按每一頁右下角的「AI 助理」叫出浮動的對話框（不在專案頁時先選專案），輸入框下面可以換模型與思考深度。

**自己的 AI 帳號**：管理員在「AI 設定」勾「允許成員使用自己的 AI 帳號」（預設不允許：專案的設定會送到成員自己選的供應商）之後，每個人可以在「設定 → AI 帳號」接自己的供應商，只有自己能用、費用算在自己的帳號，模型選單裡標「（我的）」。組織沒開 AI 時，允許了的成員照樣能用自己的。關掉允許之後，已經接的列得出來、可以刪，但選不到。一個人最多 5 個。個人的「OpenAI 相容」端點一律不准連內部網路（`AI_ALLOW_PRIVATE_ENDPOINTS` 只放寬組織的連線）。

- 金鑰到供應商的主控台建立：Anthropic `https://platform.claude.com/settings/keys`、OpenAI `https://platform.openai.com/api-keys`、Google `https://aistudio.google.com/apikey`。費用算在那個帳號。
- 助理只讀專案的設定、模板與統計數字，**看不到項目的內容**；它能做的最多是提出變更集，要有權限的人在「變更集」頁核准才會生效（AI 提出的會標「AI 助理提出」）。
- 對話只有本人看得到，存到自己刪掉為止。

**用不了時**：

1. 專案頁列沒有「AI 助理」：組織沒開、沒有開放任何模型，或自己沒有讀專案設定的權限。
   右下角沒有「AI 助理」的按鈕：組織沒開（或沒有開放任何模型），而且也不允許使用自己的 AI 帳號。
2. 回覆下面顯示失敗的原因：「金鑰不對」「額度用完」「連不上」照原因到「AI 設定」頁處理（可以先按「重新讀取模型」試金鑰）；「模型拒絕了這個要求」換一個模型或換個說法。
3. `bash scripts/ops/errors.sh`：AI 的錯誤與警告在 `PmTool.Ai` 開頭的類別，記錄裡不會有金鑰與對話的內容。
4. 還原到新的機器、或弄丟 DataProtection 的金鑰之後：「存的金鑰解不開，請管理員重新輸入」，到「AI 設定」頁編輯連線重新輸入金鑰。
5. 「用 OpenRouter 登入」回來顯示沒有完成：登入的連結 10 分鐘內有效、只能由開始登入的那個帳號完成；在 OpenRouter 按了拒絕也會這樣。重新按一次登入。登入成功但連線數已滿時，OpenRouter 那邊已經建好的金鑰要到 `https://openrouter.ai/settings/keys` 封存。OpenRouter 回「額度不夠」（402）時到 OpenRouter 加值。

**選項目、建議修改**：在項目頁或開著項目的側邊面板時，AI 助理自動帶那個項目（也可以在助理的頁首搜尋換一個）。助理能讀項目的說明與留言（會送到選的 AI 供應商），要改的話只會提出「修改建議」卡片，使用者按「套用」才以自己的權限寫入，按「不要」就作廢；建議之後有人改了說明，套用會被擋下、什麼都不改，請重新請助理建議。本機代理要讀項目，權杖要有 `items:read`（「建立本機代理用的權杖」會帶；之前建立的要重建）。

**本機代理（成員自己電腦上的 Claude Code／Codex）**：成員在自己的電腦執行 `kumi agent start`（權杖要有 `agent:run`、`blueprint:read`、`blueprint:write`，「設定 → AI 帳號」有按鈕建一把），網頁的模型選單就多出「Claude Code（電腦名稱）」。它只往外連（長輪詢 `/v1/agent/check-in`），伺服器不連進成員的電腦，主機不用開任何埠。用不了時：

1. 選單裡沒有：組織沒有允許使用自己的 AI 帳號，或那台電腦 60 秒內沒有登記（`kumi agent start` 沒開、網路斷了）；「設定 → AI 帳號」看得到每台電腦在線或最後在線的時間。
2. 回覆失敗「你電腦上的 Kumi 代理沒有回應」：送出後 20 秒沒被領走（代理剛好關掉）；領走後 120 秒沒有回報（代理被關、網路斷了）。
3. 本機的錯誤看執行 `kumi agent start` 的那個終端機（每件工作一行，不印對話內容）；常見的是那台電腦的 `claude`／`codex` 沒登入、版本太舊（缺必要的旗標時不提供那個引擎）。
4. 伺服器的記錄在 `PmTool.Ai` 開頭的類別。殘留的工作與進度在回覆存完後刪掉，不需要手動清。

**連本機的模型（Ollama）**：8080 那一套的 api 在容器裡，`localhost` 連不到主機。供應商選「OpenAI 相容」，基底網址填 `http://host.docker.internal:11434/v1`，並在 `.env` 加 `AI_ALLOW_PRIVATE_ENDPOINTS=true` 後用 `deploy-local.sh` 重啟。

## 16. 用自己的 Claude Code／Codex 連（MCP）

使用者在自己的電腦上用 Claude Code 或 Codex（自己的訂閱）連 pm-tool 的 MCP 伺服器，第一次連線時在瀏覽器登入 pm-tool、按「允許」。步驟、範圍與撤銷見 `docs/MCP.md`；網頁「設定 → 命令列與整合」有這個部署的網址與可以複製的指令。

**連不上時**：

1. 用戶端說授權失敗：確認瀏覽器開的是 pm-tool 的授權頁、登入的是對的帳號；授權頁顯示錯誤時照錯誤處理（例如「resource 不對」通常是用戶端的網址打錯，要是 `https://<網域>/mcp`）。
2. 授權過、之後連不上：到「設定 → 命令列與整合」的「已授權的 AI 代理」看還在不在（被撤銷、30 天沒用的要重新授權）；帳號停用也會失效。
3. `bash scripts/ops/errors.sh`：OAuth 的錯誤在 `PmTool.Identity` 開頭的類別、MCP 在 `PmTool.Api.Mcp`。記錄裡不會有權杖。
4. 本機（8080，沒有 HTTPS）也能接：`claude mcp add --transport http kumi http://localhost:8080/mcp`。


## 17. 主機 AI 代理（全組織共用的 Claude Code／Codex）

組織擁有者把 Claude Code／Codex 放在這台主機的 `ai-worker` 容器上，全組織的成員（擁有者、管理員、一般成員；訪客不行）在 AI 助理的模型選單選「主機上的 Claude Code」就能用，不必自己登入供應商、設金鑰、裝 CLI 或執行 `kumi agent start`。AI 讀寫 Kumi 的資料時用的是**發問的人自己的權限**，修改一樣只會提出變更集或修改建議，要人核准、按套用。規格見 `internal/HANDOFF-host-agent.md`。

**計費**：只能用 API 用量計費的登入（Claude Code 的 Anthropic Console、Codex 的 OpenAI API 金鑰），費用算在那個 Console 組織或 OpenAI 帳號。Claude（Pro／Max／Team／Enterprise）與 ChatGPT 的訂閱帳號依兩家的條款不能給全組織共用：在主機上用訂閱帳號登入，伺服器會拒絕、worker 會立刻登出。

只有組織擁有者看得到「AI 設定 → 主機 AI 代理」；管理員與一般成員沒有這個入口，直接打 API 也是 403。

### 第一次設定

1. 在 `.env` 加一行 `COMPOSE_PROFILES=ai-worker`，執行 `bash scripts/ops/deploy-local.sh`（會建 ai-worker 的映像檔並啟動；第一次要下載 Claude Code 與 Codex，幾分鐘）。`docker compose ps` 看得到 `ai-worker` 是 Up；`docker compose logs ai-worker` 會一直說「等待配對」，這是正常的。
2. **配對**：擁有者在網頁「AI 設定 → 主機 AI 代理」按「新增主機代理」、取個名字，畫面出現一次性的配對碼（15 分鐘內有效、只顯示一次）。在主機的終端機執行：

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
- **登入失效或被換掉**：網頁上那一列會變成「需要登入」或「需要擁有者重新授權」，來源停止派工作（成員的選單顯示「主機上的 … 需要組織擁有者重新授權」）。重做第一次設定的第 3（或 4）步即可。有人在 Kumi 以外直接 `docker compose exec ai-worker claude auth login` 換了登入，worker 會發現帳號變了、不接工作，也要從網頁重新開始登入。
- **登出**：引擎那一列的「登出」：worker 會執行 CLI 自己的登出，排隊中的取消、執行中的停下。
- **停用**：主機的「更多 → 停用」（不派工作、排隊的取消、執行中的停下，機器憑證保留，再「啟用」就好）；只停一個來源用來源的「開放給成員使用」。
- **重新配對**（換了主機、懷疑機器憑證外流）：「更多 → 重新配對」，舊的憑證立刻失效，照第 2 步配對。
- **移除**：「更多 → 移除」，撤銷憑證與工作授權、刪掉它的來源；容器還開著的話會一直說「機器憑證失效」，用 `docker compose stop ai-worker` 停掉或重新新增、配對。
- **結果不明**：worker 在工作中途斷線（被強制結束、網路斷了）60 秒，那一則失敗並寫「結果不明」：它可能已經提出了變更集或修改建議，請到變更集與項目確認；**不會自動重跑**。

### 資料放在哪裡、怎麼備份

- 資料庫（`ai` schema 的 `host_*` 表：代理、來源、登入紀錄、用量）在平常的備份裡。對話與工作的內容在回覆存完後刪掉，用量只留則數與 token。
- `ai-worker-home` 資料卷（compose 專案的 `pm-tool_ai-worker-home`）放 Claude Code／Codex 的登入（`~/.claude`、`~/.codex`）與 kumi 的機器憑證（`~/.kumi-host/host-agent.json`，600）。**刻意不備份、不要複製**：裡面是供應商的登入憑證，複製一份等於多一個能用帳號的地方。弄丟了（`docker compose down -v` 會刪掉它，所以不要用）就重新配對、重新登入，資料庫裡的設定與用量都還在。
- 還原到另一台主機：資料庫照第 6 節還原，ai-worker 的資料卷是空的，照第一次設定的第 2～4 步重新配對與登入（舊的那台會變成離線，用「移除」清掉）。

### 用不了時

| 網頁上看到 | 怎麼辦 |
|---|---|
| 一直「等待配對」或「離線」 | `docker compose ps` 看 ai-worker 有沒有起來；`docker compose logs ai-worker` 看最後幾行（等待配對、機器憑證失效、連不上伺服器、伺服器要求 https）。 |
| 需要登入、需要擁有者重新授權 | 重新「開始登入」、在主機執行 `kumi host login`。 |
| 訂閱帳號不能共用、判斷不出計費方式 | 改用 Console／API 金鑰登入。 |
| 版本不支援、沒有安裝 | 映像檔裡的 CLI 壞了或被換掉：`bash scripts/ops/deploy-local.sh` 重建。 |
| 成員送出時「排隊滿了」「次數已經用完」 | 等前一則結束；上限在來源的設定裡調。 |
