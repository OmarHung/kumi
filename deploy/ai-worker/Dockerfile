# 主機 AI 代理的 worker（internal/HANDOFF-host-agent.md 第 8 節）：kumi host run 加上 Claude Code 與 Codex。
# 從專案根目錄建置：docker build -f deploy/ai-worker/Dockerfile -t pmtool-aiw .
# 映像檔裡不放任何憑證：機器憑證與引擎的登入都存在資料卷（/home/kumi），由 kumi host pair／login 在主機上產生。
# 基底映像檔固定到 digest（多平台的 index），標籤只是給人看的；怎麼更新見 docs/DEPLOYMENT.md 第 7 節。

# 建置階段：只發佈 CLI（framework-dependent）與它引用的藍圖模組。
# .NET SDK 10.0.404（和根目錄 Dockerfile 同一個 digest）
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PmTool.Cli/PmTool.Cli.csproj src/PmTool.Cli/
COPY src/Modules/PmTool.Blueprint/PmTool.Blueprint.csproj src/Modules/PmTool.Blueprint/
RUN dotnet restore src/PmTool.Cli/PmTool.Cli.csproj
COPY schemas/ schemas/
# kumi licenses 把這三份內嵌進執行檔（PmTool.Cli.csproj）；少了建置會失敗。
COPY LICENSE NOTICE THIRD-PARTY-NOTICES.txt ./
COPY src/PmTool.Cli/ src/PmTool.Cli/
COPY src/Modules/PmTool.Blueprint/ src/Modules/PmTool.Blueprint/
# 產生 kumi 這個啟動檔（apphost），ENTRYPOINT 直接用它。
RUN dotnet publish src/PmTool.Cli/PmTool.Cli.csproj -c Release --no-restore -o /app -p:UseAppHost=true

# node 與 npm：只為了裝兩個引擎的 CLI。從固定 digest 的 node 映像檔複製（glibc 版，和下面的執行環境相容）。
# Node.js 24（2026-11-24 查的）
FROM node:24-bookworm-slim@sha256:dc26ae92c9fb15cf6b1d63e20f0944b2efe0f115b20e00be48fb23fceb061c54 AS node

# 執行階段：.NET 執行環境（不含 ASP.NET，CLI 用不到）。
# .NET 執行環境 10.0（2026-11-24 查的）
FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:0a0e20d12b84b081fb737fddb92d8d205edce9ac2dc62796d2aab30f03804498 AS final

COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx

# 引擎 CLI 固定版本（本機代理的旗標與輸出格式是在這兩個版本上驗過的，internal/HANDOFF-local-agent.md 第 5 節；升級前要重驗）。
# 這兩個引擎不屬於 Kumi、不在 Kumi License 的範圍：Codex 是 Apache-2.0，Claude Code 適用 Anthropic 自己的條款、不能轉散布。
# 所以這個映像檔只給部署的人自己建，不要把建好的推到公開的映像檔倉庫。
RUN npm install -g --no-fund --no-audit @anthropic-ai/claude-code@2.1.330 @openai/codex@0.163.0 \
 && npm cache clean --force

# 非 root 使用者；HOME 是資料卷（引擎的登入與 kumi 的狀態都在裡面）。
RUN groupadd --gid 10001 kumi && useradd --uid 10001 --gid 10001 --create-home --home-dir /home/kumi --shell /usr/sbin/nologin kumi
COPY --from=build /app /opt/kumi
RUN ln -s /opt/kumi/kumi /usr/local/bin/kumi

USER kumi
WORKDIR /home/kumi
ENV HOME=/home/kumi \
    KUMI_HOST_STATE_DIR=/home/kumi/.kumi-host \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DISABLE_AUTOUPDATER=1
ENTRYPOINT ["kumi", "host", "run"]
