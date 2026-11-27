# 主機 AI 代理的 worker（internal/HANDOFF-host-agent.md 第 8 節）：kumi host run 加上 Claude Code 與 Codex。
# 從專案根目錄建置：docker build -f deploy/ai-worker/Dockerfile -t pmtool-aiw .
# 映像檔裡不放任何憑證：機器憑證（/home/kumi/.kumi-host）與引擎的登入（/home/kumi/engine-claude、/home/kumi/engine-codex）都存在資料卷，由 kumi host pair／login 在主機上產生。
# 基底映像檔固定到 digest（多平台的 index），標籤只是給人看的；怎麼更新見 docs/DEPLOYMENT.md 第 7 節。

# 建置階段：只發佈 CLI（framework-dependent）與它引用的藍圖模組。
# .NET SDK 10.0.404（和根目錄 Dockerfile 同一個 digest）
FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PmTool.Cli/PmTool.Cli.csproj src/PmTool.Cli/
COPY src/Modules/PmTool.Blueprint/PmTool.Blueprint.csproj src/Modules/PmTool.Blueprint/
RUN dotnet restore src/PmTool.Cli/PmTool.Cli.csproj
COPY schemas/ schemas/
# kumi licenses 把這三份內嵌進執行檔（PmTool.Cli.csproj）；少了建置會失敗。
COPY LICENSE NOTICE THIRD-PARTY-NOTICES.txt ./
# kumi skill install 把 skills/ 內嵌進執行檔（PmTool.Cli.csproj）；少了建置會失敗。
COPY skills/ skills/
COPY src/PmTool.Cli/ src/PmTool.Cli/
COPY src/Modules/PmTool.Blueprint/ src/Modules/PmTool.Blueprint/
# 產生 kumi 這個啟動檔（apphost），ENTRYPOINT 直接用它。
RUN dotnet publish src/PmTool.Cli/PmTool.Cli.csproj -c Release --no-restore -o /app -p:UseAppHost=true

# node 與 npm：只為了裝兩個引擎的 CLI。從固定 digest 的 node 映像檔複製（glibc 版，和下面的執行環境相容）。
# Node.js 24（2026-11-24 查的）
FROM node:25-bookworm-slim@sha256:81db02c4b671288a03915da9534dbd54f96d0e7c24d80ccc54f5b36b2e684370 AS node

# 執行階段：.NET 執行環境（不含 ASP.NET，CLI 用不到）。
# .NET 執行環境 10.0（2026-11-24 查的）
FROM mcr.microsoft.com/dotnet/runtime:11.0@sha256:43a34aaf5edd4ae1a36dd82fe857602e4b5449bfcf1a9bed753eeb0727dc0c70 AS final

COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx

# 引擎 CLI：版本釘在 package.json、整棵依賴樹（含各平台的原生套件）釘在 package-lock.json，用 npm ci 照 lockfile 裝，
# 不會因為建置的時間不同而裝到別的東西（OPS-08）。本機代理的旗標與輸出格式是在這兩個版本上驗過的
# （internal/HANDOFF-local-agent.md 第 5 節；升級前要重驗，流程見 docs/OPERATIONS.md 第 17 節）。
# 這兩個引擎不屬於 Kumi、不在 Kumi License 的範圍：Codex 是 Apache-2.0，Claude Code 適用 Anthropic 自己的條款、不能轉散布。
# 所以這個映像檔只給部署的人自己建，不要把建好的推到公開的映像檔倉庫。
WORKDIR /opt/engines
COPY deploy/ai-worker/package.json deploy/ai-worker/package-lock.json ./
RUN npm ci --omit=dev --no-fund --no-audit \
 && npm cache clean --force \
 && ln -s /opt/engines/node_modules/.bin/claude /usr/local/bin/claude \
 && ln -s /opt/engines/node_modules/.bin/codex /usr/local/bin/codex

# 三個帳號（AG-05）：kumi（10001）拿著機器憑證；Claude Code（10002）與 Codex（10003）各用自己的帳號與家目錄，讀別人寫的內容、可能被提示注入。
# kumi 啟動引擎時切換成那個引擎的帳號：引擎的程序讀不到 kumi 的狀態檔、也碰不到 kumi 的程序，兩個引擎之間也彼此讀不到。
# 引擎的家目錄放在資料卷裡（/home/kumi/engine-claude、/home/kumi/engine-codex，引擎的登入存在這裡），kumi 的狀態在 /home/kumi/.kumi-host。
RUN groupadd --gid 10001 kumi && useradd --uid 10001 --gid 10001 --create-home --home-dir /home/kumi --shell /usr/sbin/nologin kumi \
 && groupadd --gid 10002 engine-claude && useradd --uid 10002 --gid 10002 --no-create-home --home-dir /home/kumi/engine-claude --shell /usr/sbin/nologin engine-claude \
 && groupadd --gid 10003 engine-codex && useradd --uid 10003 --gid 10003 --no-create-home --home-dir /home/kumi/engine-codex --shell /usr/sbin/nologin engine-codex
COPY --from=build /app /opt/kumi
# 啟動器（降成 kumi 帳號）與引擎的包裝檔（切換成引擎帳號）；真正的 kumi 在 /opt/kumi/kumi。
COPY --chmod=0755 deploy/ai-worker/libexec/kumi /usr/local/bin/kumi
COPY --chmod=0755 deploy/ai-worker/libexec/as-engine deploy/ai-worker/libexec/claude deploy/ai-worker/libexec/codex /usr/local/libexec/kumi/

# 不寫 USER：容器以 root 啟動，由 /usr/local/bin/kumi 整理資料卷後降成 kumi 帳號。root 只在那幾毫秒，
# compose 只給它 chown、讀寫略過、setuid／setgid 這幾個能力，其他的全部拿掉。
WORKDIR /home/kumi
ENV HOME=/home/kumi \
    KUMI_HOST_STATE_DIR=/home/kumi/.kumi-host \
    KUMI_AGENT_CLAUDE_PATH=/usr/local/libexec/kumi/claude \
    KUMI_AGENT_CODEX_PATH=/usr/local/libexec/kumi/codex \
    CODEX_HOME=/home/kumi/engine-codex/.codex \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DISABLE_AUTOUPDATER=1
ENTRYPOINT ["kumi", "host", "run"]
