# 三個基底映像檔都固定到 digest（多平台的 index）：同一個標籤之後可能指向不同的內容，建置結果就跟著變。
# 標籤只是給人看的，拉的是 digest。每一行上面寫著 2026-11-17 查的時候裡面的版本。
# 怎麼更新見 docs/DEPLOYMENT.md 第 7 節。

# 網頁建置階段：Vite 把 React 應用建置成靜態檔，之後由 API 主機一起提供。
# 產物和平台無關，用建置機器本身的平台跑（在 Mac 上建 amd64 的映像檔時不必經過模擬）。
# Node.js 24.22.3
FROM --platform=$BUILDPLATFORM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5 AS web
WORKDIR /web

# 先只複製套件清單做安裝。原始碼改動不會讓這一層失效，套件不必每次重抓。
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY web/ ./
RUN npm run build

# 建置階段：只還原、發佈 API 與它引用的模組，測試專案不進映像檔。
# 用建置機器本身的平台跑：發佈的是和平台無關的組件（UseAppHost=false、不指定 RID），只有最後的執行環境要是目標平台。
# 在 Mac 上建 amd64 的映像檔時，模擬下的 dotnet restore 曾經卡住一個多小時。
# .NET SDK 10.0.404（執行環境 10.0.15）
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

# 先只複製專案檔與共用設定做還原。原始碼改動不會讓這一層失效，套件不必每次重抓。
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PmTool.Api/PmTool.Api.csproj src/PmTool.Api/
COPY src/Modules/PmTool.Ai.Contracts/PmTool.Ai.Contracts.csproj src/Modules/PmTool.Ai.Contracts/
COPY src/Modules/PmTool.Ai/PmTool.Ai.csproj src/Modules/PmTool.Ai/
COPY src/Shared/PmTool.Abstractions/PmTool.Abstractions.csproj src/Shared/PmTool.Abstractions/
COPY src/Shared/PmTool.Http/PmTool.Http.csproj src/Shared/PmTool.Http/
COPY src/Shared/PmTool.Storage/PmTool.Storage.csproj src/Shared/PmTool.Storage/
COPY src/Modules/PmTool.Blueprint/PmTool.Blueprint.csproj src/Modules/PmTool.Blueprint/
COPY src/Modules/PmTool.Identity.Contracts/PmTool.Identity.Contracts.csproj src/Modules/PmTool.Identity.Contracts/
COPY src/Modules/PmTool.Identity/PmTool.Identity.csproj src/Modules/PmTool.Identity/
COPY src/Modules/PmTool.Knowledge.Contracts/PmTool.Knowledge.Contracts.csproj src/Modules/PmTool.Knowledge.Contracts/
COPY src/Modules/PmTool.Knowledge/PmTool.Knowledge.csproj src/Modules/PmTool.Knowledge/
COPY src/Modules/PmTool.Notifications.Contracts/PmTool.Notifications.Contracts.csproj src/Modules/PmTool.Notifications.Contracts/
COPY src/Modules/PmTool.Notifications/PmTool.Notifications.csproj src/Modules/PmTool.Notifications/
COPY src/Modules/PmTool.Work.Contracts/PmTool.Work.Contracts.csproj src/Modules/PmTool.Work.Contracts/
COPY src/Modules/PmTool.Work/PmTool.Work.csproj src/Modules/PmTool.Work/
RUN dotnet restore src/PmTool.Api/PmTool.Api.csproj

# schemas/ 要放在和 repo 相同的相對位置：藍圖模組用萬用字元內嵌 Schema，路徑對不到時建置不會失敗，只會內嵌零個檔案。
COPY schemas/ schemas/
# AI 助理的系統提示內嵌「結構怎麼決定」的建議，和 CLI 的代理共用同一份原文（PmTool.Ai.csproj）。
COPY skills/pm-tool-planning/references/structure.md skills/pm-tool-planning/references/
COPY src/ src/
RUN dotnet publish src/PmTool.Api/PmTool.Api.csproj -c Release --no-restore -o /app/publish -p:UseAppHost=false

# kumi 的執行檔階段：伺服器提供登入後下載的 macOS／Windows 單一執行檔（internal/HANDOFF-cli-downloads.md）。
# 用建置機器本身的平台跑：輸出和映像檔的平台無關，在 Mac 上建 amd64 的映像檔時這一段不必經過模擬。
# .NET SDK 在 Linux 上也會替 macOS 的執行檔加 ad-hoc 簽章，Apple 晶片的 Mac 才跑得起來。
# .NET SDK 10.0.404（和上面的建置階段同一個 digest）
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS cli
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PmTool.Cli/PmTool.Cli.csproj src/PmTool.Cli/
COPY src/Modules/PmTool.Blueprint/PmTool.Blueprint.csproj src/Modules/PmTool.Blueprint/
# 三個平台各還原一次：各平台的執行環境套件（每個約 70 MB）留在這一層，原始碼改動時不必重抓。
RUN for rid in osx-arm64 osx-x64 win-x64; do \
      dotnet restore src/PmTool.Cli/PmTool.Cli.csproj -r "$rid" -p:SelfContained=true -p:PublishSingleFile=true; \
    done
COPY schemas/ schemas/
# kumi licenses 把這三份內嵌進執行檔（PmTool.Cli.csproj）；少了建置會失敗。
COPY LICENSE NOTICE THIRD-PARTY-NOTICES.txt ./
COPY scripts/cli/build-packages.sh scripts/cli/
COPY src/PmTool.Cli/ src/PmTool.Cli/
COPY src/Modules/PmTool.Blueprint/ src/Modules/PmTool.Blueprint/
RUN bash scripts/cli/build-packages.sh /out

# 執行階段：只有 ASP.NET Core 執行環境，以非 root 身分執行，程式檔屬於 root，執行身分改不動它們。
# 容器裡不存任何東西（工作階段 Cookie 的加密金鑰存在資料庫），所以可以隨時換掉、同時開好幾個。
# ASP.NET Core 執行環境 10.0.15
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=web /web/dist ./wwwroot
# GET /v1/cli 讀的就是這個資料夾（Cli:PackagesPath 的預設值）。
COPY --from=cli /out/ ./cli/
# 授權與第三方聲明跟著映像檔走：裡面的套件（MIT、Apache-2.0、BSD 這類）都要求散布時附上條文與版權聲明。
COPY LICENSE NOTICE THIRD-PARTY-NOTICES.txt ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PmTool.Api.dll"]
