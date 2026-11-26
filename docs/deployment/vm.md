# 上雲端的部署手冊：VM 加 docker compose 加反向代理

> 從 `docs/DEPLOYMENT.md` 拆出來的兩節，節號不變（3.3 與 5）。先讀 `docs/DEPLOYMENT.md` 的第 1、2 節與 3.1 的設定一覽，再照這裡做；第 4、6～8 節（部署順序、備份、更新 digest、上線前的檢查清單）回到 `docs/DEPLOYMENT.md`。

### 3.3 VM 加 docker compose 加反向代理

VM 上另外寫一份 compose（不是 repo 裡的 `compose.yaml`，那份是本機自用的，自己帶資料庫、只綁 127.0.0.1）。

目錄（`chmod 700`）：

```
pmtool/
  compose.yaml
  Caddyfile            （或 nginx.conf，第 5 節）
  api.env              （chmod 600，祕密）
  migrate.env          （chmod 600，祕密，只給遷移）
  secrets/dataprotection.pfx
  secrets/db-ca.pem    （其他雲的代管資料庫才要：資料庫服務給的 CA 憑證）
```

**資料庫怎麼連**，兩條路擇一：

- **Cloud SQL：用 Cloud SQL Auth Proxy**（下面的 compose 範例就是這一條）。proxy 是 compose 裡的一個服務，api 與 migrate 連它的 5432，它再用加密的連線連到 Cloud SQL，不用加授權網路、不用處理資料庫的 CA 憑證。proxy 需要身分：
  - 建議 VM 掛一個服務帳戶（`gcloud compute instances create … --service-account=<帳戶> --scopes=cloud-platform`，或停機之後用 `gcloud compute instances set-service-account` 改），帳戶只給 `roles/cloudsql.client`。proxy 會自己用 VM 的身分，不用放金鑰檔。
  - 沒辦法掛的話（VM 不在 GCP 上），建一個只有 `roles/cloudsql.client` 的服務帳戶、下載金鑰檔放在 `secrets/`（`chmod 400`），proxy 加 `--credentials-file`。金鑰檔外流就等於能連你的資料庫：能不用就不用。
  - proxy 到 api 之間是 compose 內部網路，連線字串用 `SSL Mode=Disable`（proxy 到 Cloud SQL 那一段是加密的）。
- **其他雲的代管資料庫：TCP 直連**。不要 proxy 服務，連線字串用第 3.1 節的 TCP 那一種（`SSL Mode=VerifyFull;Root Certificate=…`），`db-ca.pem` 掛進去。

`api.env`（Cloud SQL 加 proxy）：

```
ConnectionStrings__Database=Host=cloudsql-proxy;Port=5432;Database=pmtool;Username=pmtool_app;Password=<密碼>;SSL Mode=Disable
DataProtection__CertificatePassword=<PFX 的密碼>
```

`migrate.env`：

```
ConnectionStrings__Migrations=Host=cloudsql-proxy;Port=5432;Database=pmtool;Username=pmtool;Password=<密碼>;SSL Mode=Disable
Database__RuntimeRole=pmtool_app
Bootstrap__OrganizationName=<組織名稱>
Bootstrap__AdminName=<你的名字>
Bootstrap__AdminEmail=<你的 Email>
Bootstrap__AdminPassword=<至少 10 個字元>
```

- **第一個擁有者登入成功之後，把 `Bootstrap__AdminPassword` 那一行（與其他 `Bootstrap__*`）從 `migrate.env` 刪掉**，之後的遷移就不再需要它。檔案裡留著明文密碼沒有好處。

`compose.yaml`：代理與 api 都用固定的 IP，api 才能只信任代理那一個 IP。

```yaml
name: pmtool

x-api: &api
  image: <區域>-docker.pkg.dev/<專案>/pmtool/api:<TAG>
  env_file: [api.env]
  # 用 Cloud SQL Auth Proxy 才要。其他雲的資料庫直連的話，刪掉這兩行與下面的 cloudsql-proxy 服務。
  depends_on: [cloudsql-proxy]
  environment:
    ASPNETCORE_ENVIRONMENT: Production
    Database__MigrateOnStartup: "false"
    ForwardedHeaders__Enabled: "true"
    ForwardedHeaders__KnownProxies__0: 172.30.0.10
    Security__RequireHttps: "true"
    # 打開 RequireHttps 的正式環境一定要設：308 轉到這個網域，OAuth 的 issuer 與 MCP 的網址也由它組成，不用請求的 Host。
    Security__PublicHost: pm.example.com
    DataProtection__CertificatePath: /run/secrets/dataprotection.pfx
    # 忘了掛憑證時啟動直接失敗，不會悄悄用明文存金鑰。
    DataProtection__RequireCertificate: "true"
    TZ: Asia/Taipei
  volumes:
    - ./secrets/dataprotection.pfx:/run/secrets/dataprotection.pfx:ro
  # 縱深防禦：根目錄唯讀、拿掉所有特權、不准再提升權限。api 只寫附件資料夾（用 local 時另外掛進來）與 /tmp。
  read_only: true
  tmpfs:
    - /tmp
  cap_drop: [ALL]
  security_opt:
    - no-new-privileges:true

services:
  api:
    <<: *api
    networks:
      default:
        ipv4_address: 172.30.0.20
    # 讓一個小的 init 程序當 PID 1：程式碼處理不到的崩潰（例如堆疊溢位）會讓程序結束，
    # Docker 才看得到並依 restart 重啟；沒有它，PID 1 是 .NET 本身，行為比較難預期。
    init: true
    restart: unless-stopped
    # 不開連接埠：只有代理連得到它。

  # 遷移：docker compose --profile migrate run --rm migrate
  migrate:
    <<: *api
    env_file: [api.env, migrate.env]
    command: ["migrate"]
    profiles: [migrate]

  # Cloud SQL Auth Proxy（只有連 Cloud SQL 才要）。固定版本與 digest：標籤之後可能指向不同的內容，更新照第 7 節。
  # 2.28.0（2026-11-18 查的）。沒有 shell 與 healthcheck；--address 要設 0.0.0.0，別的容器才連得到。
  # 服務帳戶用 VM 掛的那一個。改用金鑰檔的話，加 volumes 與 --credentials-file=/secrets/proxy.json。
  cloudsql-proxy:
    image: gcr.io/cloud-sql-connectors/cloud-sql-proxy:2.28.0@sha256:e0cbea7b63455c1635d354ef456696d019dd02a5db47bf1994e849823e71ae89
    command: ["--address=0.0.0.0", "--port=5432", "<連線名稱>"]
    # 不開連接埠：只有同一個 compose 網路裡的 api 與 migrate 連得到。
    restart: unless-stopped

  proxy:
    # Caddy 2.11.9（2026-11-18 查的），固定到 digest（多平台的 index）。更新照第 7 節。
    image: caddy:2.11.9@sha256:4851e54a46b206517c574821bb5cc67f170d2156041e9fbc4c9193b45e84a75b
    ports:
      - "80:80"
      - "443:443"       # 只開 TCP：Caddyfile 關掉了 HTTP/3（QUIC），不用 UDP 443。
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
    networks:
      default:
        ipv4_address: 172.30.0.10
    restart: unless-stopped

networks:
  default:
    ipam:
      config:
        - subnet: 172.30.0.0/24

volumes:
  caddy-data:
```

- 檔案要讓 UID 1654 讀得到：`sudo chown 1654 secrets/* && chmod 400 secrets/*`。
- Docker 發佈的連接埠，IPv6 的用戶端會先經過 `docker-proxy`，api 收到的來源 IP 可能變成 Docker 的閘道位址（`172.30.0.1` 之類），不是真的用戶端。症狀是 IPv6 的用戶端全被當成同一個人、一起被限流。要避免，把 VM 的 IPv6 先關掉（網域只設 A 紀錄、不設 AAAA），或在 `/etc/docker/daemon.json` 設 `"ip6tables": true` 並啟用 IPv6（Docker 的文件有寫）。照第 8 節的方法用 IPv6 與 IPv4 各打一次確認。
- `init: true` 只加在 api 服務上；migrate 跑完就結束，不需要。
- `172.30.0.0/24` 和 VM 所在的網路衝突時，換一個網段，三個地方（`subnet`、兩個 `ipv4_address`、`KnownProxies__0`）一起換。
- api 不開連接埠：外面的請求只能經過代理。要是開了，別人可以繞過代理、自己帶假的 `X-Forwarded-For`。

**設定錯了的症狀**：

| 看到的 | 原因 |
|---|---|
| 瀏覽器說「重新導向太多次」，或每個請求都是 308 | api 不信任代理（`KnownProxies__0` 和代理的 IP 對不上），看不到 `X-Forwarded-Proto: https`，以為是 http |
| 大家一起被限流、登入常常說太多次 | 同上：所有人都被當成代理的 IP |
| api 起不來，訊息提到 `ForwardedHeaders` | 打開了 `Enabled`，卻沒有 `KnownProxies`、`KnownNetworks` 或 `TrustAllProxies` |


## 5. 反向代理的範例

兩個都假設代理和 api 在第 3.3 節的同一個 compose 網路裡，代理的 IP 是 `172.30.0.10`。api 那邊的設定都一樣：

```
ForwardedHeaders__Enabled=true
ForwardedHeaders__KnownProxies__0=172.30.0.10
Security__RequireHttps=true
```

### Caddy

`Caddyfile`：

```
{
	# 只用 HTTP/1.1：不開 HTTP/2 與 HTTP/3（理由見下面）。
	servers {
		protocols h1
	}
}

# http 轉到 https 要自己寫：Caddy 自動產生的轉址不吃上面的設定，80 埠會照樣接受 HTTP/2。
http://pm.example.com {
	redir https://{host}{uri} 308
}

pm.example.com {
	reverse_proxy api:8080
}
```

- Caddy 自己申請 HTTPS 憑證，`caddy-data` 資料卷放著憑證與帳號，不要刪。自己寫的 80 埠轉址不影響申請：Let's Encrypt 在 80 埠的驗證照樣由 Caddy 回應。
- **不開 HTTP/2 與 HTTP/3**：Caddy 預設三種都開，最上面的全域設定把它們關掉。HTTP/2 的實作一再出現用少量連線就能拖垮伺服器的弱點（例如 2023 年的 Rapid Reset、2024 年的 CONTINUATION flood），Kumi 用 HTTP/1.1 就夠。改了之後兩個連接埠都要確認：`openssl s_client -connect pm.example.com:443 -alpn h2,http/1.1 </dev/null 2>/dev/null | grep ALPN` 應該是 `http/1.1`；`curl --http2-prior-knowledge http://pm.example.com/` 應該失敗（不是 `308`）。
- 只用 HTTP/1.1 時，瀏覽器對同一個網站最多同時開 6 條連線，所以即時更新一定要走 WebSocket（Caddy 會自己轉）：退回長輪詢的話，每個開著的分頁一直佔住一條，分頁一多整個網站就卡住。
- 防火牆要開 TCP 80 與 TCP 443。
- 它自己帶上 `X-Forwarded-For`（用戶端的 IP）與 `X-Forwarded-Proto`，並丟掉用戶端自己送來的同名標頭，不用另外寫。
- 即時更新（`/v1/live`）的 WebSocket，Caddy 的 `reverse_proxy` 自己會轉，不用另外設定。

### nginx

**VM 建議用 Caddy**（自己申請與更新憑證，設定少）。nginx 只是給已經有 nginx 與憑證流程的人參考。

要點：

- 憑證用 certbot 取得，驗證走 80 埠的 webroot：80 埠先放行 `/.well-known/acme-challenge/`，其他才轉到 https。
- 映像檔固定版本：這裡用 `nginx:1.28-alpine`（1.28.3，2026-11-18 查的），固定到 digest。
- **不開 HTTP/2**（理由見上面 Caddy 的那一段）：新版不寫 `http2 on;`，舊版（例如 Ubuntu 22.04 的 1.18）是 `listen 443 ssl;` 後面不加 `http2`。nginx 的 HTTP/2 是開在整個「位址:連接埠」上的：同一個 443 只要有一個 `server` 開了，這個連接埠上的每個網站都是開的。主機上還有別的網站時，先 `nginx -T | grep -E 'listen|http2'` 看全部，再用上面的 `openssl s_client` 確認是 `http/1.1`。
- **沒有網域名稱的請求一律拒絕**：直接用 IP 或別的網域名稱連進來的請求，不該落到 Kumi 的 `server`。下面的範例在 80 與 443 各加一個 `default_server`（80 回 444 直接斷線，443 用 `ssl_reject_handshake on;` 在握手就拒絕，不需要憑證；要 nginx 1.19.4 以上）。`ssl_reject_handshake` 是掛在「同一個位址:連接埠的預設 `server`」上的：主機上還有別的網站時，先用 `nginx -T | grep -E 'listen|default_server'` 看 80 與 443 現在誰是預設，不要有兩個 `default_server`。主機有監聽 IPv6 時（`listen [::]:80;`、`listen [::]:443 ssl;`），`[::]:80` 與 `[::]:443` 也要各有一個 `default_server`（範例已經寫了；沒有 IPv6 的主機把這兩行拿掉）：沒有的話，直接用 IPv6 位址連進來的請求會落到 Kumi 的 `server`。
- `http` 區塊的 `server_tokens off;` 讓錯誤頁與回應標頭不帶 nginx 的版本號（範例的檔案掛在 `conf.d/`，本來就在 `http` 裡，直接寫在檔案最上面）。
- `AllowedHosts` 維持預設的 `*`：平台的健康檢查常走內部的 IP，限制了會被擋；擋掉陌生網域名稱的是上面的預設 `server`。
- `proxy_http_version 1.1;` 不能省（見下面 `nginx.conf` 裡的註解）。2026-11-21 第一次在 VM 上照做時，就是少了這一行，沒有內文的 POST 一律 400。
- 即時更新（`/v1/live`）用 WebSocket：nginx 預設不轉升級的標頭，要照下面的 `map` 與兩行 `Upgrade`、`Connection` 寫。少了的話網頁還能用（SignalR 會退回長輪詢），但每個開著的分頁一直佔住一條連線，只用 HTTP/1.1 時分頁一多整個網站就卡住（見上面 Caddy 的那一段）；瀏覽器的 Network 裡 `/v1/live` 沒有變成 101 就是少了。
- compose 裡的 `proxy` 服務換成下面這一段（`ipv4_address` 與 Caddy 的一樣，api 的 `KnownProxies__0` 不用改）：

  ```yaml
    proxy:
      image: nginx:1.28-alpine@sha256:a8b39bd9cf0f83869a2162827a0caf6137ddf759d50a171451b335cecc87d236
      ports:
        - "80:80"
        - "443:443"
      volumes:
        - ./nginx.conf:/etc/nginx/conf.d/default.conf:ro
        - /etc/letsencrypt:/etc/letsencrypt:ro   # certbot 放憑證的地方。live/ 底下是連結，要整個目錄掛。
        - /var/www/certbot:/var/www/certbot:ro   # 驗證用的檔案
      networks:
        default:
          ipv4_address: 172.30.0.10
      restart: unless-stopped
  ```

`nginx.conf`：

```
# 即時更新的 WebSocket：有 Upgrade 標頭時轉成 upgrade，其他請求照舊關掉連線。
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

# 不顯示 nginx 的版本號。
server_tokens off;

# 沒有對上網域名稱的請求（直接用 IP 連、別的網域指到這台）：80 直接斷線，443 在 TLS 握手就拒絕。
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name _;
    return 444;
}

server {
    # 同 443 的其他 server：不寫 http2。
    listen 443 ssl default_server;
    listen [::]:443 ssl default_server;
    server_name _;
    ssl_reject_handshake on;
}

server {
    listen 80;
    listen [::]:80;
    server_name pm.example.com;

    # certbot 驗證網域用，必須是 http、不能轉址。
    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    location / {
        return 308 https://pm.example.com$request_uri;
    }
}

server {
    # 不寫 http2 on;：只用 HTTP/1.1。
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name pm.example.com;
    ssl_certificate     /etc/letsencrypt/live/pm.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/pm.example.com/privkey.pem;

    # 藍圖文件最大約 1 MiB，預設的 1m 放不下；附件單檔最大 25 MiB（AttachmentRules.MaxBytes）加上 multipart 的其他內容，
    # 所以整個站設 30m。api 自己對每個端點再擋一次，這裡只是不要太小。
    client_max_body_size 30m;

    location / {
        proxy_pass http://api:8080;
        # 一定要寫：nginx 預設用 HTTP/1.0 轉給 api，沒有內文的 POST（套用變更集、封存、停用人員）
        # 若用戶端沒帶 Content-Length（例如 curl -X POST），Kestrel 會直接回 400。瀏覽器與 kumi 會帶，所以不容易發現。
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        # 用 $remote_addr 蓋掉用戶端自己送來的，不要用 $proxy_add_x_forwarded_for 接在後面。
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
        # 即時更新的 WebSocket。逾時用預設的 60 秒就好：SignalR 每 15 秒送一次心跳。
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
    }
}
```

**第一次取得憑證**（雞生蛋：443 的區塊要憑證才起得來）：

1. 先把 `nginx.conf` 裡 `server_name pm.example.com;` 的那個 443 `server` 整段註解掉（預設的 443 `server` 不需要憑證，留著），`sudo mkdir -p /var/www/certbot`，`docker compose up -d proxy`。
2. 在 VM 上裝 certbot（例如 `sudo apt install certbot`），取得憑證：

   ```
   sudo certbot certonly --webroot -w /var/www/certbot -d pm.example.com --email <你的 Email> --agree-tos
   ```

3. 把 443 的區塊還原，`docker compose exec proxy nginx -t && docker compose exec proxy nginx -s reload`。

**自動更新**：apt 裝的 certbot 會自己排定時更新（`systemctl list-timers | grep certbot` 看得到）。更新完要讓 nginx 重新讀憑證，加一個更新後的動作：

```
sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh >/dev/null <<'EOF'
#!/bin/sh
docker compose -f /<路徑>/pmtool/compose.yaml exec -T proxy nginx -s reload
EOF
sudo chmod 755 /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
sudo certbot renew --dry-run
```

`--dry-run` 成功才算設好。
