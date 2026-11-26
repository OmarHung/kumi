# 上雲端的部署手冊

這份寫給要把 pm-tool 放到雲端、或放到自己的伺服器上給別人用的人。本機自用的那一套（`compose.yaml`、`scripts/ops/`）看 `docs/OPERATIONS.md`，不是這一份。

- 指令可以直接複製。`<…>` 換成自己的值，例如 `<專案>` 是 GCP 的專案 ID、`<區域>` 是 `asia-east1` 這種區域。
- GCP 的部分只寫了做法，還沒有在真的專案上跑過（2026-11-17）。第一次照做時，對不上的地方請改回這份文件。

## 先懂這幾件事

- **架構**：一個無狀態的容器（API 主機，網頁由它一起提供）加一個 PostgreSQL 18。容器裡不存任何東西：資料、工作階段、工作階段 Cookie 的加密金鑰都在資料庫裡，項目的附件檔案在 Cloud Storage 的 bucket 裡（第 1.6 節），容器可以隨時換掉。
- **容器的加固**：api（與 migrate）的根目錄唯讀、只有 `/tmp` 是記憶體裡的暫存、`cap_drop: ALL`、`no-new-privileges`，以非 root 執行。它只寫附件資料夾（`local` 時）與 `/tmp`，所以這些限制不影響功能；萬一程式有能執行程式碼的漏洞，進到容器也改不了程式、拿不到特權。`compose.yaml` 與 `docs/deployment/vm.md` 的範例都這樣設，自己寫的部署照樣做。
- **兩種放法**，用同一個映像檔：
  - A. GCP 的 Cloud Run（跑容器）加 Cloud SQL（資料庫）。
  - B. 一台 VM 跑 docker compose，前面放反向代理（Caddy 或 nginx）做 HTTPS，資料庫用代管的。
- **設定全部是環境變數**，設定名稱裡的 `:` 寫成 `__`：`ConnectionStrings:Database` 就是 `ConnectionStrings__Database`。
- **資料庫有兩個角色**：`pmtool` 是資料表的擁有者，只在遷移時用；`pmtool_app` 是平常執行用的，只能讀寫資料。服務被攻破時，改不了資料表結構。
- **遷移不在服務啟動時跑**。每次更新先跑一次 `migrate`，成功了才換新版的服務。
- **一開始只開一個容器**（第 4.3 節）。

## 1. 要準備的東西

### 1.0 先在新專案啟用要用的 API

新的 GCP 專案什麼 API 都沒開，後面的指令會說「API has not been used」。專案要先綁帳單，再一次開好：

```
gcloud services enable --project=<專案> \
  run.googleapis.com sqladmin.googleapis.com secretmanager.googleapis.com \
  artifactregistry.googleapis.com iam.googleapis.com cloudresourcemanager.googleapis.com \
  logging.googleapis.com monitoring.googleapis.com
```

- `iam` 是建服務帳戶用，`cloudresourcemanager` 是 `add-iam-policy-binding` 用，`logging` 與 `monitoring` 是第 4.5 節與檢查清單的監控用。
- 放在 VM 上（第 3.3 節）的話，再加 `compute.googleapis.com`。

### 1.1 PostgreSQL 18

- 代管的就好：Cloud SQL for PostgreSQL 18，或其他雲的代管 PostgreSQL 18。
- 一定要開：自動備份、時間點還原、刪除保護（第 6 節）。
- 不要對外開放：Cloud Run 經由 Cloud SQL 的連接器連線，不用加「授權網路」（authorized networks）。

Cloud SQL 的建立方式（機器大小依預算選：共用核心的 `db-g1-small` 最便宜但沒有 SLA；要專用核心，把 `--tier` 換成 `--cpu=1 --memory=3840MiB`）：

```
gcloud sql instances create pmtool-db --project=<專案> --region=<區域> \
  --database-version=POSTGRES_18 --edition=enterprise --tier=db-g1-small \
  --ssl-mode=ENCRYPTED_ONLY \
  --backup-start-time=18:00 --enable-point-in-time-recovery --retained-transaction-log-days=7 \
  --deletion-protection --retain-backups-on-delete
gcloud sql users set-password postgres --instance=pmtool-db --project=<專案> --prompt-for-password
```

- `--backup-start-time` 是 UTC：`18:00` 是台灣時間凌晨 2 點。
- 管理帳號 `postgres` 的密碼用 `--prompt-for-password` 輸入，不要寫在指令裡（會留在 shell 的歷史紀錄）。
- 連線名稱是 `<專案>:<區域>:pmtool-db`，下面寫成 `<連線名稱>`。

### 1.2 兩個角色

先產生兩組密碼（只用英文字母與數字：密碼會被放進連線字串），存進密碼管理器：

```
openssl rand -hex 24     # pmtool 的
openssl rand -hex 24     # pmtool_app 的
```

用管理帳號連上資料庫，執行下面的 SQL。Cloud SQL 用：

```
gcloud sql connect pmtool-db --user=postgres --database=postgres --project=<專案>
```

- 本機要有 `psql`。它會暫時把你的 IP 加進授權網路幾分鐘，用完記得確認已經移除（`gcloud sql instances describe pmtool-db --project=<專案> --format='value(settings.ipConfiguration.authorizedNetworks)'` 應該是空的）。
- **不要用主控台的 Cloud SQL Studio 跑下面的 SQL。** Studio 不是 `psql`，沒有 `\password`。硬要用的話，要把 `\password 角色` 換成 `ALTER ROLE 角色 PASSWORD '明文密碼';`，而這個密碼會留在 Studio 的查詢歷史，開了稽核記錄的話也會留在稽核記錄裡。這樣做之後要清掉 Studio 的歷史，並且把這兩個密碼換掉。

```sql
-- 資料表的擁有者：只有遷移（migrate）用它。
CREATE ROLE pmtool LOGIN;
\password pmtool
-- 執行用的角色：服務平常用它。不是超級使用者，不能建資料庫、不能建角色。
CREATE ROLE pmtool_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
\password pmtool_app
-- 代管服務的管理帳號通常不是真正的超級使用者，要先能切換成 pmtool，才能把資料庫交給它。
GRANT pmtool TO CURRENT_USER;
CREATE DATABASE pmtool OWNER pmtool;
-- PUBLIC 預設連得上每一個資料庫：拿掉，只讓 pmtool_app 連（擁有者本來就連得上）。
REVOKE ALL ON DATABASE pmtool FROM PUBLIC;
GRANT CONNECT ON DATABASE pmtool TO pmtool_app;
```

- `\password` 會問兩次密碼，貼上剛才產生的。它在 `psql` 本機先把密碼雜湊過才送出，密碼不會出現在資料庫的記錄裡。
- **不要用 `gcloud sql users create` 或主控台建 `pmtool_app`**：那樣建的帳號會加入 `cloudsqlsuperuser`，有建資料庫與建角色的權限。
- 讀寫資料表的權限不用自己給：`migrate` 遷移完會授予 `Database__RuntimeRole`（`pmtool_app`），之後新建的資料表也會自動授予。
- 確認：`\du pmtool*` 裡 `pmtool_app` 的 Attributes 是空的（只能登入）；下面這一句查不到任何列（它不屬於任何角色）：

  ```sql
  SELECT r.rolname FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.roleid WHERE m.member = 'pmtool_app'::regrole;
  ```

### 1.3 放映像檔的地方

GCP 用 Artifact Registry：

```
gcloud artifacts repositories create pmtool --project=<專案> --location=<區域> --repository-format=docker
gcloud auth configure-docker <區域>-docker.pkg.dev
```

映像檔的名稱是 `<區域>-docker.pkg.dev/<專案>/pmtool/api`。

### 1.4 DataProtection 的憑證

工作階段 Cookie 用一組金鑰加密，那組金鑰存在資料庫裡（`identity.data_protection_keys`）。憑證用來把金鑰加密之後才存進去：資料庫的備份外流時，Cookie 的金鑰不會跟著外流。

- 沒有憑證也能跑，只是金鑰是明文，正式環境啟動時會記一筆 Warning（「沒有設定 DataProtection:CertificatePath…」）。金鑰除了簽 Cookie，也用來加密 AI 供應商的金鑰與寄信的密碼：沒有憑證時，拿到資料庫或備份的人解得開這些。上線一定要設，並把 `DataProtection__RequireCertificate` 設成 `true`：設了它卻沒有 `DataProtection__CertificatePath`，啟動直接失敗，不會悄悄用明文上線（本機的 8080 不設它，照舊可以不用憑證）。
- 弄丟憑證不會掉資料：金鑰解不開，舊的 Cookie 全部失效，所有人重新登入一次；用那把金鑰加密的 AI 供應商金鑰與寄信密碼也解不開，要重新輸入。
- 本機的 `compose.yaml`（`docs/OPERATIONS.md` 第 20 節）從 `.env` 的 `DATAPROTECTION_DIR`、`DATAPROTECTION_CERT_FILE`、`DATAPROTECTION_CERT_PASSWORD` 把憑證唯讀掛進 `api` 與 `migrate`，設上 `DataProtection__*`；步驟、設了之後的後果與換憑證在那一節。

**正式環境要在第一次上線之前就設好憑證。** 事後才補設的話，之前以明文存進資料庫的金鑰不會被重新加密，備份裡還是明文。補設之後要刪掉舊金鑰、讓所有人重新登入（做法見下面「憑證外流」那一段的 SQL）。

在一台信得過的電腦上產生（RSA：DataProtection 用憑證的 RSA 金鑰加密，不能用 EC）。`umask 077` 放在括號裡：只影響這一段，不會留在你的 shell：

```
mkdir -m 700 pmtool-dp
(
  umask 077
  openssl rand -hex 24 | tr -d '\n' > pmtool-dp/dataprotection.password
  openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
    -subj "/CN=pm-tool DataProtection" \
    -keyout pmtool-dp/dataprotection.key -out pmtool-dp/dataprotection.crt
  openssl pkcs12 -export -inkey pmtool-dp/dataprotection.key -in pmtool-dp/dataprotection.crt \
    -passout file:pmtool-dp/dataprotection.password -out pmtool-dp/dataprotection.pfx
  rm pmtool-dp/dataprotection.key pmtool-dp/dataprotection.crt
)
```

- 路徑都是從目前的目錄寫起（`pmtool-dp/…`），不用 `cd`，後面第 3.2 節的指令也是從同一個目錄執行。
- 密碼檔的結尾不能有換行（上面用 `tr -d '\n'` 拿掉了）：整個檔案會變成 `DataProtection__CertificatePassword` 的值，多一個換行就是錯的密碼。
- 留下 `dataprotection.pfx` 與 `dataprotection.password`，放進祕密管理（第 3.2 節），另外在密碼管理器裡存一份，然後刪掉這個資料夾。

**換憑證**分兩種情況。

*A. 只是定期更換，沒有外流。*

1. 用**同一個密碼**產生新的 PFX（上面的指令，跳過產生密碼那一行，輸出檔名換成 `dataprotection-new.pfx`）。
2. 新的當成目前的（`DataProtection__CertificatePath`），舊的放進 `DataProtection__PreviousCertificatePaths__0`。新產生的金鑰用新憑證加密，舊的金鑰還解得開。
   - Cloud Run：舊的 PFX 另建一個祕密，掛到**另一個目錄**（同一個目錄不能掛兩個祕密）：
     ```
     gcloud secrets create pmtool-dp-cert-prev --project=<專案> --data-file=pmtool-dp/dataprotection.pfx
     gcloud secrets versions add pmtool-dp-cert --project=<專案> --data-file=pmtool-dp/dataprotection-new.pfx
     ```
     然後 `--set-secrets` 加上 `/secrets/dp-prev/dataprotection.pfx=pmtool-dp-cert-prev:1`、把 `pmtool-dp-cert` 的版本號改成新的，環境變數檔加上 `DataProtection__PreviousCertificatePaths__0: "/secrets/dp-prev/dataprotection.pfx"`。服務與遷移的 Job 都要改，兩個服務帳戶都要給 `pmtool-dp-cert-prev` 讀取權限（第 3.2 節第 3 步）。
   - VM：舊的放 `secrets/dataprotection-prev.pfx`，compose 掛到 `/run/secrets/dataprotection-prev.pfx`，加上 `DataProtection__PreviousCertificatePaths__0: /run/secrets/dataprotection-prev.pfx`。
3. 舊憑證的保留期是**金鑰的有效期 90 天加上工作階段的絕對期限 7 天，也就是 97 天**：最後一把用舊憑證加密的金鑰，最晚到第 90 天還在簽發 Cookie，那些 Cookie 最長再活 7 天。過了這段時間才能把舊憑證拿掉。不確定就一直留著：它只用來解開舊金鑰。

*B. 憑證外流（PFX 或它的密碼被別人拿到，或資料庫備份與 PFX 同時外流）。*

光換憑證不夠：資料庫裡的金鑰仍是用舊憑證加密的，拿到舊憑證的人解得開它們，就能偽造工作階段的 Cookie。要連金鑰一起丟掉，並讓所有工作階段失效：

1. 產生**新的密碼與新的 PFX**（整段指令照跑一次），換進祕密（加新版本、改版本號）。**不要**把外流的舊憑證放進 `PreviousCertificatePaths`。
2. 部署新的設定（Cloud Run 是新的修訂版，VM 是 `docker compose up -d`）。
3. 用擁有者（`pmtool`）連上資料庫，執行：

   ```sql
   BEGIN;
   DELETE FROM identity.data_protection_keys;
   UPDATE identity.sessions SET revoked_at = now(), revoked_reason = 'key-rotated' WHERE revoked_at IS NULL;
   COMMIT;
   ```

4. 讓服務重新啟動，丟掉記憶體裡舊的金鑰：VM 是 `docker compose restart api`；Cloud Run 是再部署一次（例如 `gcloud run services update pmtool --project=<專案> --region=<區域> --update-env-vars=DP_ROTATED=<日期>`，加一個沒人讀的環境變數就會換一個新的修訂版）。
5. 後果：**所有人立刻被登出，要重新登入**。資料不受影響。服務啟動後會產生新的金鑰，用新憑證加密。
6. 同時處理外流的原因本身，並照第 6 節的做法檢查舊備份放在哪裡：舊的備份裡還有舊金鑰。

### 1.5 HTTPS 的網址

- **Cloud Run**：部署之後就有 `https://…run.app` 的網址，自帶 HTTPS。要用自己的網域，用 Cloud Run 的網域對應（domain mapping）。**網域對應不是每個區域都支援**（有些區域不能用，狀態也可能還是預覽版），先查你選的區域在 Cloud Run 的「domain mappings」文件裡有沒有列出。指令是 `gcloud beta run domain-mappings create --service=pmtool --domain=<網域> --region=<區域>`（要裝 `beta` 元件），區域不支援就會被拒絕，那就改走外部負載平衡器。前面加 Google 的外部負載平衡器的話，先看第 3.2 節的注意事項。
- **VM**：網域的 A 紀錄指到 VM 的對外 IP，防火牆開 TCP 80 與 443（反向代理只用 HTTP/1.1，不開 HTTP/2 與 HTTP/3，所以不用 UDP，見 `docs/deployment/vm.md` 第 5 節）。Caddy 會自己申請與更新憑證。

### 1.6 附件的儲存空間（Cloud Storage）

項目上傳的檔案不放在資料庫與容器裡：資料庫只記檔案的資訊（檔名、大小、SHA-256），檔案本身放在一個 bucket。雲端一律用 `Attachments__Provider=gcs`；`local` 只給本機（容器的檔案系統是暫時的，Cloud Run 上寫的檔案會跟著容器不見）。

1. **建立 bucket**：和 Cloud SQL **同一個區域**（延遲低、不收跨區流量費）、**統一的存取控管**（uniform bucket-level access，不用物件的 ACL）、**不公開**（強制禁止公開存取）：

   ```
   gcloud storage buckets create gs://<bucket> --project=<專案> --location=<區域> \
     --uniform-bucket-level-access --public-access-prevention
   ```

   - bucket 名稱全球唯一，不要放進組織或客戶的名字。
   - 下載一律經過 api（由它檢查登入與權限、串流回傳），不發簽名網址，也不開放直接讀取。
2. **服務帳號只給這個 bucket**：服務（`pmtool-api`）要寫入、讀取、刪除物件，授予 `roles/storage.objectAdmin`，**綁在這個 bucket 上**，不要綁在專案層級（否則它碰得到專案裡所有的 bucket）：

   ```
   gcloud storage buckets add-iam-policy-binding gs://<bucket> --project=<專案> \
     --role=roles/storage.objectAdmin \
     --member=serviceAccount:pmtool-api@<專案>.iam.gserviceaccount.com
   ```

   - `objectAdmin` 包含建立、讀取、刪除物件，不含改 bucket 本身的設定。只要寫入與讀取、不要刪除的話，可以改成 `roles/storage.objectUser` 加上自訂角色，但 api 在刪除附件時會呼叫刪除，沒有這個權限會一直記 Warning（檔案不會被刪）。遷移的 Job 不碰附件，不用給。
   - 認證用 Application Default Credentials：Cloud Run 的服務帳號自動帶上，不用設金鑰檔，**不要建服務帳號金鑰**。
3. **保護已寫入的檔案**（至少選一個，建議第一個）：

   ```
   # 物件版本控管：覆寫或刪除之後，舊的版本還在，可以救回來
   gcloud storage buckets update gs://<bucket> --project=<專案> --versioning
   # 刪除之後的非目前版本留 30 天再清掉，免得空間一直長
   # （lifecycle.json：{"rule":[{"action":{"type":"Delete"},"condition":{"daysSinceNoncurrentTime":30,"isLive":false}}]}）
   gcloud storage buckets update gs://<bucket> --project=<專案> --lifecycle-file=lifecycle.json
   ```

   - 或者用軟刪除（soft delete，新的 bucket 預設就開著、保留 7 天）：`gcloud storage buckets update gs://<bucket> --soft-delete-duration=30d`。軟刪除只救「被刪掉」，物件版本控管連「被覆寫」也救；附件的檔案建好之後不會改（鍵是附件的編號），兩者的差別不大，但版本控管可以和資料庫的時間點還原對上（見第 6 節）。
4. **設定**：服務的環境變數檔（`cloudrun-service.env.yaml`，VM 是 `service.env`）加：

   ```yaml
   Attachments__Provider: "gcs"
   Attachments__Bucket: "<bucket>"
   ```

   - 遷移的 Job 不需要這兩個。
   - bucket 名稱寫錯或服務帳號沒有權限時，服務**啟動失敗並說明**，不會等到第一次上傳才發現。
5. **上傳的大小**：
   - Cloud Run 對 HTTP/1 的請求有 **32 MiB** 的上限，超過的請求在到達 api 之前就被平台擋掉（回 413，不是 api 的 `attachment.too_large`）。目前單檔上限是 25 MiB，加上 multipart 的其他內容（api 的請求上限是檔案上限再加 1 MiB），在 32 MiB 以內，沒問題。
   - **之後要調高單檔上限，不能超過 32 MiB 再扣掉 multipart 的負擔**（留 2 MiB 左右）；要更大，得換成瀏覽器直接上傳到 Cloud Storage（簽名網址的可續傳上傳），那是另一個設計，目前沒有做。
   - VM 前面的反向代理也有請求大小的限制：nginx 的 `client_max_body_size`（預設 1 MB）要設成 26m 以上（`docs/deployment/vm.md` 的範例是 30m），Caddy 預設沒有上限。
   - Cloud Run 的請求逾時預設 300 秒，25 MiB 的檔案在一般網路上夠用。
6. **容量的上限**：項目的附件與知識庫的附件合在一起算，每個組織的總量預設 20 GiB（`Attachments__Quota__OrganizationGiB`），每個人過去 24 小時上傳的量預設 2 GiB（`Attachments__Quota__PersonDailyGiB`）。到了上限，新的上傳回 409（項目是 `attachment.quota_exceeded`、`attachment.daily_quota_exceeded`，知識庫是 `knowledge.attachment_quota_exceeded`、`knowledge.attachment_daily_quota_exceeded`），訊息寫得出是哪一個上限、用了多少；已經上傳的不受影響。
   - 兩個值都是 0 以上的整數（單位 GiB），`0` 是不限；寫成別的東西啟動失敗。
   - 組織管理員在網頁的「設定 → 儲存空間」看得到用量與上限。
   - **既有的部署升級之前先看用量**：已經超過 20 GiB 的組織，升級之後新的上傳會被擋，先把上限調高。
   - 上限管的是資料庫裡記錄的大小；bucket 或磁碟實際還有多少空間要另外看（本機是 `status.sh` 的「附件」那一列）。

### 1.7 寄信（Email 通知）

寄信的設定在網頁上（組織管理員的「寄信設定」頁），不是部署的環境變數；換寄信帳號不必重新部署。部署時只要注意：

- **對外的 25 埠通常被擋**：GCP 一律擋對外的 25，其他雲端多半也是。用 587（STARTTLS）或 465（SSL／TLS）。Gmail 固定走 587。
- **主機要連得到寄信服務**：防火牆只擋進來的話不用動；有限制對外連線的，要放行寄信主機的 587 或 465。
- **SMTP 密碼和 Cookie 的金鑰是同一套 Data Protection**：沒有設 DataProtection 的憑證（第 1.4 節）時，金鑰是明文存在資料庫裡，備份外流就等於 SMTP 密碼外流。上線一定要設憑證；弄丟憑證的話，除了所有人重新登入，寄信設定也要重新輸入密碼（設定頁會顯示「存的密碼解不開，請重新輸入」）。
- **寄信的主機預設不能在內網**：設定頁填的主機是私有網段、本機或 link-local 的 IP 時不給存；填主機名稱的，在每次連線的當下檢查它解析出來的位址，是內網的就不連、設定頁顯示原因（伺服器會照管理員填的主機去連，不擋的話等於讓人拿它探內網）。公司內網的轉信主機才設 `Notifications__Email__AllowPrivateHosts=true`。**既有的部署升級之前先看現在的寄信主機**：是內網的位址而沒有打開這個設定，升級之後信寄不出去（設定頁會顯示原因）。
- 寄送失敗時，設定頁顯示的原因是整理過的分類（連不上、帳號密碼不對、憑證有問題、對方拒收），不帶對方主機回的原文；原文在 api 的記錄裡。
- 同一封信在極少數的情況下會寄兩次：信已經交給寄信主機、但回應在逾時之後才到，下一輪會再寄。寧可多寄也不漏寄。
- 只開一個容器的限制和寄信無關：同時只有一個程序在寄（資料庫的 advisory lock），之後開多個容器也不會重複寄。

### 1.8 AI（供應商與 AI 助理）

接哪家 AI、開放哪些模型，是組織管理員在網頁的「AI 助理設定」頁設定的，不是部署的環境變數。部署時只要注意：

- **主機要連得到供應商**：Anthropic（`api.anthropic.com`）、OpenAI（`api.openai.com`）、Google（`generativelanguage.googleapis.com`）、OpenRouter（`openrouter.ai`），都是對外的 443。有限制對外連線的要放行。
- **「用 OpenRouter 登入」不用事先註冊應用**：瀏覽器導到 OpenRouter 登入、回到 `https://<PublicHost>/ai/openrouter/callback`，伺服器再用授權碼向 `openrouter.ai` 換一把金鑰。回呼網址由 `Security__PublicHost` 組成（同第 1.9 節），沒設的話用請求的 Host。登入的狀態用 Data Protection 加密帶在網址上，所以多個容器之間不用另外共用。
- **成員自己的 AI 帳號**：組織管理員在「AI 助理設定」勾「允許成員使用自己的 AI 帳號」之後，每個人可以在「設定 → 我的 AI 助理」接自己的供應商（費用算在自己的帳號）。個人的「OpenAI 相容」端點**一律不准連私有網段**，`Ai__AllowPrivateEndpoints` 只放寬組織的連線（不然任何成員都能讓伺服器連內網）。
- **OpenAI 相容端點（自架的 Ollama、vLLM）在內網或本機時**：預設不准連私有網段、本機與 link-local 的位址（伺服器會照管理員填的網址去連，不擋的話等於讓人拿它掃內網，雲端主機的中繼資料服務也在 link-local）。真的要連內網的模型服務才設 `Ai__AllowPrivateEndpoints=true`。容器裡的 `localhost` 是容器自己，主機上的服務用主機的內網 IP（Docker Desktop 是 `host.docker.internal`）。
- **助理的回覆是串流（`text/event-stream`）**：api 每 15 秒送一行心跳、回應帶 `X-Accel-Buffering: no`，nginx 不用改設定（預設 60 秒的 `proxy_read_timeout` 夠用）；前面還有別的代理或 CDN 的話，要確認它不會把整個回應緩衝到結束才送。Cloud Run 一則回覆最多 5 分鐘，在預設的請求逾時之內。
- **金鑰和 Cookie 的金鑰是同一套 Data Protection**（同第 1.7 節）：上線一定要設憑證；弄丟憑證的話，AI 助理設定的金鑰要重新輸入（助理會顯示「存的金鑰解不開，請管理員重新輸入」）。
- 費用算在組織自己的供應商帳號；設定頁顯示每個連線這個月的 token 用量，金額要到供應商的主控台看。

### 1.9 MCP 與 OAuth（用自己的 Claude Code／Codex 連）

使用者電腦上的 Claude Code、Codex 連到 `https://<網域>/mcp`，用 pm-tool 的帳號登入授權（說明在 `docs/MCP.md`）。部署時要注意：

- **一定要設 `Security__PublicHost`**：OAuth 的 issuer 與 MCP 的網址都由它組成（`https://<PublicHost>`、`https://<PublicHost>/mcp`）。沒設時用請求的 Host 標頭，只適合本機；上線沒設的話，用戶端看到的網址可能和中繼資料對不上，授權會失敗。
- **反向代理不用改設定**：`/.well-known/oauth-authorization-server`、`/.well-known/oauth-protected-resource`、`/oauth/*`、`/mcp` 都是一般的 HTTP，照現在的轉送就好。
- **主機要能連出去抓用戶端的資料**：Claude Code 這類用戶端用「Client ID Metadata Document」（一個 https 網址）表明身分，授權時伺服器會去抓（對外的 443，只連公開的位址）。對外連線有限制的話，要放行用戶端的網域（例如 `claude.ai`）。
- **限流在反向代理後面要讀得到用戶端的 IP**（`ForwardedHeaders__*`）：token、動態註冊、撤銷依來源 IP 限流，沒設的話所有人共用一份額度。命令列的瀏覽器登入也會比對核准與兌換兩邊的用戶端位址，轉送標頭沒設好時所有人的位址都一樣，這個比對就沒有作用。
- claude.ai 網頁版的自訂連接器要從 Anthropic 的雲端連進來，防火牆只放行公司 IP 的部署用不了。

### 1.10 即時更新（WebSocket）

網頁開著時用一條 WebSocket（`/v1/live`，SignalR）收「哪些項目變了」，再自己重抓。

- **反向代理要轉 WebSocket**：nginx 要加升級的標頭（`docs/deployment/vm.md` 的範例）；Caddy、Cloud Run 不用另外設定。少了的話還能用（退回長輪詢），但每個開著的分頁一直佔住一條連線；VM 的反向代理只用 HTTP/1.1，瀏覽器對同一個網站最多 6 條，分頁一多整個網站就卡住。
- **Cloud Run**：一條連線最長是服務的請求逾時（預設 5 分鐘，最多 60 分鐘），到了會被切斷，網頁自己重連、把畫面上的資料重抓一次。建議把請求逾時調到 60 分鐘（`--timeout=3600`）。開著的連線會讓執行個體一直算在使用中。
- **多個容器**：每個 api 程序自己讀事件流、推給自己的連線，項目的變動不需要 Redis。程序之間另外要互相通知三件事——憑證作廢（登出、改密碼、停用帳號，別的程序的工作階段快取要立刻清掉）、權限變動（即時連線的群組要重算）、收件匣有新東西——走 PostgreSQL 的 `LISTEN/NOTIFY`（`Cluster__Signals__Enabled`，預設開著），不用另外架東西。
  - 每個 api 程序多一條一直開著的資料庫連線（用執行的角色，不需要額外的權限）。算連線數的上限時要算進去。
  - **連線池的代理要能轉 `LISTEN`**：PgBouncer 的 transaction 模式不行（收不到通知），要用 session 模式，或讓 api 直接連資料庫。Cloud SQL Auth Proxy 沒有這個問題。
  - 那條連線斷了會自己重連；重連之後這個程序把工作階段的快取整個清掉、即時連線全部重算一次（中間漏掉的訊號補不回來）。
  - 關掉（`false`）時退回舊的行為：別的程序作廢的工作階段最多再認 30 秒，權限變動最多 2 分鐘才反映到即時連線，收件匣的即時更新只從跑通知背景工作的那一個程序送出。只開一個容器時關不關都一樣。
- 每個開著連線的 api 程序每 500 毫秒查一次事件流。

### 1.11 命令列工具的下載

映像檔裡內含 kumi 的 macOS（Apple 晶片、Intel）與 Windows 執行檔（`/app/cli/`，Dockerfile 的 `cli` 階段建的），登入的人在「我的帳號 → 命令列工具」下載，`kumi update` 也從這裡拿新版（`internal/HANDOFF-cli-downloads.md`）。

- 不用另外設定。執行檔的版本跟著映像檔：部署新的映像檔，`kumi update` 就拿得到新版。
- 映像檔因此大了約 110 MB。不想提供下載時，把 `Cli__PackagesPath` 指到一個空的資料夾，網頁會說這個伺服器沒有提供下載。
- 反向代理不用改設定；檔案約 37 MB，nginx 的 `proxy_read_timeout` 用預設值就夠。

### 1.12 主機 AI 代理（全組織共用的 Claude Code／Codex）

組織擁有者在「設定 → AI 助理主機代理」設定，全組織的成員在 AI 助理選「主機上的 Claude Code／Codex」（`internal/HANDOFF-host-agent.md`，操作步驟在 `docs/OPERATIONS.md` 第 17 節）。部署時要注意：

- **只適合 VM 加 docker compose（第 3.3 節）**：worker（`deploy/ai-worker/Dockerfile`）要常駐、要一個持久的資料卷放 CLI 的登入與機器憑證。Cloud Run 會縮到零、沒有持久的檔案，不適合；要用的話另外開一台 VM 只跑 ai-worker，連到 Cloud Run 的對外網址。
- **計費一定是 API 用量**：Claude Code 用 Anthropic Console 登入、Codex 用 OpenAI API 金鑰。Claude 與 ChatGPT 的訂閱帳號依兩家的條款不能給全組織共用，伺服器會拒絕、worker 會登出（交接第 2 節）。費用算在登入的 Console 組織或 OpenAI 帳號；設定頁只顯示 Kumi 記錄的則數與 token，不是帳單。
- **worker 連 api 的網址**（`AI_WORKER_SERVER`，預設 compose 內部的 `http://api:8080`）也是工作的 MCP 網址。設了 `Security:RequireHttps=true` 的部署，內部的 http 會被轉到 https，worker 會一直說「伺服器要求 https」：把 `AI_WORKER_SERVER` 設成對外的 `https://<PublicHost>`，並確認容器裡解析得到、連得到（主機上的反向代理；連不回自己的公開 IP 時用 compose 的 `extra_hosts: ["<PublicHost>:host-gateway"]`）。換了網址要重新配對。
- **對外連線**：worker 要連 `api.anthropic.com`、`console.anthropic.com`、`claude.ai`（登入時）、`api.openai.com`、`auth.openai.com`（登入時），以及 npm 的 registry（只有建置映像檔時）。
- **只開一個 worker**：同一份登入不要複製到第二台（更新權杖時會互相作廢，也等於帳號池）。一個組織最多 3 台代理，給換主機時過渡用。
- **網路隔離與資源**：compose 的 ai-worker 只在 `ai-worker` 網路上（連得到 api、連不到 db），非 root、唯讀的根目錄、`cap_drop: ALL`、`no-new-privileges`、`mem_limit: 2g`、`pids_limit: 512`。VM 上自己寫的 compose 照樣做。
- **映像檔裡的 CLI 版本固定**（`@anthropic-ai/claude-code@2.1.330`、`@openai/codex@0.163.0`）。本機代理的隔離旗標是在這兩個版本上驗過的，升級前照 `internal/HANDOFF-local-agent.md` 第 5 節重驗。

## 2. 建置與推送映像檔

在 repo 的根目錄，從乾淨的工作目錄建置（`git status` 沒有東西）：建置內容會包括還沒提交的改動。

```
TAG="$(git rev-parse --short=12 HEAD)"
IMAGE="<區域>-docker.pkg.dev/<專案>/pmtool/api:$TAG"
docker buildx build --platform linux/amd64 -t "$IMAGE" --push .
```

- 一定要加 `--platform linux/amd64`：Cloud Run 與大多數的 VM 是 x86，Apple Silicon 的 Mac 不加的話會建成 arm64，部署上去跑不起來。
- 在 Apple Silicon 上建 x86 的映像檔要靠模擬，很慢，.NET 的建置在模擬裡也可能失敗。先在 Docker Desktop 打開 Settings → General →「Use Rosetta for x86_64/amd64 emulation on Apple Silicon」，或改在 x86 的機器上建。
- 標籤用提交的 SHA，不要用 `latest`：看得出正在跑哪一版，退回上一版就是換回上一個標籤。
- 基底映像檔已經固定到 digest，同一個提交建出來的內容一樣。

## 3. 設定一覽

### 3.1 全部的設定

| 設定（環境變數） | 預設 | 說明 |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | — | 一律 `Production`。記錄是一行一筆的 JSON。 |
| `ConnectionStrings__Database` | （必填） | 執行用的連線（`pmtool_app`）。 |
| `ConnectionStrings__Migrations` | 同上 | 遷移用的連線（`pmtool`，資料表的擁有者）。只給 `migrate`。 |
| `Database__RuntimeRole` | 無 | `pmtool_app`。遷移完把每個模組的 schema 的讀寫權限授予它。名稱只收 `^[a-z_][a-z0-9_]{0,62}$`。設了它就要另外設 `ConnectionStrings__Migrations`（用資料表擁有者的連線）：遷移與執行用同一個角色時，遷移會失敗並說明要分開。 |
| `Database__MigrateOnStartup` | `true` | 雲端設 `false`。這時有還沒套用的遷移，`/health/ready` 回 503 並記一筆 Error。 |
| `ForwardedHeaders__Enabled` | `false` | 讀反向代理帶的 `X-Forwarded-For`、`X-Forwarded-Proto`。只看最後一層。 |
| `ForwardedHeaders__ForwardLimit` | `1` | 要往回看幾層 `X-Forwarded-For`，1～10 的整數。前面有幾層代理就設幾（例如 CDN 加 nginx 是 `2`）；設太小，api 會把中間那層代理當成用戶端，設太大則會信到用戶端自己填的值。 |
| `ForwardedHeaders__KnownProxies__0`（`__1`…） | 無 | 信任的代理 IP。 |
| `ForwardedHeaders__KnownNetworks__0`（`__1`…） | 無 | 信任的網段（CIDR，例如 `10.0.0.0/24`）。 |
| `ForwardedHeaders__TrustAllProxies` | `false` | 信任任何來源。只能用在外面只連得到平台前端的環境（Cloud Run）；VM 開了對外的埠就不行。同時設了 `KnownProxies`／`KnownNetworks` 的話，啟動時會記一筆 Warning（那兩個設定不會有作用）。 |
| `Security__RequireHttps` | `false` | 打開時 Cookie 一律 Secure，名稱是 `__Host-pmtool_session`（帶 `Secure`、`Path=/`、沒有 `Domain`；關著時叫 `pmtool_session`）；每個回應帶 `Strict-Transport-Security: max-age=31536000`（瀏覽器會記一年，收不回來）；不是 HTTPS 的請求回 308 轉到 https（`/health` 開頭的不轉）。**正式環境（`ASPNETCORE_ENVIRONMENT=Production`）打開它就一定要設 `Security__PublicHost`，沒設的話啟動直接失敗。** **要搭配 `ForwardedHeaders__Enabled=true`**：在代理後面卻沒讀轉送的標頭，啟動時會記一筆 Warning，而且每個請求都被轉址。 |
| `Security__PublicHost` | 無 | 對外的網域，例如 `pm.example.com`。設了之後，308 一律轉到 `https://<PublicHost><路徑與查詢字串>`，不用請求的 `Host` 標頭。打開 `Security__RequireHttps` 的正式環境必填；其他情況建議設：不設的話轉址的目標、OAuth 的 issuer 與 MCP 的網址都來自請求的 `Host`，可以被偽造。沒設時 `/mcp` 只接受來源（`Origin`）是 `localhost`、`127.0.0.1`、`[::1]` 而且和請求同源的瀏覽器請求（本機的 `http://localhost:8080` 照常用）；沒帶 `Origin` 的命令列用戶端不受影響。 |
| `DataProtection__CertificatePath` | 無 | PFX 檔的路徑。 |
| `DataProtection__RequireCertificate` | `false` | 設成 `true` 時，沒有 `DataProtection__CertificatePath` 就啟動失敗。正式環境建議打開，避免忘了掛憑證而用明文存金鑰。 |
| `DataProtection__CertificatePassword` | 無 | PFX 的密碼。 |
| `DataProtection__PreviousCertificatePaths__0`（`__1`…） | 無 | 換憑證之前用過的，只用來解開舊金鑰。密碼和目前的相同。 |
| `Bootstrap__OrganizationName`、`Bootstrap__AdminName`、`Bootstrap__AdminEmail`、`Bootstrap__AdminPassword` | 無 | 資料庫裡還沒有任何帳號時，建立第一個組織與它的擁有者（`migrate` 會做）。密碼至少 10 個字元。**第一個擁有者登入成功後就拿掉**（尤其是密碼）。 |
| `Attachments__Provider` | （必填） | `local` 或 `gcs`。雲端一律 `gcs`（第 1.6 節）。缺了或不認得時啟動失敗。 |
| `Attachments__Bucket` | 無 | `gcs` 用：bucket 名稱。 |
| `Attachments__Directory` | 無 | `local` 用：資料夾，要存在而且 UID 1654 寫得進去。本機的 `compose.yaml` 設成 `/data/attachments`。 |
| `Attachments__Quota__OrganizationGiB` | `20` | 每個組織的附件總量上限（GiB，項目與知識庫合計）。0 以上的整數，`0` 是不限（第 1.6 節）。 |
| `Attachments__Quota__PersonDailyGiB` | `2` | 每個人過去 24 小時能上傳的量（GiB）。0 以上的整數，`0` 是不限。 |
| `Notifications__Email__AllowPrivateHosts` | `false` | 寄信的主機能不能在私有網段、本機與 link-local（第 1.7 節）。內網的轉信主機才打開。 |
| `Cluster__Signals__Enabled` | `true` | api 程序之間用 PostgreSQL 的 `LISTEN/NOTIFY` 互相通知（第 1.10 節）。每個程序多一條資料庫連線；連線池的代理不能轉 `LISTEN` 時關掉。 |
| `Cli__PackagesPath` | `/app/cli` | 放 kumi 執行檔的資料夾（第 1.11 節）。資料夾不存在或是空的，就是不提供下載。 |
| `Ai__AllowPrivateEndpoints` | `false` | 組織的 AI 連線（OpenAI 相容端點）能不能連到私有網段、本機與 link-local 的位址（第 1.8 節）。自架的模型服務在內網時才打開；成員自己的 AI 帳號不受影響，一律不准。 |
| `TZ` | UTC | 只影響記錄裡的時間，例如 `Asia/Taipei`。 |

- 設定不合規定時，啟動直接失敗，訊息說得出是哪個設定。例如打開了 `ForwardedHeaders__Enabled` 卻沒有任何信任的代理、網段格式錯、憑證讀不到或密碼錯、角色名稱不合規定、`DataProtection__RequireCertificate=true` 卻沒有憑證、正式環境打開 `Security__RequireHttps` 卻沒有 `Security__PublicHost`、`Attachments__Quota__*` 不是 0 以上的整數。
- **改了 `Security__RequireHttps` 之後，Cookie 的名稱跟著變**（`pmtool_session` 與 `__Host-pmtool_session`）：已經登入的人會被登出一次，重新登入即可。
- **放在反向代理或平台的前端後面時，`Security__RequireHttps=true` 一定要搭配 `ForwardedHeaders__*`。** 沒讀轉送的標頭，api 看到的永遠是代理送來的 http，每個請求都被轉到 https，瀏覽器會一直轉址打不開。程式不擋這個組合（api 自己提供 HTTPS 時不需要轉送的標頭），要自己確認。
- 映像檔聽 8080（`ASPNETCORE_HTTP_PORTS`），不用改。
- 映像檔以 UID 1654 執行（`APP_UID`），不是 root：掛進去的檔案要讓它讀得到。

**連線字串的寫法**（Npgsql）：

- Cloud SQL 的 Unix socket（Cloud Run）：
  `Host=/cloudsql/<連線名稱>;Database=pmtool;Username=pmtool_app;Password=<密碼>`
- TCP（VM 連代管的資料庫）：
  `Host=<資料庫的位址>;Port=5432;Database=pmtool;Username=pmtool_app;Password=<密碼>;SSL Mode=VerifyFull;Root Certificate=/run/secrets/db-ca.pem`
  - 走 TCP 一定要加密。`Root Certificate` 是資料庫服務給的 CA 憑證。服務的憑證對不上主機名稱時，退一步用 `SSL Mode=VerifyCA`。
  - **VM 連 Cloud SQL 不要走這一種**：Cloud SQL 的 TCP 要嘛加授權網路（把 VM 的對外 IP 開進資料庫，不建議），要嘛用 Cloud SQL Auth Proxy。第 3.3 節的範例用 Auth Proxy。這一種連線字串留給其他雲的代管資料庫。

### 3.2 Cloud Run 加 Cloud SQL

**服務**（`pmtool`）只拿得到執行用的連線，拿不到擁有者的密碼：

| 設定 | 值 |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Database` | 祕密 `pmtool-db-runtime`：`Host=/cloudsql/<連線名稱>;Database=pmtool;Username=pmtool_app;Password=…` |
| `Database__MigrateOnStartup` | `false` |
| `ForwardedHeaders__Enabled` | `true` |
| `ForwardedHeaders__TrustAllProxies` | `true`（外面只連得到 Cloud Run 的前端） |
| `Security__RequireHttps` | `true` |
| `Security__PublicHost` | 對外的網域（`RequireHttps` 在正式環境一定要有它） |
| `DataProtection__CertificatePath` | `/secrets/dp/dataprotection.pfx`（祕密 `pmtool-dp-cert` 掛成檔案） |
| `DataProtection__CertificatePassword` | 祕密 `pmtool-dp-cert-password` |
| `TZ` | `Asia/Taipei` |

**遷移的 Job**（`pmtool-migrate`）：服務的全部設定（包括憑證的祕密也要一樣掛上：`migrate` 會檢查全部的設定，給了 `DataProtection__CertificatePath` 卻讀不到檔案就會失敗），再加上：

| 設定 | 值 |
|---|---|
| `ConnectionStrings__Migrations` | 祕密 `pmtool-db-owner`：`Host=/cloudsql/<連線名稱>;Database=pmtool;Username=pmtool;Password=…` |
| `Database__RuntimeRole` | `pmtool_app` |
| `Bootstrap__*` | 第一次部署時才給（建立第一個擁有者），之後拿掉 |
| 參數 | `migrate`（映像檔的進入點是 `dotnet PmTool.Api.dll`，加上它就是 `dotnet PmTool.Api.dll migrate`） |

**注意：前面另外加 Google 的外部負載平衡器時**，負載平衡器會在 `X-Forwarded-For` 的最後再接上它自己的 IP。api 只看最後一層，會把所有人都當成負載平衡器，限流變成全站共用一份。用 Cloud Run 自己的網址或網域對應，不用調整。一定要用負載平衡器的話，把 `ForwardedHeaders__ForwardLimit` 設成 `2`，並照第 8 節的方法確認 api 看到的是真的用戶端（命令列的瀏覽器登入比對的也是這個位址）。

#### 步驟

1. **兩個服務帳戶**：服務用 `pmtool-api`，遷移用 `pmtool-migrate`。分開才能只讓遷移拿到擁有者的密碼。

   ```
   gcloud iam service-accounts create pmtool-api --project=<專案>
   gcloud iam service-accounts create pmtool-migrate --project=<專案>
   ```

2. **祕密**。密碼用 `read -rs` 貼上（不會顯示、不會留在歷史紀錄），`printf` 組成連線字串直接送進 Secret Manager：

   ```
   read -rs APP_PW      # 貼上 pmtool_app 的密碼，按 Enter
   printf 'Host=/cloudsql/%s;Database=pmtool;Username=pmtool_app;Password=%s' '<連線名稱>' "$APP_PW" |
     gcloud secrets create pmtool-db-runtime --project=<專案> --data-file=-
   read -rs OWNER_PW    # 貼上 pmtool 的密碼
   printf 'Host=/cloudsql/%s;Database=pmtool;Username=pmtool;Password=%s' '<連線名稱>' "$OWNER_PW" |
     gcloud secrets create pmtool-db-owner --project=<專案> --data-file=-
   read -rs ADMIN_PW    # 第一個擁有者登入用的密碼，至少 10 個字元
   printf '%s' "$ADMIN_PW" | gcloud secrets create pmtool-bootstrap-password --project=<專案> --data-file=-
   unset APP_PW OWNER_PW ADMIN_PW
   gcloud secrets create pmtool-dp-cert --project=<專案> --data-file=pmtool-dp/dataprotection.pfx
   gcloud secrets create pmtool-dp-cert-password --project=<專案> --data-file=pmtool-dp/dataprotection.password
   ```

3. **權限**：兩個服務帳戶都要能連 Cloud SQL；祕密只給各自需要的。

   ```
   gcloud projects add-iam-policy-binding <專案> --role=roles/cloudsql.client \
     --member=serviceAccount:pmtool-api@<專案>.iam.gserviceaccount.com
   gcloud projects add-iam-policy-binding <專案> --role=roles/cloudsql.client \
     --member=serviceAccount:pmtool-migrate@<專案>.iam.gserviceaccount.com
   ```

   再對每個祕密給讀取的權限（`roles/secretmanager.secretAccessor`）：

   | 祕密 | `pmtool-api` | `pmtool-migrate` |
   |---|---|---|
   | `pmtool-db-runtime` | 要 | 要 |
   | `pmtool-dp-cert`、`pmtool-dp-cert-password` | 要 | 要 |
   | `pmtool-db-owner`、`pmtool-bootstrap-password` | **不要** | 要 |

   ```
   gcloud secrets add-iam-policy-binding pmtool-db-runtime --project=<專案> \
     --role=roles/secretmanager.secretAccessor \
     --member=serviceAccount:pmtool-api@<專案>.iam.gserviceaccount.com
   ```

   照表把每一格「要」都做一次（換祕密名稱與服務帳戶）。

4. **環境變數檔**。裡面沒有祕密，但有你的名字與 Email：放在自己的電腦上，不要放進 repo。

   `cloudrun-service.env.yaml`：

   ```yaml
   ASPNETCORE_ENVIRONMENT: "Production"
   Database__MigrateOnStartup: "false"
   ForwardedHeaders__Enabled: "true"
   ForwardedHeaders__TrustAllProxies: "true"
   Security__RequireHttps: "true"
   DataProtection__CertificatePath: "/secrets/dp/dataprotection.pfx"
   Attachments__Provider: "gcs"
   Attachments__Bucket: "<bucket>"
   TZ: "Asia/Taipei"
   ```

   `cloudrun-migrate.env.yaml`：上面全部，再加上

   ```yaml
   Database__RuntimeRole: "pmtool_app"
   Bootstrap__OrganizationName: "<組織名稱>"
   Bootstrap__AdminName: "<你的名字>"
   Bootstrap__AdminEmail: "<你的 Email>"
   ```

5. **遷移的 Job**（建立與更新都是這一行）：

   ```
   gcloud run jobs deploy pmtool-migrate --project=<專案> --region=<區域> \
     --image="$IMAGE" --args=migrate \
     --service-account=pmtool-migrate@<專案>.iam.gserviceaccount.com \
     --set-cloudsql-instances=<連線名稱> \
     --env-vars-file=cloudrun-migrate.env.yaml \
     --set-secrets=ConnectionStrings__Database=pmtool-db-runtime:1,ConnectionStrings__Migrations=pmtool-db-owner:1,Bootstrap__AdminPassword=pmtool-bootstrap-password:1,DataProtection__CertificatePassword=pmtool-dp-cert-password:1,/secrets/dp/dataprotection.pfx=pmtool-dp-cert:1 \
     --tasks=1 --max-retries=0 --task-timeout=10m
   gcloud run jobs execute pmtool-migrate --project=<專案> --region=<區域> --wait
   ```

   - 祕密都指定版本號（`:1`），不用 `latest`：換祕密時加一個新版本、改這裡的版本號再部署，什麼時候換的看得出來。
   - `--max-retries=0`：遷移失敗就停下來讓人看，不要自己重試。
   - `--wait` 會等它跑完；失敗時結束代碼不是 0，錯誤在 Job 的記錄裡。

6. **服務**：

   ```
   gcloud run deploy pmtool --project=<專案> --region=<區域> \
     --image="$IMAGE" --port=8080 \
     --service-account=pmtool-api@<專案>.iam.gserviceaccount.com \
     --set-cloudsql-instances=<連線名稱> \
     --env-vars-file=cloudrun-service.env.yaml \
     --set-secrets=ConnectionStrings__Database=pmtool-db-runtime:1,DataProtection__CertificatePassword=pmtool-dp-cert-password:1,/secrets/dp/dataprotection.pfx=pmtool-dp-cert:1 \
     --max-instances=1 \
     --startup-probe=httpGet.path=/health/ready,httpGet.port=8080,periodSeconds=5,timeoutSeconds=3,failureThreshold=24 \
     --liveness-probe=httpGet.path=/health,httpGet.port=8080,periodSeconds=30,timeoutSeconds=3 \
     --allow-unauthenticated
   ```

   - `--allow-unauthenticated`：網頁本身有登入，平台這一層讓所有人連得到。
   - 沒設 `--min-instances` 時，閒置一陣子之後第一個請求要等容器啟動（幾秒）。要避免就設 `--min-instances=1`，但會一直計費。

7. **第一個擁有者登入成功之後**，把 `Bootstrap__*` 從 `cloudrun-migrate.env.yaml` 與 `--set-secrets` 拿掉，重新 `gcloud run jobs deploy` 一次，再刪掉祕密 `pmtool-bootstrap-password`。

### 3.3 VM 加 docker compose 加反向代理

本機的 `compose.yaml` 也是這個做法（`migrate` 服務用 profile 擋住、`api` 沒有擁有者的連線字串與第一個擁有者的密碼、`api` 與 `backup` 唯讀根目錄加 `cap_drop`），見 `docs/OPERATIONS.md` 第 2 節。VM 的範例寫在 `docs/deployment/vm.md` 的 3.3 節：compose 的範例（api、反向代理、連 Cloud SQL 的 Auth Proxy、遷移的 profile）、`migrate.env` 與 `service.env`、IPv6 的注意事項。

## 4. 第一次部署與每次更新

### 4.1 順序

1. 建置並推送新的映像檔（第 2 節）。
2. **用新的映像檔跑 `migrate`**，等它成功結束（結束代碼 0）。
3. 成功了才把服務換成新的映像檔。
4. 確認 `/health/ready` 回 200、登入得了。

Cloud Run：

```
gcloud run jobs deploy pmtool-migrate --project=<專案> --region=<區域> --image="$IMAGE"
gcloud run jobs execute pmtool-migrate --project=<專案> --region=<區域> --wait
gcloud run deploy pmtool --project=<專案> --region=<區域> --image="$IMAGE"
```

只帶 `--image` 時，其他設定沿用上一次的。

VM（先把 `compose.yaml` 的 `image:` 改成新的標籤）：

```
docker compose pull
docker compose --profile migrate run --rm migrate
docker compose up -d
```

為什麼要這樣：

- 服務不遷移（`Database__MigrateOnStartup=false`）。忘了跑 `migrate`，新版的就緒檢查回 503，Cloud Run 不會把流量切過去，舊版照常服務。
- 好幾個容器同時啟動時，不會同時遷移。
- 遷移跑完到新版接手之間，舊版還在跑：**寫遷移時要讓上一版還能用**（例如先加欄位，下一版才刪舊的）。

### 4.2 健康檢查

| 網址 | 用途 | 回什麼 |
|---|---|---|
| `/health` | 存活（liveness） | 程序活著就是 200。資料庫一時連不上不影響，平台不會因此重啟容器。 |
| `/health/ready` | 就緒（startup、負載平衡） | 資料庫連得上、沒有還沒套用的遷移才是 200，否則 503。 |

兩個都不會被轉到 https（`Security__RequireHttps` 不擋 `/health` 開頭的），平台走內部的 http 也查得到。

### 4.3 一開始只開一個容器

- 限流的額度與工作階段的快取，是每個容器各自算的。
- 開 N 個容器：限流的額度變成 N 倍；在一個容器登出、改密碼或被停用的工作階段，其他容器最多 30 秒後才失效。
- 所以 Cloud Run 設 `--max-instances=1`，VM 只跑一個 api。要多開之前，先把限流與工作階段的作廢改成跨容器共用。

### 4.4 退回上一版

- Cloud Run：找出上一個修訂版，把流量全部切回去。

  ```
  gcloud run revisions list --service=pmtool --project=<專案> --region=<區域> --limit=5
  gcloud run services update-traffic pmtool --project=<專案> --region=<區域> --to-revisions=<上一個修訂版>=100
  ```

  - **流量這時被釘在那個修訂版上**：之後再 `gcloud run deploy`，新的修訂版建好了也不會收到流量。修好之後部署完，要再跑一次 `gcloud run services update-traffic pmtool --project=<專案> --region=<區域> --to-latest` 才會切回最新的。

- VM：`compose.yaml` 的 `image:` 換回上一個標籤，`docker compose up -d`。
- 已經跑過的遷移不會退回去。上一版跟不上新的資料表時，只能把資料庫還原到更新之前（第 6 節）。

### 4.5 記錄

- 一行一筆的 JSON（正式環境的預設），寫到標準輸出。
- Cloud Logging 會把它解析成 `jsonPayload`，但不認得裡面的欄位名稱，嚴重度都是「預設」。找錯誤用篩選：`jsonPayload.LogLevel="Error" OR jsonPayload.LogLevel="Critical"`。

## 5. 反向代理的範例

寫在 `docs/deployment/vm.md` 的第 5 節：Caddy（建議）與 nginx（參考，含 certbot）。

## 6. 備份

本機的 `scripts/ops/` 只管本機那一套，雲端的備份靠下面兩層。

**第一層：代管資料庫自己的備份**（第 1.1 節建立時已經打開）。

- 每天自動備份，預設留 7 份；時間點還原可以回到過去 7 天內的任何一秒。
- 已經建好的執行個體補開：

  ```
  gcloud sql instances patch pmtool-db --project=<專案> \
    --backup-start-time=18:00 --enable-point-in-time-recovery --retained-transaction-log-days=7 \
    --deletion-protection --retain-backups-on-delete
  ```

- **附件的檔案不在 Cloud SQL 裡**，在 bucket（第 1.6 節）。bucket 的物件版本控管（或軟刪除）是它的備份，和資料庫的備份是**兩個各自的時間線**，要自己對起來：
  - 資料庫退回某個時間點 T 之後，T 之後上傳的附件沒有資料列了，它們的檔案還在 bucket 裡，是沒人用的孤兒檔案，只佔空間，不影響使用（可以事後用 `gcloud storage ls` 對照資料庫清掉）。
  - T 之前上傳、T 之後被刪掉的附件，資料列回來了，但檔案已經被 api 從 bucket 刪掉：要從物件的非目前版本（版本控管）或軟刪除裡救回來，**在 T 之後的某個時間點之前**救才來得及（看你設的保留天數）。救回來的指令：`gcloud storage ls --all-versions gs://<bucket>/<附件編號>` 找出版本，再 `gcloud storage cp gs://<bucket>/<附件編號>#<世代> gs://<bucket>/<附件編號>`。軟刪除的用 `gcloud storage restore`。
  - 所以還原資料庫時，**同時**記下 T，並確認 bucket 的保留期限（版本控管的清理規則、軟刪除的天數）比資料庫的時間點還原的 7 天還長，兩邊才對得上。建議物件的非目前版本至少留 30 天。
  - api 刪除附件是先提交資料庫、再刪檔案；刪檔案失敗只記 Warning，檔案留著。
- 時間點還原是複製出一個**新的**執行個體，原本的不動（時間用 UTC）：

  ```
  gcloud sql instances clone pmtool-db pmtool-db-restored --project=<專案> --point-in-time='2026-11-17T01:23:45Z'
  ```

  確認資料對了，再把兩個連線字串的祕密換成新的連線名稱（加新版本、改版本號），跑 `migrate`，再部署服務。

**第二層：定期匯出一份放在別的地方**（建議每週）。專案被刪、帳號被盜時，代管的備份可能跟著不見。

- **附件也要匯出**：bucket 在同一個專案裡，專案被刪時它也跟著不見。定期把 bucket 同步到另一個帳號或另一家的儲存空間：`gcloud storage rsync --recursive gs://<bucket> gs://<另一個專案的 bucket>`（不加 `--delete-unmatched-destination-objects`，只增不刪，和本機備份的做法一樣）。檔案是使用者上傳的原始內容，沒有加密。

- 用擁有者（`pmtool`）匯出：`pg_dump --format=custom --file=pmtool-$(date +%Y%m%d).dump`（Cloud SQL 經由 Cloud SQL Auth Proxy 連線）。
- 加密之後放到另一個帳號或另一家的儲存空間（例如用 `age` 或 `gpg` 加密）。備份裡是真實資料。（本機那一套的備份可以選用 age 加密，`.env` 的 `BACKUP_AGE_RECIPIENT`，見 `docs/OPERATIONS.md` 第 3 節；這裡的 VM 備份要自己加密。）
- 從匯出的檔案還原到一個空的、屬於 `pmtool` 的資料庫：

  ```
  pg_restore --exit-on-error --single-transaction --no-owner --no-privileges -d pmtool <檔案>
  ```

  - `--no-privileges`：備份裡的權限不還原，由接著跑的 `migrate` 重新授予 `pmtool_app`。新的資料庫還沒有那個角色時，帶著權限反而會讓還原失敗。
  - 還原之後、啟動服務之前，作廢所有的工作階段（備份之後登出或被作廢的登入，在備份裡還是有效的）：

    ```sql
    UPDATE identity.sessions SET revoked_at = now(), revoked_reason = 'restored' WHERE revoked_at IS NULL;
    ```

  - 跑 `migrate`，再啟動服務。
- 至少演練一次：從匯出的檔案還原到一個用完就丟的資料庫，確認讀得回來。

## 7. 更新固定的映像檔 digest

Dockerfile 的四個 `FROM`（建置與 `cli` 兩個階段用同一個 SDK 的 digest）、`compose.yaml` 的 PostgreSQL、CI 的每個動作都固定住了：同一個標籤之後可能指向不同的內容，固定之後建置結果不會自己變。代價是安全更新不會自己進來，要定期手動更新。

**什麼時候**：每個月一次；或有安全公告的時候（.NET 每月第二個星期二出更新、Node.js 的安全更新、PostgreSQL 每季的小版本）。

### 7.1 基底映像檔

1. 查標籤現在指向的 digest。要的是最上面那行 `Digest:`（多平台的 index），不是下面 `Manifests:` 裡各平台的：

   ```
   docker buildx imagetools inspect node:24-alpine | grep -m 1 '^Digest:'
   docker buildx imagetools inspect mcr.microsoft.com/dotnet/sdk:10.0 | grep -m 1 '^Digest:'
   docker buildx imagetools inspect mcr.microsoft.com/dotnet/aspnet:10.0 | grep -m 1 '^Digest:'
   docker buildx imagetools inspect postgres:18 | grep -m 1 '^Digest:'
   docker buildx imagetools inspect mcr.microsoft.com/dotnet/runtime:10.0 | grep -m 1 '^Digest:'
   docker buildx imagetools inspect node:24-bookworm-slim | grep -m 1 '^Digest:'
   ```

2. 換掉 digest：
   - `Dockerfile` 的四個 `FROM`（SDK 的兩個要一起換），以及每一行上面寫版本的註解。
   - `deploy/ai-worker/Dockerfile` 的三個 `FROM`（SDK 和上面同一個、`dotnet/runtime`、`node:24-bookworm-slim`）。
   - `compose.yaml` 的 `x-postgres-image` 那一行（只有一個地方：db、backup（`deploy/pg-tools/Dockerfile` 以它為基底，再裝 age）與 `scripts/ops/restore-drill.sh` 都讀它）。
   - VM 上的映像檔也一樣：代理（Caddy 或 nginx）與 `cloudsql-proxy`。查法：

     ```
     docker buildx imagetools inspect caddy:2 | grep -m 1 '^Digest:'
     docker buildx imagetools inspect nginx:1.28-alpine | grep -m 1 '^Digest:'
     docker buildx imagetools inspect gcr.io/cloud-sql-connectors/cloud-sql-proxy:2 | grep -m 1 '^Digest:'
     docker run --rm caddy:2 caddy version
     ```
3. 查新的版本，寫進註解：

   ```
   docker run --rm node:24-alpine@sha256:<新的> node --version
   docker run --rm mcr.microsoft.com/dotnet/sdk:10.0@sha256:<新的> dotnet --version
   docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0@sha256:<新的> dotnet --list-runtimes
   docker run --rm postgres:18@sha256:<新的> postgres --version
   ```

4. 驗證：`docker compose build`、`dotnet test`，再用 `bash scripts/ops/deploy-local.sh` 更新本機那一套（換了 PostgreSQL 的話，資料庫的容器會重建，它會先備份）。
5. 提交：`chore: 更新基底映像檔`。

**哪些會自己進來、哪些要手動**：Dependabot 每週看每個 Dockerfile 的 `FROM`（`Dockerfile`、`deploy/ai-worker/Dockerfile`、`deploy/ai-worker/egress-proxy/Dockerfile`、`deploy/pg-tools/Dockerfile`，設定在 `.github/dependabot.yml` 的 `directories`）。**`compose.yaml` 的 `x-postgres-image` 不在它的範圍內**（compose 檔裡的 `image:` 它看不到，`pg-tools` 的 `FROM` 是用建置參數帶進來的），資料庫的映像檔要照上面的步驟手動查 digest、手動換。新增 Dockerfile 時記得把資料夾加進 `directories`。

PostgreSQL 只能換 18 的小版本（18.x）。換大版本（19）資料要另外搬，不能只改標籤。

backup 的映像檔（`deploy/pg-tools/Dockerfile`）的 `age` 是用 `apt` 從 Debian 的套件庫裝的，版本跟著基底映像檔的 Debian（目前 age 1.2.1，BSD-3-Clause），不另外固定；換了基底的 digest 之後 `docker compose build backup` 會跟著換。它只存在你自己建的映像檔裡，不隨 repo 散布。

### 7.2 GitHub Actions

1. 查最新的版本與它的提交：

   ```
   gh api repos/actions/checkout/releases/latest --jq .tag_name
   gh api repos/actions/checkout/git/ref/tags/<版本> --jq '.object.type + " " + .object.sha'
   ```

2. 第二行印出 `commit <SHA>` 就是它。印出 `tag <SHA>`（加了註解的標籤）時再查一次，拿到提交：

   ```
   gh api repos/actions/checkout/git/tags/<上一步的 SHA> --jq .object.sha
   ```

3. `.github/workflows/ci.yml` 換成 `uses: actions/checkout@<提交的 SHA> # <版本>`。大版本變了先看它的版本說明。
4. 每個動作都做一次：`actions/checkout`、`actions/setup-dotnet`、`actions/setup-node`、`docker/setup-buildx-action`、`docker/build-push-action`。

## 8. 上線前的檢查清單

**資料庫**

- [ ] PostgreSQL 18；自動備份、時間點還原、刪除保護都開著；沒有對外開放的授權網路。
- [ ] 兩個角色照第 1.2 節建好；`pmtool_app` 沒有任何屬性、不屬於任何角色（特別是 `cloudsqlsuperuser`），第 1.2 節最後的確認都做了。
- [ ] 定期匯出（第 6 節第二層）排好了，演練過一次還原。

**附件**

- [ ] bucket 和資料庫同一個區域、統一的存取控管、禁止公開存取；物件版本控管（或軟刪除）開著，保留期限比資料庫的時間點還原（7 天）長。
- [ ] 服務帳號的 `roles/storage.objectAdmin` 只綁在這個 bucket 上，不在專案層級；沒有建服務帳號金鑰。
- [ ] `Attachments__Provider=gcs`、`Attachments__Bucket` 設好，啟動記錄裡沒有附件的錯誤。
- [ ] 上傳一個檔案、下載、刪除都試過；上傳一個接近上限（25 MiB）的檔案成功，超過上限的被擋下來。
- [ ] bucket 的內容定期同步到另一個帳號（第 6 節）。

**祕密**

- [ ] 所有密碼與 PFX 都在祕密管理裡，不在映像檔、repo、指令的歷史紀錄裡。
- [ ] 服務拿不到 `ConnectionStrings__Migrations` 與 `Bootstrap__AdminPassword`，只有遷移拿得到。
- [ ] 第一個擁有者登入成功後，`Bootstrap__*` 拿掉了：Cloud Run 是環境變數檔、`--set-secrets` 與那個祕密（第 3.2 節第 7 步），VM 是 `migrate.env` 裡的那幾行（第 3.3 節）。
- [ ] PFX 與它的密碼另外存了一份在密碼管理器。

**設定**

- [ ] `ASPNETCORE_ENVIRONMENT=Production`、`Database__MigrateOnStartup=false`。
- [ ] `Security__PublicHost` 設成對外的網域（打開 `Security__RequireHttps` 的正式環境不設就起不來）。
- [ ] **`Security__RequireHttps` 與 HSTS 一送出就收不回來**（瀏覽器記一年）：確定這個網域只走 HTTPS 之後才打開，測試用的網域不要開。
- [ ] 轉送的標頭照第 3.2 節（Cloud Run）或第 3.3 節（VM）設好；打開 `RequireHttps` 的話一定要有 `ForwardedHeaders__Enabled=true`，啟動記錄裡沒有相關的 Warning。
- [ ] `ForwardedHeaders__TrustAllProxies=true` 只用在 Cloud Run 這種外面只連得到平台前端的環境。VM 開了對外的埠就不行，要用 `KnownProxies`。
- [ ] 用 Cloud Run 的網域對應的話，已經查過所選的區域支援。
- [ ] 設了 `DataProtection__CertificatePath` 與 `DataProtection__RequireCertificate=true`，啟動記錄裡沒有「沒有設定 DataProtection:CertificatePath」的 Warning。
- [ ] 只開一個容器。
- [ ] 要寄 Email 通知的話，在「寄信設定」頁寄過一封測試信給自己，收到了（第 1.7 節）。
- [ ] 監控與告警設好了，通知會送到真的有人看的地方。至少要有：
  - 就緒檢查失敗：對 `/health/ready` 設外部或 Cloud Monitoring 的 uptime check（`gcloud monitoring uptime` 或主控台），連續失敗就通知。
  - 錯誤記錄的數量：用第 4.5 節的篩選條件建一個以記錄為基礎的指標，一段時間內超過門檻就通知。
  - 資料庫的連線數與磁碟：Cloud SQL 的 `database/postgresql/num_backends` 與 `database/disk/utilization`（磁碟用到 80% 就通知）。

**部署之後**

- [ ] 映像檔是 `linux/amd64`，標籤是提交的 SHA。
- [ ] `migrate` 成功；`curl -fsS https://<網址>/health/ready` 成功。
- [ ] `curl -sI http://<網址>/` 是 30x 轉到 `https://`；`curl -sI https://<網址>/` 帶 `Strict-Transport-Security`。
- [ ] 登入之後，瀏覽器的開發者工具裡工作階段的 Cookie 叫 `__Host-pmtool_session`，有 `Secure`、`Path=/`，沒有 `Domain`。
- [ ] `curl -sI https://<網址>/v1/session` 與 `curl -sI https://<網址>/` 都帶 `Permissions-Policy` 與 `Cross-Origin-Opener-Policy: same-origin`；`/v1` 的回應是 `Cache-Control: no-store`（附件與 kumi 的下載是 `private, max-age=0`）。
- [ ] **確認 api 看到的用戶端 IP 是真的**（第 3.2 節的假設：`X-Forwarded-For` 的最後一層是用戶端）。用兩個不同的網路（例如家裡與手機的行動網路）各打一個請求，確認限流是分開算的：在其中一個網路連續送錯誤的登入直到被限流，另一個網路的登入仍然可以送出。兩邊被一起限流，就是 api 把中間的代理當成用戶端：前面有多層代理（CDN 加 nginx、負載平衡器）就設 `ForwardedHeaders__ForwardLimit`。VM 另外用 IPv6 再試一次（第 3.3 節的 docker-proxy）。
- [ ] 第一個擁有者登入成功之後，拿掉 `Bootstrap__*` 與那個祕密（第 3.2 節第 7 步）。
- [ ] 記錄裡找得到 JSON，篩得出 Error。
