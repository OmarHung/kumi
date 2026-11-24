# pm-tool

跨平台專案管理工具的後端與相關程式碼。產品名稱是 **Kumi**（2026-11-23 定名）：畫面、CLI 的指令 `kumi`、MCP 伺服器的名稱 `kumi`、信件、AI 助理，凡是使用者看得到的都叫 Kumi（網頁從 `web/src/config/product.ts` 的 `PRODUCT_NAME`、`COMMAND_NAME` 拿）。repo、命名空間（`PmTool.*`）、資料庫的 schema 與角色、compose 的專案、權杖前綴 `pmt_` 這些內部代號照舊叫 pm-tool／PmTool，不改；Data Protection 的應用程式名稱（`SessionAuthentication` 的 `pm-tool`）一改所有 Cookie 與加密的金鑰都會失效，絕對不能動。

這個 repo 是公開的。不公開的開發紀錄在另一個私人的 repo `kumi-internal`，clone 在 `internal/`（`.gitignore` 忽略它，要自己 `git clone https://github.com/OmarHung/kumi-internal.git internal`；沒有權限的人沒有這個資料夾，程式註解裡指到 `internal/` 的連結打不開）：

- 進行中的工作與分工寫在 `internal/HANDOFF-*.md`，開工前先讀最新的那一份。
- 設計規劃書在 `internal/planning/`，索引見該目錄的 `README.md`；安全性審查的報告在 `internal/review-report/`。
- 交接文件寫好之後**在 `internal/` 裡提交並推送**：它是另一個 repo，這裡的 `git status` 看不到它的修改。
- worktree 裡沒有 `internal/`（被忽略的資料夾不會跟過去）：開了 worktree 先 `ln -s <主目錄>/internal internal`，交接文件一律在主目錄的 `internal/` 提交。

## 指令

- 建置：`dotnet build`
- 測試：`dotnet test`
- 測試加覆蓋率門檻：`dotnet test /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line`
- 網頁（在 `web/` 底下）：`npm run typecheck`、`npm run lint`（ESLint 之後接著跑樣式檢查）、`npm run test:coverage`、`npm run build`
- 只跑樣式檢查（在 `web/` 底下）：`node scripts/check-styles.mjs`，規則見 `docs/DESIGN.md` 第 13 節；共用元件的展示頁是開發伺服器的 `/design`
- 本機執行整套：`bash scripts/ops/deploy-local.sh`（先備份再 `up --build`，等到就緒），然後開 `http://localhost:8080`（密碼在不進版控的 `.env`）。8080 上有使用者自己的資料，更新一律用這支腳本，不要直接 `docker compose up --build`，也不要 `down -v`
- 本機維運：`bash scripts/ops/status.sh`（服務、就緒、備份、錯誤）、`errors.sh`、`backup-now.sh`、`restore-drill.sh`（在用完就丟的資料庫裡演練還原，不碰 8080），說明在 `docs/OPERATIONS.md`。`backups/` 是真實資料的備份，不進版控。改了 `scripts/ops/` 或 `compose.yaml` 之後要跑一次 `deploy-local.sh`：備份服務是從 repo 掛進去的腳本，容器重啟時會用新的腳本配舊的設定
- CLI（`src/PmTool.Cli`，指令 `kumi`；2026-11-23 以前叫 `pm`，環境變數 `KUMI_SERVER`／`KUMI_TOKEN`／`KUMI_ALLOW_INSECURE`，舊的 `PM_*` 與設定檔資料夾 `~/.config/pm-tool/` 照樣認得）：`dotnet pack src/PmTool.Cli -o <目錄>`，再 `dotnet tool install --tool-path <目錄> --add-source <目錄> PmTool.Cli`；試的時候把 `XDG_CONFIG_HOME` 指到 scratchpad，不要寫進使用者的 `~/.config`。用法見 `docs/CLI.md`。給 AI 代理用的 skill 在 `skills/`：改了 CLI 的指令、選項、輸出的 JSON 或結束代碼，`docs/CLI.md` 與 `skills/pm-tool/` 要一起改；改了內建模板（`src/Modules/PmTool.Work/Templates/`），`skills/pm-tool-planning/references/templates.md` 要一起改
- 給同事下載的 kumi 執行檔：`bash scripts/cli/build-packages.sh <空的資料夾>`，建出 macOS（Apple 晶片、Intel）與 Windows 的單一執行檔（不用 .NET）；映像檔的 `cli` 階段跑的就是它。在 Linux 的容器裡建的 macOS 執行檔有 ad-hoc 簽章、Apple 晶片跑得起來；瀏覽器下載的帶隔離標記，要 `xattr -c` 才跑得起來
- 上雲：照 `docs/DEPLOYMENT.md`。遷移與服務分開：`dotnet PmTool.Api.dll migrate`（遷移、授權給執行的角色、建立第一個擁有者，做完就結束），服務設 `Database__MigrateOnStartup=false`
- 第三方聲明：`node scripts/licenses/third-party-notices.mjs`（重新產生 `THIRD-PARTY-NOTICES.txt`；加 `--check` 只比對）。要先 `dotnet restore`、在 `web/` 底下 `npm ci`
- 提交前的掃描：`bash scripts/precommit-scan.sh`；以本機的第一個擁有者登入拿 Cookie：`bash scripts/dev-login.sh <檔案>`
- 新增資料表遷移：`dotnet ef migrations add <名稱> --project src/Modules/<模組> --output-dir Persistence/Migrations`

## 架構

- 模組化單體。每個模組一個專案，放在 `src/Modules/`；`src/PmTool.Api` 是唯一的組合根。
- 模組之間只透過公開介面互相使用，不引用別的模組的 internal 型別。
- `PmTool.Blueprint`：藍圖格式的讀取、驗證與計畫（比對兩版藍圖、算出對既有資料的影響）。純邏輯，不碰資料庫與網路。
- `schemas/blueprint/v0/` 是藍圖格式的唯一依據，建置時內嵌進 `PmTool.Blueprint`。
- `PmTool.Identity`（組織、人員、登入）與 `PmTool.Work`（專案、藍圖版本、成員、工作項目）各有一個 `*.Contracts` 專案放對外的介面與型別。API 只認合約，實作在 `src/PmTool.Api/Composition/ModuleRegistration.cs` 接上。
- 資料庫是 PostgreSQL。每個模組自己的 `DbContext`、自己的 schema、自己的遷移；模組之間不建外鍵。每張表都帶 `organization_id`，每個查詢都用它過濾。
- 權限檢查在模組裡做（看 `Actor`），API 只負責確認登入。預期中的失敗回 `Result<T>`，不丟例外。
- 資料庫有兩個角色：執行用的（本機是 `pmtool_app`，只能讀寫資料）與資料表的擁有者（`pmtool`，只在遷移時用，`ConnectionStrings:Migrations`）。遷移完由各模組把自己 schema 的讀寫權限授予 `Database:RuntimeRole`；新增的資料表不用另外授權（預設權限）。Cookie 的加密金鑰存在 `identity.data_protection_keys`，所有容器共用。
- 個人存取權杖（`identity.access_tokens`，`Authorization: Bearer pmt_…`）代表建立它的人，能做的事再受範圍限制（`items:read`、`items:write`、`blueprint:read`、`blueprint:write`）。每個需要登入的端點要用 `.RequireTokenScope(...)` 宣告；沒有宣告的端點一律不給權杖用（帳號管理這類只能在網頁上做）。
- 事件流：工作引擎每次寫入（建立、修改、改狀態、改類型、留言、附件、刪除、復原）在同一個交易裡寫一筆 `work.events`。讀的人（現在是通知模組，之後的 webhook、自動化）實作 `IWorkEventConsumer`，各自存讀到哪裡，依（交易編號、流水號）讀、只讀比還在進行的最舊交易更早的（`IWorkEventStream`）。新增寫入點時要一起寫事件；讀的人要容忍不認得的事件種類。還原到新的資料庫伺服器時交易編號會倒退，`WorkEventStream` 讀的時候自己把歷史歸一；讀的人讀出自己存的位置時一定先用 `IWorkEventStream.ResolvePositionAsync` 換算（見 `docs/OPERATIONS.md` 第 6 節）。API 的 `NotificationWorker` 每 2 秒把新事件交給讀的人。
- 跨專案的項目清單 `GET /v1/items`（「我的工作」的四種版面用）：條件和專案的清單同一套，另外收 `project`；範圍是看得到、沒有封存的專案。各專案的流程不同，跨專案的網頁只用狀態類別篩（選了一個專案才用個別狀態）；類型的選項由 `GET /v1/item-types` 依 key 合併。「我的工作」的檢視和專案的檢視同一張表，`project_id` 是 null，條件的意思不同（`internal/HANDOFF-my-work-views.md` 第 4.3 節）；「我的工作」沒有負責人的條件，永遠是我負責的，檢視的 `assignee` 一定是 null。
- 看得到哪些項目：角色有 `item.read` 是全部，只有 `item.read_own`（內建「執行者」）是自己負責或建立的。範圍的規則只有 `PmTool.Work/Access/ItemScope.cs` 一份（全部、只有自己的、沒有），所有讀項目的路（清單、計數、搜尋、單一項目、留言、附件、通知、收件匣）都用它；看不到的項目一律當作不存在（NotFound）。週期、里程碑的修改與變更集要看得到全部。
- 歷史匯入（`IHistoryImportService`，monday 匯入器用）：替別人署名、指定過去的時間，只給 `project.manage`；每一筆帶來源與外部編號（`work.history_refs`，組織裡唯一，記下誰匯的），重跑時略過；事件標 `History`，通知模組一則都不發、只建立負責人與 `Followers` 的關注；活動帶 `importedFrom`。外部編號已經在看不到的專案時是問題，不回傳那裡的編號。
- 專案說明（`work.project_descriptions`、`IProjectDescriptionService`）：專案的網址、部署環境、主機這類資料，一份 Markdown，不做結構化的欄位。網頁是專案的「資訊」頁（`/projects/:key/info`），CLI 是 `kumi project info`／`kumi project describe`。看得到專案就能讀（含執行者），`project.manage` 才能改，封存的專案也能改；私鑰一律擋下（`project.description_secret`），密碼靠說明格式的提醒，不要存進來。說明格式（開始寫、開新專案時填進去的架構）：內建的在 `PmTool.Work/Templates/Descriptions/`，組織自己的在 `work.description_templates`（只有組織管理員能改，網頁的「說明格式」頁；寫入在組織的 advisory lock 裡排隊），清單剛好一份預設。專案搜尋（`GET /v1/search/projects`、⌘K 的專案組、專案清單的搜尋框、`kumi project search`）比對代號、名稱與說明，範圍和專案清單同一份（`Access/VisibleProjects.cs`），說明比對到的帶一行摘錄（`internal/HANDOFF-project-info.md`）。
- 即時更新（`src/PmTool.Api/Live/`，交接 `internal/HANDOFF-live-updates.md`）：SignalR hub `/v1/live`，只能用 Cookie、檢查 `Origin`、一個人最多 20 條連線、不佔依人的同時請求數。每個 api 程序自己從 `IWorkEventStream.GetLatestPositionAsync` 起讀、每 500 毫秒一輪，推 `items`（`{ projectKey, itemKeys, actorId }`，超過 100 個編號是 null；`actorId` 是「這一則全是同一個人做的」時的那個人，網頁用它略過這個分頁自己剛改的）給 `project:{編號}`（看得到全部的）與 `own:{專案}:{人}`（只看自己的，只推負責人、建立者、前一個負責人是他的）；`inbox` 由 `INotificationObserver` 與標成已讀的端點推給 `user:{人}`。每 2 分鐘用不續命的查詢（`ISessionService.IsActiveAsync`）重新檢查工作階段與群組；這個程序裡的建立專案、設定或移除成員、改角色、停用人員之後立刻重算。每次送出各有 5 秒的逾時，一條不讀資料的連線拖不住別人。網頁只把對應的快取失效再重抓（`web/src/features/live/`），拖曳或寫入進行中先記著。事件流以外的東西（藍圖、成員、週期本身、專案說明）不推。
- 新通知的提示（`web/src/features/notification-toasts/`，交接 `internal/HANDOFF-notification-toasts.md`）：收到即時的 `inbox` 之後讀最新的未讀，只跳「編號沒看過、建立時間不早於已知最新的減 5 秒」的；開始與重連時先靜靜讀一次當基準。設定「完整／精簡／不顯示」記在瀏覽器（`notifications.toastMode`）。分頁看不見、在收件匣、項目正開著、確認類的對話框開著時不跳。提示層在最上面的 `aria-modal` 視窗裡（沒有時在 `body`），帶 `data-inert-exempt`。
- 說明的衝突檢查：單一項目的回應帶 `descriptionRevision`（說明的雜湊，`DescriptionRevisions`），PATCH 帶 `expectedDescriptionRevision` 時在鎖住項目的交易裡比對，不同回 409 `item.description_conflict`、什麼都不改；沒帶不檢查（CLI、匯入）。其他欄位照舊以欄位為單位、後寫的贏。
- 通知模組（`PmTool.Notifications`，schema `notifications`）：關注、通知、收件匣；只有專案成員收得到。提及的寫法是 `@[名稱](mention:<人員編號>)`。
- Email 通知（通知模組的 `Email/`，交接 `internal/HANDOFF-email-notifications.md`）：寄信設定一個組織一份（`notifications.email_settings`，只有組織管理員能改，網頁的「寄信設定」頁），Gmail（應用程式密碼）或自己填的 SMTP，連接埠只能 25、465、587、2525，密碼用 Data Protection 加密、API 不讀出來，沿用存的密碼時不能換寄信服務、主機、連接埠或帳號。API 的 `EmailWorker` 每 30 秒呼叫 `IEmailDispatcher`：通知 5 分鐘後還沒讀才寄、同一個人的併成一封、超過 24 小時不寄、看得到的規則和收件匣同一份（`NotificationVisibility`）、同時只有一個程序在寄（advisory lock）；組織層級的失敗退避 1～30 分鐘，單一收件人的問題只略過那個人。每個人在「設定 → 通知」可以關掉（`notifications.email_preferences`）。端點都只能用 Cookie。
- AI（`PmTool.Ai`，schema `ai`，交接 `internal/HANDOFF-ai-assistant.md`）：組織管理員在「AI 設定」頁接供應商（Anthropic、OpenAI、Google Gemini、OpenRouter、OpenAI 相容），金鑰用 Data Protection 加密、只回最後四碼，沿用存的金鑰時不能換供應商或網址；模型清單從供應商讀、管理員勾選開放的；思考深度每個模型各自能選（`AiEffortCatalog` 一份規則：Anthropic 讀 Models API 的 capabilities，OpenAI、Gemini 照代號）。統一用 Microsoft.Extensions.AI 的 `IChatClient`，Anthropic 的思考深度用 `RawRepresentationFactory` 送 `output_config.effort`（含 max），OpenAI 走 Chat Completions（Responses 是實驗性的 `OPENAI001`）。供應商的呼叫都在 `IAiProviderGateway` 後面（測試放假的），各家的網址明確寫死、不讀環境變數；相容端點在連線當下檢查位址，預設不連私有網段（`Ai:AllowPrivateEndpoints`）。專案的 AI 助理只讀藍圖、模板與統計數字（`IBlueprintUsageService`、`ITemplateLibrary`），能做的最多是提出變更集（`ChangeSetOrigin.Assistant`，網頁標「AI 助理提出」），權限等於發起的人；對話只有本人看得到。回覆是 SSE（每 15 秒心跳），一則對話同時只有一個回覆（`running_until` 的租約），不管怎麼結束都存下並放掉租約；存的是和供應商無關的 transcript，不存模型的思考（換模型時送不回去）。系統提示是 `Assistant/Prompts/system.md` 加上內嵌的兩份 Schema 與 `skills/pm-tool-planning/references/structure.md`：改了後兩份，助理的行為跟著變（映像檔要複製 `skills/` 的那一份，建置時檢查）。端點都只能用 Cookie。
- 浮動的 AI 助理與自己的 AI 帳號（交接 `internal/HANDOFF-ai-floating.md`）：每一頁右下角的「AI 助理」（`web/src/features/assistant-panel/`，掛在 `AppShell`）是同一個專案助理換外框，和整頁版共用元件與 API；串流在 `AppShell` 層，換頁、關掉面板不中斷。連線多一個擁有者（`ai.connections.owner_person_id`，null 是組織的；名稱的唯一索引 `NULLS NOT DISTINCT`）：組織的服務只碰擁有者是 null 的，`IPersonalAiConnectionService` 只碰自己的（寫入排在 `ai_personal:{org}:{person}` 的 advisory lock），共用 `AiConnectionStore`。組織管理員勾 `AllowPersonalConnections`（預設不允許）才能接、才選得到；組織沒開 AI 時允許了的人照樣能用自己的；別人的個人連線一律當作不存在。個人的相容端點一律檢查位址，`Ai:AllowPrivateEndpoints` 只放寬組織的。OpenRouter（`AiProviderKind.Openrouter`）走 OpenAI 的 SDK 加 `OpenRouterHandler`：帶 `HTTP-Referer`（`Security:PublicHost`）與 `X-OpenRouter-Title`，思考深度附在模型代號後面（`#effort=`）由 handler 改寫成 `reasoning: {effort}`（SDK 公開的 API 送不出任意欄位與 max），串流中途的 `{"error"}` 變成失敗。「用 OpenRouter 登入」（`IOpenRouterLoginService`）是 OpenRouter 官方的 PKCE，不用註冊應用：state 用 Data Protection 加密帶在網址上（人、組織、屬於誰、verifier、10 分鐘到期），回呼是網頁的 `/ai/openrouter/callback`，換到的金鑰照一般金鑰存。端點都只能用 Cookie。
- 本機代理（交接 `internal/HANDOFF-local-agent.md`，`ILocalAgentService`、`ILocalAgentDirectory`）：成員電腦上的 `kumi agent start`（`src/PmTool.Cli/Agent/`）用權杖（範圍 `agent:run`，OAuth 不發、只收權杖不收 Cookie）長輪詢 `/v1/agent/check-in`，伺服器不連進成員的電腦。每台電腦的每個引擎（`AiProviderKind.ClaudeCode`／`Codex`，不能存成連線）是能選清單裡的一個連線（`ai.local_engines` 的編號），60 秒內登記過才列；管控沿用 `AllowPersonalConnections`。送出時 `AssistantReplyRunner` 改走轉送：建一件工作（`ai.local_agent_jobs`，系統提示＋整段對話排成文字，不靠引擎的工作階段），讀 `ai.local_agent_events` 轉成 SSE，用同一套存訊息；20 秒沒被領、領走後 120 秒沒回報就失敗。本機跑引擎的隔離（只開 Kumi MCP 的七個藍圖工具、關掉內建工具、不載入使用者的 hooks 與技能、MCP 網址要和登入的伺服器同源、權杖只經環境變數）照交接第 5 節，旗標是在真的 CLI 上驗過的，改之前先重驗。一件工作只能由那個人自己在網頁上的訊息觸發（OpenAI 的 Sign in with ChatGPT 條款）。
- 設定精靈與瀏覽器登入（交接 `internal/HANDOFF-setup-wizard.md`，`ICliLoginService`）：「命令列與整合」最上方的步驟卡給一行指令（安裝、PATH、`kumi setup`），`kumi setup` 先問完要設定哪些，再一次開瀏覽器到網頁的 `/cli/authorize`（只能用 Cookie）按「允許」：kumi 在 127.0.0.1 開暫時的連接埠、PKCE S256、一次性的碼（2 分鐘），`POST /v1/cli-login/token` 不用登入、換到**一般的個人存取權杖**（多記 `machine_name`，在「存取權杖」撤銷得掉），一次最多兩把：命令列的（範圍使用者勾）與本機代理的（固定 `agent:run`＋讀寫設定＋讀項目，存在設定檔的 `agentToken`，引擎拿得到它所以不能改項目）。導回網址由伺服器組成 `http://127.0.0.1:{port}/kumi-login`，不收別的主機。重新登入時 kumi 附上舊的權杖、伺服器撤銷同一個帳號的；舊版存在 `token` 的本機代理權杖搬去 `agentToken`、不撤銷。`/v1/session` 用權杖呼叫時帶 `scopes`。精靈替 Claude Code 跑 `claude mcp add --scope user` 加 `claude mcp login`，替 Codex 跑 `codex mcp add`（遇到 OAuth 會馬上登入，所以繼承終端機跑、看 `auth_status`）。本機代理的開機自動啟動由精靈問：`kumi agent enable`／`disable`／`status`，macOS 是 `~/Library/LaunchAgents/kumi.local-agent.plist`（不帶任何權杖），Windows 是 HKCU 的 Run 值 `KumiLocalAgent`（**沒有在 Windows 上實測過**）；`agent.lock` 讓一台電腦只跑一個本機代理。
- 主機 AI 代理（交接 `internal/HANDOFF-host-agent.md`，操作 `docs/OPERATIONS.md` 第 17 節；`IHostAgentAdminService`、`IHostAgentWorkerService`、`IHostToolGrants`）：Claude Code／Codex 常駐在 Kumi 主機的 `ai-worker` 容器（`deploy/ai-worker/`、compose 的 `ai-worker` profile、`kumi host run`），**只有組織擁有者**（每次問身分模組，不看 `IsOrganizationAdmin`）能新增、配對、登入、管理來源；組織的有效成員（擁有者、管理員、一般成員，訪客不行）都能在助理選「主機上的 …」，和 `AllowPersonalConnections`、組織 API 的開關無關。三種身分分開：供應商的登入（CLI 自己的官方流程，在主機的終端機完成，存在資料卷）、機器憑證 `kha_`（只能用在 `/v1/host-agent/*`）、每件工作的工具授權 `kjt_`（只能用在 `/mcp`，解析出來的 Actor 是發問的人、每次重查，工作結束或來源關掉就失效）；兩種都不進 `AccessTokenHandler`、不變成 ClaimsPrincipal，失敗各有自己的額度桶。**只接受 API 用量計費的登入**（Claude Code 的 Console、Codex 的 API 金鑰）：訂閱帳號依兩家的條款不能共用，完成登入時拒絕並登出。佇列：每個來源同時 1 件、每人等 1 件、先排先做，排隊 3 分鐘與執行 5 分鐘分開計；送出時在來源的 advisory lock 裡原子預留配額；領走後 60 秒沒回報是「結果不明」（一次都沒回報是「沒有開始執行」、退還用量），不自動重跑；提出類的工具同一件工作同樣參數只建立一次（`host_tool_calls`）。工作內容（整段對話）結束就清掉，用量（`host_usage`）只有則數與 token。worker 的 MCP 網址是它回報的 `serverUrl` 加 `/mcp`（同源檢查照舊）。
- AI 助理選項目（交接 `internal/HANDOFF-ai-items.md`，`IItemProposalService`、`IItemProposalCreator`）：訊息帶選的項目（`AssistantMessageInput.ItemKey`，網頁自動跟著項目頁或 `?item=`），助理用 `get_item`、`search_items`（只限對話的專案、照 ItemScope）讀，用 `propose_item_changes` 建立修改建議（`ai.item_proposals`）——**助理一律不直接寫項目**，使用者在卡片上按「套用」才以自己的身分照工作引擎的權限寫入（欄位 → 狀態 → 留言；說明用模型讀到的 `descriptionRevision` 檢查衝突；狀態失敗就不留言）。本機代理經 MCP 用同名的工具；權杖有 `agent:run` 時 `/mcp` 一定要帶 `X-Kumi-Project` 標頭、所有工具只准碰那個專案（`McpProjectScope`，模型改不了標頭），`propose_item_changes` 的範圍是 `agent:run`。項目的標題不放進 system 訊息（別人寫的內容）。
- kumi 的下載與更新（交接 `internal/HANDOFF-cli-downloads.md`）：映像檔內含三個平台的單一執行檔（`/app/cli/`，`Cli:PackagesPath`；檔名 `kumi-<版本>-<平台>[.exe]` 是合約，伺服器照檔名認版本與平台）。`GET /v1/cli` 列出版本、大小與 SHA-256，`GET /v1/cli/download/{platform}` 下載，Cookie 與任何一把權杖都能用。網頁「設定 → 命令列與整合」的「命令列工具」給下載與照平台的安裝指令；`kumi update` 只更新從伺服器下載的單一執行檔（建置時的 `KumiStandalone`），核對大小與雜湊、試跑 `--version` 才換掉自己（Unix 用同資料夾的 rename，Windows 先把自己改名成 `.old`）。不做 brew：formula 只能從公開網址下載，而這裡的下載要登入。
- MCP 與 OAuth（交接 `internal/HANDOFF-mcp.md`，給使用者的說明 `docs/MCP.md`）：`/mcp` 是 MCP 伺服器（官方 C# SDK、2026-07-28 版規格、無狀態），使用者電腦上的 Claude Code／Codex 用自己的訂閱、以 pm-tool 帳號登入授權後連過來（Anthropic 禁止第三方提供 Claude.ai 訂閱登入，這是合規的做法）。pm-tool 自己當 OAuth 2.1 的授權伺服器（身分模組的 `OAuth/`，`IOAuthService`）：授權碼加 PKCE（只收 S256）、`resource` 綁 `/mcp`、Client ID Metadata Document 與動態註冊、更新權杖每次輪替、重放時撤銷；issuer 與 MCP 網址只有 `OAuthServerResolver` 一份算法（設了 `Security:PublicHost` 就用它）。發出去的存取權杖就是個人存取權杖（`access_tokens.grant_id` 有值），**只能用在 `/mcp`**，個人存取權杖的清單與上限不算它們。`/mcp` 只收 Bearer、不收 Cookie、檢查 Origin，16 個工具各宣告四個範圍之一，權限等於授權的人；沒有套用變更集的工具，提出的標 `ChangeSetOrigin.Agent`（網頁「AI 代理提出」）。抓 Client ID Metadata Document 與 AI 相容端點都用共用的 `src/Shared/PmTool.Http/PublicAddressGuard`（連線當下檢查位址）。同意頁是網頁的 `/oauth/authorize`，「設定 → 命令列與整合」可以撤銷授權並複製連線指令。
- 說明、留言、專案說明、說明格式的輸入是所見即所得的編輯器（`web/src/features/rich-text/`，Tiptap），**存的還是 Markdown**：讀寫在 `features/rich-text/markdown/`（用和顯示同一套的 remark，跳脫只加在會被誤讀的地方；打開不改交出去的是原文）。新增文件節點時讀、寫、編輯器的 schema 三處一起改，`EditorRoundTrip.test.tsx` 會抓到漏的。
- 留言的回覆只有一層（`item_activities.reply_to_id` 一律指到最上層的留言）。活動清單可以用 `kind` 只取一種（`ActivityQuery.Kind`），在資料庫裡篩掉再分頁；網頁時間軸的「留言」用 `commented`，兩份各一份快取，讓活動失效時用前綴 `queryKeys.activity(項目)`。說明與留言裡的附件寫成 `![檔名](attachment:<附件編號>)`（圖片）或 `[檔名](attachment:<附件編號>)`；Markdown 只對 `attachment:` 載入圖片，外部網址的圖片照舊畫成連結（內容安全政策只允許同網域）。
- 留言的編輯、刪除與復原（交接 `internal/HANDOFF-comment-edit.md`）：只有作者能編輯，每次編輯把舊內容存成一個版本（`work.comment_revisions` 只存舊版本，最多 100 版），`at` 與時間軸位置不變、項目的 `updatedAt` 不動，只為這一版新加的提及發通知、既有通知的摘要換成新的。刪除是藏起來（`deleted_at`／`deleted_by`），留在原位顯示「已刪除」，內容與版本**只在 `ActivityRecords.ToActivity` 一處藏**（所有讀的路都經過它）；刪除時這則留言的通知一起刪掉，復原不找回、不重發。作者或 `project.manage` 能刪；作者自己刪的作者或 `project.manage` 能復原，管理者刪的只有 `project.manage` 能復原；封存的專案一律不行（`project.manage` 在封存的專案還在，要自己擋 `IsArchived`）。刪除的留言復原之前不能編輯、回覆、看版本（409 `comment.deleted`）；刪除與復原重送不會失敗。目前沒有永久刪除：貼錯的密碼清不掉。
- 組織時區：「今天」「這個月」一律依組織的時區算，不依伺服器（UTC）也不依瀏覽器。讀的人用 `IOrganizationClock`（`PmTool.Abstractions`，身分模組實作，存在 `identity.organizations.time_zone`，預設 `Asia/Taipei`）與 `OrganizationTime.Today`。
- 自訂欄位的篩選與排序只限專案的項目清單（`FieldFilter`、`WorkItemSortField.Field`）：jsonb 包含查詢，值一律當參數；網頁、存的檢視送出前用 `knownFieldFilters` 去掉藍圖裡已經沒有或格式不對的條件，不讓舊網址整張被 422 擋掉。
- 工作階段存在身分模組（`identity.sessions`，只存權杖的雜湊）。Cookie 裡帶權杖，API 每個請求都問 `ISessionService.FindAsync`（模組裡快取 30 秒，同一個程序作廢的立刻清掉）。
- `web/` 是網頁版（React、TypeScript、Vite），建置出來的靜態檔由 API 主機一起提供。
- 部署以容器為主。

## 規矩

- 回覆使用者一律用繁體中文（台灣用語），包括等待背景工作或子代理時的進度回報、排程喚醒之後的回報與最後的總結；程式碼的識別字、指令、檔名照原文。給子代理的提示也用中文寫。
- 先寫測試再寫實作。測試用 NUnit 的 `Assert.That`，名稱描述行為，結構是 Arrange／Act／Assert。
- 不可變：模型一律用 record 與 init 屬性，不修改既有物件；對外的集合用 `IReadOnlyList<T>`。
- 檔案以 200～400 行為宜，上限 800；函式 50 行以內；巢狀不超過 4 層。
- 驗證問題一律用 `IssueCodes` 的常數。代碼是對外合約，不能改名。訊息用繁體中文。
- JSON Schema 只能用這幾個關鍵字：`type`、`properties`、`required`、`additionalProperties: false`、`items`、`enum`、`const`、`$ref`。要加別的，先停下來問。理由：同一份 Schema 要能直接拿去約束語言模型的輸出。
- 警告視為錯誤。不要用 `#pragma warning disable` 或 `NoWarn` 把警告關掉。
- 授權：Kumi 以 Kumi License（`LICENSE`：Apache 2.0 加附加條件）公開原始碼，另外賣商業授權，不是 OSI 定義的開源，對外不要寫「開源」。`LICENSE` 的第二部分是 Apache 2.0 的原文，一個字都不能動；第一部分的條件要改，先停下來問。
- 套件版本集中在 `Directory.Packages.props`。要加套件，先確認授權是寬鬆的（MIT、Apache-2.0、BSD、ISC 這一類；GPL、AGPL、LGPL 與限制商業使用的不收），在那裡用註解寫出來。加了、升了、拿掉套件（NuGet 或 npm）之後重新產生 `THIRD-PARTY-NOTICES.txt`；套件自己沒附授權檔時腳本會失敗，把條文補進 `scripts/licenses/texts/`。
- 不用這些套件：JsonSchema.Net（NuGet 套件帶維護費條款）、MassTransit v9、MediatR、AutoMapper、FluentAssertions v8、Duende IdentityServer。
- 註解只寫「為什麼這樣做」，用繁體中文。
- 身分、工作引擎的測試與 API 的端到端測試對真的 PostgreSQL（Testcontainers），透過公開介面觀察行為，不去查資料表來驗證。
- API 的測試類別會同時跑（`tests/PmTool.Api.Tests/TestParallelism.cs`，同一個類別裡照舊一個一個跑）：每個測試自己開 `ApiFactory`，不要讓類別之間共用狀態；動到整個程序共用的東西（`Console`、環境變數、計時器的數量）的類別標 `[NonParallelizable]`。`RealStack/` 那一組共用一個 PostgreSQL，事件流看的是整個伺服器的交易，整組一個一個跑（標在 `RealStackDatabase`）。關掉的主機要能被回收（`ApiFactoryLeakTests` 盯著）：一輪開兩千多個主機，沒停的計時器或背景迴圈會抓著整個主機，記憶體越積越多、後面的測試慢好幾倍（2026-11-24 限流器的計時器就讓整套從 17 分鐘拖到 51 分鐘）；新增這類東西時要在主機關閉時停掉。
- EF 產生的遷移檔帶 BOM 與 `#pragma warning disable`，提交前拿掉。
- 會進資料庫的字串輸入都要擋 NUL 與控制字元：PostgreSQL 不收，沒擋就是 500。
- 合約加了屬性，模組、API 的 JSON 對應、API 的形狀測試、網頁的型別與 MSW 假資料要一起改；只改其中幾層，單元測試全過、整合起來卻會壞。
- 畫面（`web/`）照 `docs/DESIGN.md` 的設計規範做：新增或修改畫面前先讀，做完照它第 14 節的清單檢查（版面的數字用 `scripts/ui-audit.js` 量）。要偏離或擴充，在同一個提交裡改規範並寫出理由。規範分成五份、節號共用：設計變數（第 2 節）、元件（第 7 節）、控制項（7.3）、流程與互動（第 8 節）在 `docs/design/`，其餘在 `docs/DESIGN.md`，它的第 0 節有對照表。

## Git

- 提交訊息：`<type>: <繁體中文說明>`。type 用 feat、fix、refactor、docs、test、chore、perf、ci。
- 作者用 repo 層級已經設定好的身分，不要改 git 設定。
- 程式碼、測試資料、提交訊息都不能出現真實的個人資料或任何公司的內部資訊。範例一律用 `example` 之類的假名。
- 這個 repo 是公開的，推上去就收不回來：導入的公司與它的結構、客戶、試用環境、使用的人數與資料量、還沒修好的安全性問題，只寫在 `internal/`，不寫進這裡的程式、註解、文件與提交訊息。
- 每完成一個可以驗證的段落，就提交並推送；`internal/` 有修改時那邊也要提交並推送。
