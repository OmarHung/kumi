# 命令列工具 kumi

`kumi` 讓你在終端機、腳本與 CI 裡用 Kumi：看與改項目，匯出、預覽、套用專案的藍圖。它呼叫的就是網頁用的那一套 API，權限也一樣：你在網頁上做不到的事，`kumi` 也做不到。

## 安裝

### 從網頁下載（macOS、Windows）

登入網頁，開「設定 → 命令列與整合」的「命令列工具」，選平台（macOS 的 Apple 晶片或 Intel、Windows），下載執行檔，再複製畫面上的指令貼到終端機。不用另外裝 .NET。指令做的事：

- **macOS**：把下載的檔案搬到 `~/.local/bin/kumi`、設成可以執行，並用 `xattr -c` 清掉瀏覽器加的隔離標記（沒清的話 macOS 會直接擋下來，連警告都沒有）。第一次裝還要把 `~/.local/bin` 加進 `~/.zprofile` 的 PATH，再開一個新的終端機視窗。
- **Windows**（PowerShell）：把下載的檔案搬到 `%LOCALAPPDATA%\Programs\Kumi\kumi.exe`、`Unblock-File`。第一次裝還要把那個資料夾加進使用者的 PATH，再開一個新的 PowerShell 視窗。Windows 的 ARM 電腦用 x64 的版本。
- Windows 的指令向系統問下載資料夾在哪裡（被 OneDrive 搬走也找得到）；macOS 用 `~/Downloads`。瀏覽器存到別的資料夾的話，把指令裡的路徑換掉。
- 下載資料夾裡已經有同名的檔案時，瀏覽器會在檔名後面加上 (1)，指令搬到的會是舊的那一個：先刪掉舊的再下載一次。
- 網頁只列檔名合乎規則（`kumi-<版本>-<平台>[.exe]`）的平台：檔名會原樣放進要貼到終端機的指令，不盲信伺服器回的內容。

執行檔沒有 Apple 的公證，也沒有 Windows 的程式碼簽章：Chrome 下載 `.exe` 時可能提醒「不常下載」；開著 Smart App Control 的 Windows 可能擋下來，這時請改用下面的 dotnet tool。

**更新**：`kumi update`（見下面的「更新」）。

### 用 dotnet tool 安裝（Linux、CI、開發 Kumi 的人）

需要 .NET 10 SDK。在 repo 根目錄：

```bash
dotnet pack src/PmTool.Cli -c Release -o ./nupkg
dotnet tool install --global PmTool.Cli --add-source ./nupkg
kumi --version
```

更新時把 `install` 換成 `update`（`kumi update` 只更新從網頁下載的執行檔）。移除：先 `kumi uninstall --skip executable`（撤銷權杖、拿掉 MCP 與 skill、刪設定，見下面的「移除」），再 `dotnet tool uninstall --global PmTool.Cli`。

不想裝到全域，可以裝到一個資料夾：`dotnet tool install PmTool.Cli --tool-path ~/tools/kumi --add-source ./nupkg`，執行 `~/tools/kumi/kumi`。

### 更新

```bash
kumi update            # 有新版就下載、換掉自己
kumi update --check    # 只看有沒有新版
```

- 向登入的伺服器問現在提供哪一版（`GET /v1/cli`），比這份新才下載。用的是登入時存的權杖，範圍不拘。
- 下載到 kumi 所在的資料夾裡的暫存檔，核對大小與 SHA-256，再執行一次它的 `--version` 確認跑得起來、版本對，才換掉原本的檔案；任何一步不對都不換，暫存檔也會刪掉。macOS 上 `kumi` 是符號連結時，換掉的是它指到的檔案。
- Windows 上執行中的 `kumi.exe` 不能覆蓋：舊的改名成 `kumi.exe.old`，下一次 `kumi update` 時刪掉。更新中途被強制結束、找不到 `kumi.exe` 時，把同一個資料夾裡的 `kumi.exe.old` 改回 `kumi.exe`。
- 伺服器是遠端的 http（不是 https、也不是 localhost、127.0.0.1、::1）時不更新，結束代碼 1，就算加了 `--insecure` 或 `KUMI_ALLOW_INSECURE=1`：路上的人可以同時換掉檔案與它的 SHA-256。`--check` 照常可以用。
- 用 dotnet tool 裝的不能用 `kumi update` 更新（結束代碼 1，說明會寫怎麼更新）；`--check` 照樣能看有沒有新版。
- kumi 放在自己沒有寫入權限的資料夾時（例如 `/usr/local/bin`）會失敗並說明；請放在 `~/.local/bin` 這類自己的資料夾。
- 換好之後，如果這台電腦的**使用者層**（`~/.claude`、`~/.codex`）裝過 skill（`kumi skill install`，有標記的），會用**新的**執行檔跑一次 `kumi skill update`（舊的程序內嵌的是舊版 skill），把印出的結果接在後面；用 `--project` 裝在專案資料夾裡的不會動，要自己執行 `kumi skill update --project <資料夾>`。改過的資料夾照樣跳過並警告；失敗只警告（附上新執行檔印的錯誤，建議執行 `kumi skill update`），不讓更新失敗。
- 換好之後（0.13.0 起），本機代理正在跑的話：開機自動啟動的背景程序（macOS）用 `launchctl kickstart -k` 照啟動項目重開一次，等到新的程序拿到鎖才算數（印「背景的本機代理已經用新版重新啟動。」）；在跑的程序記憶體裡還是舊版，向伺服器登記的版本也是舊的，不重開的話網頁會一直說要更新。在終端機視窗裡的 `kumi agent start` 只提醒你到那個視窗按 Ctrl-C 再開；Windows 的背景程序還不會自動重開，提醒你登出再登入；沒在跑的不替你開。重開失敗只警告（建議 `kumi agent status`、`kumi agent restart`），不讓更新失敗。0.12.0 以前的 `kumi update` 不會重開，更新完請自己執行一次 `kumi agent restart`。
- `--json`：`{ "current", "latest", "updateAvailable", "updated", "standalone", "skillsUpdated", "agentRestart" }`（伺服器沒有提供下載時 `latest` 是 `null`；`skillsUpdated` 是這次有沒有順便更新 skill；`agentRestart` 是本機代理怎麼了：`restarted`、`not_running`、`foreground`、`unsupported`、`failed`，沒有換檔案時是 `null`）。

**從舊版的 `pm` 升級**（2026-11-23 產品定名 Kumi，指令從 `pm` 改成 `kumi`）：套件名稱還是 `PmTool.Cli`，照上面 `update` 一次，`pm` 就換成 `kumi`。不用重新登入：設定檔還在舊的 `~/.config/pm-tool/` 時照樣讀；環境變數的舊名 `PM_SERVER`、`PM_TOKEN`、`PM_ALLOW_INSECURE` 也照樣認得，新舊都有時用 `KUMI_*`。

### 移除（kumi uninstall，0.12.0 起）

```bash
kumi uninstall                          # 一步一步問，每一步都可以說不要
kumi uninstall --yes                    # 不問，每一步照預設做
kumi uninstall --yes --skip mcp --skip executable   # 照預設做，但不動 MCP 與執行檔
```

照下面的順序一步一步做，**每一步做之前先問**（`[Y/n]` 直接按 Enter 是預設）；這台電腦上沒有的步驟不問，前一步失敗不擋後一步：

1. **背景的本機代理**（`agent`）：有設定開機自動啟動時，停掉它並取消（等於 `kumi agent disable`）。
2. **伺服器上的權杖**（`tokens`）：用每一把權杖撤銷它自己（命令列的與本機代理的，和 `kumi auth logout --revoke` 一樣），撤銷了的才刪掉本機存的；伺服器回 401（權杖早就過期或被撤銷）算是已經不能用，一樣刪。撤銷失敗（連不上、伺服器出錯）的那一把本機留著，可以再執行一次，或到網頁「設定 → 命令列與整合」的「存取權杖」撤銷。
3. **Claude Code／Codex 裡的 kumi MCP**（`mcp`）：每個找得到的引擎各問一次（`claude mcp remove kumi --scope user`、`codex mcp remove kumi`）。指到這台電腦登入的伺服器的預設移除；指到別的伺服器的預設不移除（`--yes` 時留著）。Claude Code 裡不在使用者層（專案或本機層，不是 `kumi setup` 加的）的不動，只說明怎麼自己移除。
4. **skill**（`skills`）：移除使用者層 kumi 裝的 skill（等於 `kumi skill uninstall`）；自己放的、改過的不動。用 `--project` 裝在專案資料夾的要在那裡執行 `kumi skill uninstall --project <資料夾>`。
5. **設定與記錄**（`files`）：設定資料夾（`~/.config/kumi`，舊的 `~/.config/pm-tool` 也一起；設定檔被指到不叫 `kumi` 的資料夾時，只刪 kumi 自己的那幾個檔案；設定資料夾是連結時只刪連結，並說明它指到的地方沒動）、背景代理的記錄檔（macOS 的 `~/Library/Logs/Kumi`、Windows 的 `%LOCALAPPDATA%\Kumi`）、本機代理留下的暫存工作資料夾（15 分鐘內用過的不刪；權限不是 700 或是連結的不刪，這一步會失敗並說明）。**權杖還沒撤銷時預設改成不刪**：刪掉之後就只能到網頁撤銷。本機代理正在執行（前景的 `kumi agent start`）時這一步失敗，請先結束它。
6. **執行檔**（`executable`）：macOS 刪掉 kumi 自己（只刪檔名是 `kumi` 開頭的）。Windows 刪不掉正在執行的 `kumi.exe`，會說明要自己刪的路徑與要從 PATH 拿掉的資料夾（`kumi update` 留下的 `kumi.exe.old` 問過之後刪）。用 dotnet tool 裝的不刪，會說明要執行 `dotnet tool uninstall`。

最後列出 kumi 做不到、要自己處理的：Claude Code／Codex 的 MCP 授權（OAuth）要到網頁「設定 → 命令列與整合」撤銷；安裝時加進 shell 的 PATH（例如 `~/.zprofile` 裡的 `~/.local/bin`）；還設定著的 `KUMI_TOKEN`。用 `CLAUDE_CONFIG_DIR` 開了另一份 Claude Code 設定的，那一份的 MCP 與 skill 要在那個環境下再執行一次。

- `--skip` 的值：`agent`、`tokens`、`mcp`、`skills`、`files`、`executable`，可以重複或用逗號隔開；不認得的是用法錯誤（代碼 2）。
- 沒有終端機或加了 `--json` 時一定要加 `--yes`（不然代碼 2、什麼都不做）。
- 結束代碼：有一步失敗是 1，其他（包括說不要的、要自己處理的）是 0。
- `--json`：`{ "steps": [{ "step", "status", "messages", "engine"? }], "reminders": [...] }`。`status` 是 `done`、`declined`（說不要，或 `--yes` 時預設是不要）、`skipped`（`--skip`）、`not_found`（這台電腦上沒有）、`manual`（要自己處理）、`failed`；`mcp` 每個有 kumi MCP 的引擎各一筆，帶 `engine`（`claude_code`、`codex`）。

### 授權與第三方聲明

```
kumi licenses            # Kumi 的授權（LICENSE、NOTICE）與用到的第三方套件的授權
kumi licenses | less     # 內容很長，接分頁程式或導到檔案
```

這些文件內嵌在執行檔裡，不用登入、也不連線。從伺服器下載的單一執行檔只有一個檔案，授權就靠這個指令帶著走。

## 登入

`kumi` 用「存取權杖」登入，不用密碼。**在終端機裡預設是開瀏覽器按「允許」，不必到網頁建權杖、複製貼上。**

```bash
kumi auth login --server https://pm.example.com
# 正在開瀏覽器，請在 Kumi 的頁面按「允許」。瀏覽器沒有打開的話，把這個網址貼到瀏覽器：…
# 確認碼：951 123
# 在瀏覽器的 Kumi 頁面輸入這組確認碼再按「允許」。頁面上不是你在這台電腦剛執行的登入，請按「拒絕」。
```

流程（照 RFC 8252 的原生程式作法）：`kumi` 在這台電腦的 `127.0.0.1` 開一個暫時的連接埠、打開瀏覽器到 Kumi 的同意頁；你按「允許」後，瀏覽器把一次性的碼帶回這個連接埠，`kumi` 用 PKCE 的 verifier 換成權杖並存進設定檔。kumi 讀瀏覽器請求標頭只給 3 秒，沒送完就關掉那條連線（同一台電腦上的其他程式不能靠佔住連線擠掉瀏覽器）。伺服器只會把碼送到 `127.0.0.1`（連接埠是 `kumi` 自己報的，網址由伺服器組成）。發出來的就是一般的存取權杖，名稱是「kumi（電腦名稱）」，在網頁的「存取權杖」清單看得到、撤銷得掉。重新登入會換掉這台電腦上同一個伺服器、同一個帳號的舊權杖，清單不會越積越多。

**確認碼**：`kumi` 由這次登入的 PKCE challenge 算出六碼確認碼（`SHA-256("kumi-cli-login-confirm\n" ‖ challenge)` 的前 4 個位元組當大端的無號整數，除以 1,000,000 取餘數、補零到 6 位），印在終端機（寫到 stderr，`--json` 的 stdout 不受影響），同意頁要你輸入同一組才能按「允許」。確認碼只看自己的終端機；別人告訴你的確認碼（信件、訊息、電話，或頁面上別的數字，包括電腦名稱）一律不要輸入，頁面上不是你在這台電腦剛執行的登入，請按「拒絕」。登入成功之後，瀏覽器會停在不帶登入碼的頁面（`kumi` 收到碼就把瀏覽器導到沒有查詢字串的網址），登入碼不會留在網址列與瀏覽紀錄裡。`auth login`、`agent login`、`setup`、`--no-browser` 都一樣。0.8.0 以前的 `kumi` 不會印確認碼，同意頁會請你先更新：`kumi update`（需要還有效的權杖）；權杖已經失效時，到網頁「設定 → 命令列與整合」下載新版。

按「允許」的瀏覽器與執行 `kumi` 的電腦，要從同一個網路位址連到伺服器：一次性的碼只換給當初核准它的那個位址，碼的有效時間是 1 分鐘、只能換一次。下面幾種情形兩邊的位址會不同，登入會失敗、要重新執行，這時改用 `--paste`：其中一邊走 VPN 或代理（兩邊出口位址不同）、瀏覽器開了會換出口的功能（例如 Safari 的 iCloud 私密轉送）、只有瀏覽器走公司的代理。登入發的權杖最長 365 天（同意頁選的天數，一定有期限）；本機代理的那一把固定 90 天。

| 選項 | 作用 |
|---|---|
| `--server 網址` | 伺服器（沒給時用 `KUMI_SERVER`、設定檔，都沒有就問） |
| `--scope 範圍` | 權杖要的範圍，可以重複、也可以用逗號放好幾個（預設 `items:read`、`items:write`、`blueprint:read`）。同意頁只預先勾讀取的範圍，寫入的（`items:write`、`blueprint:write`）要自己勾。`agent:run` 不能用這個要，見下面的 `kumi agent login` |
| `--no-browser` | 不自己開瀏覽器，只印網址；迴路照樣在這台電腦上等，所以要在**這台電腦**的瀏覽器開 |
| `--paste` | 改成在終端機貼上在網頁建立的存取權杖（輸入時不會顯示）。**透過 SSH 登入遠端機器時用這個**：瀏覽器在你面前的電腦，迴路卻開在遠端機器上，導不回去 |
| `--with-token` | 從標準輸入讀權杖，不互動，給腳本用 |

沒有終端機又沒給 `--paste`、`--with-token` 時是用法錯誤（結束代碼 2）。`--scope`、`--no-browser` 只有瀏覽器登入用，不能和 `--paste`、`--with-token` 一起用。你在瀏覽器按「拒絕」、或等了 5 分鐘沒有回應：結束代碼 1，什麼都不存。`--json` 印 `{ "server", "user": { "displayName", "email", "organizationName" }, "cliToken", "agentToken", "agentSkipped" }`（不含權杖明文；`--paste`、`--with-token` 印的是伺服器回的 session）。

不想互動（腳本、CI）時，從標準輸入讀，或直接用環境變數 `KUMI_TOKEN`：

```bash
printf '%s' "$MY_TOKEN" | kumi auth login --server https://pm.example.com --with-token
```

### 本機代理的權杖（kumi agent login）

設定檔存**兩把**權杖：命令列的（`token`）與本機代理的（`agentToken`）。分開存是因為本機代理的引擎拿得到它自己的權杖，不能讓它改項目；也因為登入本機代理不會再蓋掉命令列原本的那一把。

```bash
kumi agent login [--server 網址] [--no-browser] [--paste] [--with-token]
```

流程和 `kumi auth login` 一樣，但要的是本機代理的權杖（範圍固定是 `agent:run`、`blueprint:read`、`blueprint:write`、`items:read`），存進 `agentToken`，不動命令列的那一把。組織沒有開放成員使用自己的 AI 帳號時，同意頁會說明、`kumi` 說「沒有建立本機代理的權杖」並結束（代碼 1）。`--paste`、`--with-token` 貼的是網頁「AI 帳號」建立的本機代理權杖，`kumi` 會先確認它的範圍恰好是上面那一組：沒有 `agent:run`，或多了（例如 `items:write`，引擎拿得到這把權杖就改得動項目）、少了，都不存、說明原因；瀏覽器登入時伺服器發下來的範圍不對也一樣不存。`kumi agent start` 與 `kumi agent enable` 依序用 `KUMI_TOKEN`、`agentToken`、`token`（舊設定檔只有一把時照樣能用）。

本機代理的權杖只能用在領工作與連 MCP：REST API 上只能打 `/v1/agent/*`、`GET /v1/session`、`GET /v1/cli*`，其他一律 403；MCP 上只能用本機代理的十四個工具。建立權杖時 `agent:run` 不能和 `items:write` 放在同一把。以前把同一把權杖同時拿來跑 `kumi` 的一般指令與本機代理的話，一般指令會變成 403，重新執行 `kumi setup`（或 `kumi auth login` 加 `kumi agent login`）分成兩把。

```bash
kumi auth status    # 伺服器、登入身分、權杖從哪裡來、有沒有存本機代理的權杖
kumi auth logout    # 刪掉這台電腦上存的兩把權杖；有終端機時會問要不要一併撤銷
kumi auth logout --revoke   # 不問：一併撤銷伺服器上的權杖，並停掉背景的本機代理
```

`logout` 有終端機時會問「要一併撤銷伺服器上的權杖（命令列與本機代理的），並停掉背景的本機代理嗎」。答是，或加了 `--revoke`，會先停掉背景的本機代理（等於執行 `kumi agent disable`），再用每一把權杖呼叫 `DELETE /v1/account/tokens/current` 撤銷它自己，最後刪本機的檔案；撤銷失敗（連不上、權杖早就失效）會寫到標準錯誤說明，本機的檔案照樣刪。加了 `--json` 或沒有終端機又沒加 `--revoke` 時不問、**只刪本機的檔案，不撤銷**（要撤銷請加 `--revoke`，或到網頁「設定 → 命令列與整合」的「存取權杖」撤銷）。`--json` 的輸出多 `revoked`（`cliToken`、`agentToken`）、`revokeFailures`、`agentStopped`。

### 權杖的範圍

權杖能做的事，是「你的權限」與「權杖的範圍」兩者都允許的。

| 範圍 | 可以 |
|---|---|
| `items:read` | 看專案清單、項目、活動、我的工作、人員清單、搜尋項目、已刪除的項目、列出與下載附件 |
| `items:write` | 建立、修改、改狀態、留言、編輯、刪除與復原留言、刪除、復原、上傳附件 |
| `blueprint:read` | 匯出藍圖、預覽（`plan`）、看變更集 |
| `agent:run` | 只給本機代理（`kumi agent start`）：登記這台電腦、領網頁送來的回覆、回報進度。其他的 API 都不能用；OAuth 授權也不發這個範圍 |
| `blueprint:write` | 送出、套用、拒絕變更集（套用還是要你在專案裡有核准的權限）、專案改名、封存與取消封存、加入成員或改成員的角色（要你在專案裡有管理的權限） |

範圍不夠時，指令會結束代碼 1，並在訊息裡說明缺哪一個。帳號管理、人員管理、建立專案、移除成員只能在網頁上做。

### 設定檔與環境變數

- 設定檔：`$XDG_CONFIG_HOME/kumi/config.json`，沒設時是 `~/.config/kumi/config.json`（舊版登入過、新的還沒有時，照樣用舊的 `pm-tool/config.json`，之後也存回那裡）。裡面有權杖。Unix 上檔案權限是 600（只有你讀寫），資料夾 700；讀設定檔時發現別人（群組或其他使用者）讀得到，會收緊成 600（資料夾 700，只動名叫 `kumi` 或 `pm-tool` 的資料夾）並在標準錯誤警告，因為裡面的權杖在那之前可能被看過；**Windows 上 `kumi` 不另外設權限**，靠的是設定檔放在你的使用者資料夾底下（`%USERPROFILE%\.config`），其他一般帳號讀不到；共用的電腦請改用環境變數。
- 環境變數優先於設定檔：`KUMI_SERVER`（伺服器網址）、`KUMI_TOKEN`（權杖）。`--server` 又優先於 `KUMI_SERVER`。改名前的 `PM_SERVER`、`PM_TOKEN` 也認得，新舊都有時用新的；錯誤訊息會說是哪一個變數。
- `KUMI_TOKEN` 與設定檔裡的權杖，不是 `pmt_` 開頭、或中間有空白，`kumi` 在送出之前就報錯（結束代碼 2），不會把一段亂碼當成憑證送出去。
- 設定檔裡的權杖只會送給當初登入的那個伺服器。用 `--server` 指向別的伺服器時，要用 `KUMI_TOKEN` 另外給權杖。
- 伺服器網址不能帶帳號與密碼（`https://帳號:密碼@主機`），一律拒絕。
- **伺服器不是 `https` 時，`kumi` 預設拒絕**（結束代碼 2、什麼都不送），因為權杖會在網路上明文傳送。本機（`localhost`、`127.0.0.1`、`::1`）的 `http` 不受影響，也不警告。真的要用（例如公司內網的測試機），加 `--insecure`，或設環境變數 `KUMI_ALLOW_INSECURE=1`；這時每次執行都會在送出之前把警告寫到標準錯誤（加了 `--json` 也一樣，標準輸出仍是乾淨的 JSON）。
- `kumi` 不會自動跟著轉址：每個請求都帶著權杖，不該被帶去別的網址。伺服器回轉址時，`kumi` 說明它轉去哪裡（結束代碼 1），請改用那個網址。

## 設定精靈（kumi setup）

一次設定好：命令列登入、Claude Code／Codex 使用 Kumi（MCP）、給 AI 代理用的 skill、本機代理（含登入電腦時自動啟動）。網頁「設定 → 命令列與整合」最上方的步驟卡下載 `kumi` 之後，只要貼一行就會接著執行它。

```bash
kumi setup [--server 網址] [--no-browser]
```

必須在終端機裡（它會問問題、開瀏覽器）；不支援 `--json`。流程：

1. **偵測**：找 `claude`、`codex`（環境變數 `KUMI_AGENT_CLAUDE_PATH`、`KUMI_AGENT_CODEX_PATH` 優先，再看 PATH），各跑一次 `--version`。都沒有時只設定命令列，並附安裝網址，之後裝好再跑一次 `kumi setup`。
2. **問題全部先問完才動手**（Enter、`y`、`是` 是「是」；`n`、`否` 是「否」；其他重問）：安裝給 AI 代理用的 skill 到偵測到的 Claude Code／Codex（預設是；等於執行 `kumi skill install`，舊名稱的資料夾要移除時到那一步才問）？讓 Claude Code 使用 Kumi？讓 Codex 使用 Kumi？讓網頁的 AI 助理用這台電腦的引擎（本機代理）？（macOS、Windows 才問）登入電腦時自動在背景啟動？引擎裡已經有一個叫 `kumi` 的 MCP 伺服器、卻指到別的網址時，多問一題「要換成這個伺服器嗎？」（預設否）。
3. **登入**：設定檔裡已經有這個伺服器的權杖、伺服器認得、範圍也對（命令列的不能有 `agent:run`、本機代理的要恰好是固定的那一組）就不再登入；缺的才**一次**開瀏覽器，同一頁核准命令列與本機代理兩把。登入沒有成功（拒絕、逾時）不中止精靈：MCP 的授權是 Claude Code／Codex 自己的流程。
4. **Claude Code**：寫進引擎設定的 MCP 網址只收 `https`，或本機（`127.0.0.1`、`localhost`、`::1`）的 `http`，就算加了 `--insecure` 也一樣（引擎之後每一次都會帶著授權連這個網址，它寫在引擎的設定檔裡、不再經過 `kumi`）：不合格就不寫、說明原因，請改用 https 的網址或自己設定。`claude mcp add --scope user --transport http kumi <伺服器>/mcp`（user 範圍，所有資料夾都能用），再用繼承終端機的方式跑 `claude mcp login kumi`（瀏覽器開 Kumi 的授權頁，按「允許」）。**Codex**：`codex mcp add kumi --url <伺服器>/mcp`——Codex 遇到支援 OAuth 的伺服器會在 `add` 裡**馬上**請你授權（印出網址、開瀏覽器，沒有關掉的選項），所以它也是繼承終端機跑；跑完讀 `codex mcp list --json` 的 `auth_status`，還沒授權才補跑 `codex mcp login kumi`，已經授權過就略過。加了 `--no-browser` 時兩個 `mcp login` 都加上 `--no-browser`（只印網址）；Codex 的 `add` 沒有這個選項，照樣會試著開瀏覽器，但也會印出網址。每個指令執行前都先印出整行；網址只准英數字與 `. : / - [ ]`，不合就不執行。`mcp login` 沒完成不擋後面的步驟，之後在引擎裡輸入 `/mcp` 選 kumi 登入。
5. **本機代理**：問「登入電腦時自動在背景啟動本機代理？」**預設是「否」**（題目寫明它會在你沒看著的時候用你的 AI 訂閱執行網頁送來的對話，每小時最多 30 件、每天 200 件），要明講 y 才執行 `kumi agent enable --server …`；不要的話，總結印完後問「現在在這個視窗啟動本機代理嗎？」，是就執行 `kumi agent start`（視窗要一直開著）。組織沒有開放成員使用自己的 AI 帳號時這一步標「略過」。

最後印總結（✓ 完成、– 略過、✗ 失敗並寫怎麼補）。結束代碼：全部做了或略過是 0；任何一步失敗是 1；用法錯誤（沒有終端機、`--json`）是 2。網頁的「設定精靈」會依權杖、MCP 授權與本機代理的狀態即時打勾。

## 指令

每個指令都可以加：

- `--json`：不印給人看的表格，印伺服器回的資料（JSON，縮排，中文照寫），給腳本用。`--json` 一律視為非互動：不會問任何問題。
- `--insecure`：允許用 `http` 連到不是本機的伺服器（見上面）。
- `--server 網址`：這次改用另一個伺服器。
- `--help`：說明。`kumi --help` 列出全部指令。

### 專案

```bash
kumi project list
kumi project rename WEB --name "官網改版（第二期）"
kumi project archive WEB
kumi project unarchive WEB
kumi project info WEB
kumi project describe WEB --file 說明.md      # - 是標準輸入
kumi project describe WEB --clear
kumi project formats
kumi project search web01.example.com        # 代號、名稱或專案說明包含這段文字的專案
kumi project search 192.0.2.10 --limit 20    # 預設 8 個，最多 20 個
```

- `list` 把封存的專案在名稱後面標「（已封存）」；`--json` 是伺服器的資料，專案有 `archivedAt`（沒封存是 `null`）。
- `rename` 的 `--name` 沒給或只有空白，結束代碼 2、什麼都不送出；名稱本身不合規定（例如太長）由伺服器擋，結束代碼 2。成功印「已把 WEB 改名為「…」。」。
- `archive`、`unarchive` 成功印「已封存 WEB。」「已取消封存 WEB。」。三個指令加 `--json` 都印伺服器回的專案，不印上面的句子。
- 沒有權限（403）或找不到專案（404）：結束代碼 1，印伺服器的訊息。
- `info` 印專案說明（網址、部署環境、主機，一份 Markdown）原文，空一行後寫「最後由 王小明 於 2026-11-20 16:05 更新」（時刻用本機時區）；沒寫過（或已清掉）印「這個專案還沒有說明。」，結束代碼 0。`--json` 印伺服器的資料：`{ "text", "updatedAt", "updatedBy": { "personId", "displayName" } }`，沒寫過三個都是 `null`。看得到專案的人都能讀（`items:read`）。
- `describe` 改說明，要專案的 `project.manage`（`blueprint:write`）。`--file` 與 `--clear` 要二選一，兩個都沒給或都給，結束代碼 2、什麼都不送。檔案的內容原樣送出（縮排與換行都保留）；內容只有空白時結束代碼 2、不送，要清掉說明請用 `--clear`，免得標準輸入忘了接內容就把原本的說明清掉。成功印「已更新 WEB 的專案說明。」「已清除 WEB 的專案說明。」，加 `--json` 印伺服器回的說明。
- `formats` 列出專案說明的格式（組織管理員在網頁上建的，加上內建的「網站案子」）：代號、名稱、標記（「預設」「內建」，沒有標記是空的）三欄，內容太長不印；`--json` 印伺服器的陣列（每筆 `{ "id", "name", "body", "isBuiltIn", "isDefault" }`，`body` 是格式的 Markdown），給 AI 代理寫說明前挑格式用，剛好一筆 `isDefault` 是 `true`。新增、修改、刪除格式只能在網頁上做（組織管理員）；開新專案時一併寫說明也只能在網頁上。看得到的人都能讀（`items:read`）。
- `search` 在看得到的專案（含封存的）裡，找代號、名稱或專案說明包含這段文字的（不分大小寫，`%`、`_`、`\` 照字面比對）；用途是輸入主機名稱、IP、網址，查出哪些專案在那裡。輸出代號、名稱（封存的標「（已封存）」）、說明裡的（說明裡第一個比對到的那一行，去掉行首的 Markdown 符號，超過 80 字以比對到的字為中心截斷、被截的一邊加「…」；代號或名稱就比對到的留空）三欄。排序：代號完全符合、代號或名稱以它開頭、代號或名稱包含、只有說明包含；同一層沒封存的在前，再依代號。沒有結果印「找不到符合的專案。」，結束代碼 0。`--json` 印伺服器的陣列，每筆 `{ "project": { …和 project list 的一筆同一個形狀 }, "snippet": "主機：web01.example.com" }`，`snippet` 是 `null` 表示代號或名稱就比對到了；沒有結果是 `[]`。文字去掉頭尾空白後要有 1 到 200 個字、不能有控制字元，`--limit` 是 1 到 20，不合格伺服器回 422 `query.invalid`（結束代碼 2）。文字以 `-` 開頭時前面加 `--`。只能看到你是成員的專案（組織管理員是全部），要 `items:read`。
- **說明裡不要放密碼與金鑰**：成員都看得到、搜尋得到、備份裡也有。寫密碼管理器裡的項目名稱。內容裡任何位置有私鑰（`-----BEGIN … PRIVATE KEY-----`，含 PGP 的 `PRIVATE KEY BLOCK`，前面有 Markdown 標記或接在同一行也算；或 PuTTY 的 `PuTTY-User-Key-File-數字:`）時伺服器回 422 `project.description_secret`，結束代碼 2；太長（上限 50000 字）是 `project.description_too_long`，有控制字元是 `project.description_invalid`。封存的專案也可以改說明。從沒寫過的專案送 `--clear` 什麼都不寫，直接回「沒有說明」的樣子（三個欄位都是 `null`）。
- **封存的專案只能看**：對它新增、修改、刪除項目、匯入等寫入，伺服器回 409 `project.archived`，`kumi` 結束代碼 1，照印伺服器的訊息；先 `kumi project unarchive` 才能再寫。讀取不受影響。

### 項目

```bash
kumi item list --project WEB                         # 預設一頁 50 個
kumi item list --project WEB --mine --category in_progress
kumi item list -p WEB --created-by @me               # 我開出去的項目（0.14.0 起；可以和 --mine 一起用，是「而且」）
kumi item list -p WEB --state todo --state doing --type task --limit 100
kumi item list -p WEB --priority high --priority urgent   # 優先順序可以重複，是「或」
kumi item list -p WEB --due-from 2026-11-16 --due-to 2026-11-22   # 到期日在這個範圍（含頭尾）
kumi item list -p WEB --dates-from 2026-11-09 --dates-to 2026-12-20   # 期間（開始日到到期日）和這個範圍有重疊的
kumi item list -p WEB --no-due                       # 只看沒有到期日的
kumi item list -p MAINT --field client=client_a --field billable=true   # 依自訂欄位篩選，不同欄位是「而且」
kumi item list -p MAINT --field client=client_a,client_b --field owner=me,_none   # 同一欄位的多個值用逗號，是「或」
kumi item list -p WEB --cursor "<上一頁的 nextCursor>"   # 接著看下一頁
kumi item list -p WEB --all                          # 自動翻完所有頁（一頁 200 個，除非另外給 --limit）
kumi item view WEB-12                                # 欄位、下層（最多 50 個）與最近 10 筆活動（新的在前）
kumi item create -p WEB --type task --title "改首頁" --assignee @me
kumi item create -p WEB --type task -t "查報價" --assignee ming@example.com \
  --set priority=high --set dueDate=2026-12-01 --set budget=1200 --set 'tags=["docs"]'
kumi item update WEB-12 --title "改首頁（第二版）" --assignee none
kumi item move WEB-12 --to doing
kumi item comment WEB-12 -m "已經改好，請看"
kumi item comments WEB-12                            # 列出留言：完整編號、作者、時間、（已編輯）、第一行；回覆縮排
kumi item comment-edit WEB-12 <留言編號> -m "新的內容"   # 只有作者能編輯，舊的版本伺服器留著；匯入的留言不能編輯
kumi item comment-delete WEB-12 <留言編號>             # 作者或專案管理者；留言留在原位標「已刪除」
kumi item comment-restore WEB-12 <留言編號>            # 復原已刪除的留言
kumi item delete WEB-12 WEB-13                       # 刪除（一個或多個）
kumi item restore WEB-12 WEB-13                      # 復原已刪除的（一個或多個）
kumi item deleted -p WEB                             # 已刪除的項目，最近刪除的在前
kumi item attach WEB-12 ./規格.pdf ./截圖.png         # 附加檔案（一個檔案送一次）
kumi item attachments WEB-12                         # 列出附件
kumi attachment download <編號>                      # 下載附件，並用 sha256 核對
kumi item move WEB-1 WEB-2 --to done                 # 一次改多個項目的狀態
kumi item update WEB-1 WEB-2 --assignee @me --set priority=high --set dueDate=2026-12-01
```

- `--assignee`：`@me`、同事的 email，修改時 `none` 是清掉。
- `--created-by`（`item list`，0.14.0 起）：`@me`、同事的 email 或人員編號，只列這個人建立的項目。項目的 `--json` 多 `createdBy`（`{ personId, displayName }`，一定有值，建立後不會變）；`item view` 的文字輸出多一行「建立者」。
- 列表還有下一頁時，`kumi` 會提示用 `--cursor` 接著看，或加 `--all`；`--json` 的 `nextCursor` 就是 `--cursor` 要給的值（`--all` 時 `items` 是所有頁合起來，`nextCursor` 是 `null`）。`item view` 的下層與活動被截斷時也會說。
- 單一項目的 `--json`（`item view`、建立、修改、改狀態）多一個 `descriptionRevision`：說明內容的版本（雜湊），網頁編輯說明時用來發現別人同時改過；`kumi` 改說明時不帶它，所以不檢查衝突，照舊是後寫的蓋掉先寫的。清單不帶。
- `--set 欄位=值` 可以重複。`priority`、`startDate`、`dueDate`、`estimate`、`parentKey`、`description` 是項目本身的屬性（`estimate` 是數字，其他是文字）；其他名稱是藍圖裡的自訂欄位，**值依欄位在藍圖裡的型別轉**（`kumi` 會查一次專案的藍圖，專案是 `--project`，或項目編號 `WEB-12` 的前綴 `WEB`）：
  - 文字、長文字、日期、單選、人員、連結：一律是字串，`--set code=123` 送的是文字 `"123"`，不用加引號；
  - 數字、金額：數字，不是數字就報錯（結束代碼 2）；
  - 勾選：只收 `true` 或 `false`；
  - 多選：寫成 JSON 的清單 `'["a","b"]'`，或用逗號隔開 `a,b`；
  - 值寫 `null` 是清掉，任何型別都一樣；
  - 藍圖裡沒有這個欄位，或藍圖讀不到（例如權杖只有 `items:write`）：退回舊的猜法，JSON 的數字、`true`、`false`、`null`、陣列照 JSON 送，否則是文字。
- 給人看的輸出用名稱，不用內部的代號：狀態、項目類型、欄位用藍圖裡的 `name`；狀態類別、優先順序、組織與專案角色用和網頁一樣的中文（例如「進行中」「高」「擁有者」）。`--json` 照舊是伺服器的原始資料，代號不變，腳本請用 `--json`。
- `--priority` 是 `none`（沒有設定）、`low`、`medium`、`high`、`urgent`。`--due-from`、`--due-to` 是 `YYYY-MM-DD`，可以只給一邊；有給日期範圍時，沒有到期日的項目不會出現，`--due-from` 晚於 `--due-to` 時結果是空的。`--dates-from`、`--dates-to`（`YYYY-MM-DD`，可以只給一邊）篩的是期間：項目的開始日到到期日（只有其中一個時就是那一天）和這個範圍有重疊的才列出，兩個日期都沒有的項目不會出現。`--no-due` 不能和 `--due-from`、`--due-to`、`--dates-from`、`--dates-to` 一起用（結束代碼 2）。「已過期」「本週」這類說法 `kumi` 不算，因為伺服器不知道你所在的時區：自己換成日期，例如 `--due-to 2026-11-15 --category in_progress`。
- `--field 欄位=值[,值…]` 依自訂欄位篩選，可以重複給不同的欄位（欄位之間是「而且」，和其他條件也是「而且」）；同一個欄位的多個值用逗號分開（是「或」）。值照傳給伺服器：單選與多選是選項的 key，人員欄位是人員編號或 `me`（伺服器換成你自己），勾選欄位是 `true` 或 `false`（`false` 是沒有勾），`_none` 是沒有填的（勾選欄位不能用）。欄位名稱要像藍圖的 key（`^[a-z][a-z0-9_]{1,39}$`）。沒有等號、欄位名稱格式不對、值是空的、同一個欄位給了兩次，都在送出前就擋下（結束代碼 2）；欄位不在藍圖裡、型別不能篩、超過 10 個欄位或 50 個值，由伺服器回 422，訊息照印。`--all` 翻頁時條件每一頁都帶著。
- `--category` 是狀態類別：`backlog`、`not_started`、`in_progress`、`done`、`canceled`。
- 不允許的狀態轉換，伺服器的訊息會說明原因，結束代碼是 2。
- **刪除與復原**：`item delete`、`item restore` 給一個編號時走單一項目的端點，多個編號走批次的端點。項目還有下層時不能刪除（先刪下層；同一批裡有它的下層時，伺服器會先刪下層）；復原時上層也是刪除的要先復原上層（同一批裡有上層時，伺服器會先復原上層）；藍圖已經沒有它的類型時無法復原。給多個編號時，已經刪除的再刪、沒有刪除的再復原算成功；只給一個編號時，再刪一個已經刪除的項目是「找不到」（結束代碼 1），再復原一個沒有刪除的項目照樣成功。多個編號的專案代號不分大小寫（`WEB-1 web-2` 是同一個專案）。
- `item deleted -p 代號` 列出專案已刪除的項目：編號、類型、標題、狀態、刪除的人（名稱）、刪除時間。`--limit`、`--cursor`、`--all`、`--json` 和 `item list` 一樣（`--json` 輸出 `{ "items": […], "nextCursor": … }`）。已刪除的項目頁沒有多選，復原一批請用 `item restore` 並把編號全部列出。
- **一次處理多個編號**（`delete`、`restore`、`move`、`update`）：
  - 編號要是同一個專案（前綴相同），否則結束代碼 2、什麼都不送出。
  - 多個編號的 `update` 只收 `--assignee` 與 `--set` 的 `priority`、`startDate`、`dueDate`；給了別的（`--title`、`description`、`parentKey`、`estimate`、自訂欄位）結束代碼 2，說明「一次改多個項目時只能改負責人、優先度、開始日、到期日」。寫 `null`（負責人寫 `none`）是清掉。
  - 成功的照單一操作的格式一行一筆印到標準輸出；失敗的逐筆印到標準錯誤（`WEB-3：<伺服器說的原因>`）。成功的照常生效，失敗的不影響它們。有任何一筆失敗，結束代碼是 2（和「不允許的狀態轉換」同一個）。
  - `--json` 印伺服器的原始資料：`move`、`update` 是 `{ "updated": […], "failed": […] }`，`delete` 是 `{ "deleted": […], "failed": […] }`，`restore` 是 `{ "restored": […], "failed": […] }`；`failed` 每筆是 `{ "key", "error": { "code", "message", "details" } }`。有失敗時結束代碼照樣是 2，所以腳本可以只看結束代碼。
  - 一次最多幾個編號由伺服器決定；超過時伺服器回 422（結束代碼 2）。
  - 批次的通知照單一修改的規則一筆一筆發（指派 20 個給同一個人，他會收到 20 則）。
- **附件**：
  - `item attach 編號 檔案...` 一個檔案送一次（multipart，欄位 `file`），成功印「已附加 規格.pdf（1.2 MB）」。送出前先檢查：檔案存在、不是資料夾、讀得到、不超過 25 MiB（和伺服器的上限一樣）；不合格的結束代碼 2、不送出。多個檔案時一個失敗不影響其他的：失敗的逐筆印到標準錯誤（`檔案：原因`），有失敗時結束代碼是第一個失敗的（本機檢查不合格是 2；伺服器回 413、422 是 2，403、404、409 與連不上是 1）。`--json` 印成功的附件，是一個陣列。附件的數量上限、檔名規則、專案已封存等由伺服器判斷。需要 `items:write`。
  - `item attachments 編號` 列出編號（`attachment download` 要用）、檔名、大小（B、KB、MB）、上傳的人（名稱）、時間；`--json` 是伺服器的原始資料（含 `sha256`）。沒有附件時印「沒有附件」。需要 `items:read`。
  - `attachment download 編號 [-o 路徑] [--item 項目編號] [--force]`：編號是附件的編號（GUID）。預設存成原本的檔名在目前的資料夾；`-o` 是資料夾就放在裡面，是檔案路徑就照它存（資料夾要已經存在）。檔名來自上傳的人，所以只留最後一段、去掉控制字元與格式字元、作業系統不收的字元換成 `_`，整理完是空的、`.` 或 `..` 就用 `attachment-<編號>`；它不會讓檔案寫到目前資料夾以外的地方（`-o` 是你自己給的，照原樣用）；Windows 的保留名稱（`CON`、`NUL`、`COM1`、`CONIN$`、`CONOUT$`…，含副檔名，如 `nul.txt`）前面加 `_`；不合法的字元（`: * ? " < > |` 與控制字元）在每個作業系統上都換成 `_`，不看執行的系統。目標已經存在時不覆蓋（結束代碼 2），加 `--force` 才覆蓋。內容串流寫到同資料夾的暫存檔，核對完才改成正式的名字，中途失敗或核對不合時不留下檔案（也不動原本的檔案）。
  - **檔名與 sha256 的核對**：不用 `--item` 也會核對。檔名取下載回應 `Content-Disposition` 的 `filename*`（UTF-8），沒有才用 `filename`；sha256 取回應的 `X-Attachment-Sha256` 標頭，內容算出來不一樣就結束代碼 1、不留下檔案。加 `--item 項目編號` 時，`kumi` 會多讀一次那個項目的附件列表，改用列表的檔名與 `sha256`（兩邊 sha256 不一樣時照列表核對，並在標準錯誤說明）。舊的伺服器沒有 `X-Attachment-Sha256` 又沒給 `--item` 時，只比對大小（`Content-Length`），並在標準錯誤提醒。`--json` 印 `{ "path", "sha256" }`（`sha256` 是實際算出來的）。需要 `items:read`；附件不存在或看不到是 404（結束代碼 1）。
- `kumi item view --json` 輸出 `{ "item": …, "children": […], "activity": […] }`；`activity` 是最近 10 筆，新的在前（伺服器的活動由舊到新分頁，`kumi` 照游標讀到最後一頁再取最後 10 筆，最多讀一萬筆）。

### 搜尋

```bash
kumi search 登入                  # 在所有看得到的專案裡，找標題包含「登入」的項目
kumi search WEB-12                # 編號完全符合的排最前面
kumi search 報價 --limit 20       # 預設 8 個，最多 20 個
kumi search 登入 --json           # 伺服器回的資料：{ "items": [ … ] }，和 item list 的項目同一個形狀
```

- 文字以 `-` 開頭時，前面加 `--`：`kumi search -- -x` 搜尋「-x」（`--` 之後都當位置參數）。
- 一個文字，要有空白就加引號。去掉前後空白後要有 1 到 200 個字，不能有換行或控制字元；`%` 與 `_` 當成普通字元。
- 排序：編號完全符合，再來標題以它開頭，再來標題包含它；同一層裡最近更新的在前。已刪除的項目不會出現，只找得到你有權限讀項目的專案（專案成員，或組織的擁有者與管理員）。
- 表格有編號、標題、狀態、專案（專案名稱）；狀態與專案名稱查不到時，退回印代號。沒有符合的項目時印「沒有找到符合的項目。」，結束代碼 0。
- 需要 `items:read` 範圍。

### 藍圖

藍圖是專案的設定：欄位、流程（狀態與轉換）、項目類型、檢視、角色。`kumi` 讓你把它匯出成檔案、改完預覽、再套用。

```bash
kumi blueprint export --project AGY -o agy.yaml     # 預設 YAML；--format json 或 -o 檔名.json 是 JSON
$EDITOR agy.yaml                                  # 例如加一個狀態與兩條轉換
kumi blueprint validate -f agy.yaml                 # 只在本機檢查，不連伺服器
kumi blueprint plan -f agy.yaml                     # 預覽：差異與對既有資料的影響，什麼都不存
kumi blueprint apply -f agy.yaml                    # 預覽、送出，有權限就直接套用
kumi blueprint apply -f agy.yaml --summary "加上法務審查"
```

匯出的檔案就是變更集的請求：`baseVersion`（這份是以藍圖第幾版為基準）加 `blueprint`（全文）。改完直接 `plan`、`apply`，不用另外描述差異。

- YAML 檔的第一行是註解 `# project: AGY`，`plan`、`apply` 沒給 `--project` 時用它。JSON 沒有地方放註解，要自己加 `--project`。
- 要搬動既有資料的修改（例如拿掉一個還有項目的狀態），在檔案裡加 `migrations`，寫明搬去哪裡；少寫的話，`plan` 會列出缺什麼。格式見 `schemas/blueprint/v0/changeset.schema.json` 與 `schemas/blueprint/v0/examples/`。
- 專案的藍圖在你匯出之後被別人改過（版本變了），`plan` 與 `apply` 會列為「現在還不能套用」，結束代碼 3：請重新匯出，把你的修改再做一次。
- `-f -` 從標準輸入讀文件。
- `--summary` 時，`kumi` 用和伺服器**同一份程式**把文件讀進來、加上 `summary`（文件裡已經有就取代），再以 JSON 送出：`0x10`、很大的整數、有引號的數字、`yes` 的意思都和不加 `--summary` 時一樣；伺服器不收的寫法（anchor、標籤、複雜的鍵、重複的鍵）直接回 2 並說明哪裡不對。`validate` 判斷文件是藍圖還是變更集，用的也是這一份讀法。
- `export -o 檔案 --json` 只寫檔案，不印給人看的訊息。
- `plan --json` 輸出預覽的原樣資料：`baseVersion`、`currentVersion`、`blueprint`、`changes`、`impacts`、`blockers`、`canApply`。

#### `apply` 的流程

1. 預覽。文件有問題：列出每個問題，結束代碼 2。有擋住套用的原因：列出，結束代碼 3，沒有送出。
2. 會動到既有資料（影響裡有項目數大於 0）而且沒加 `--yes`：互動時問「要套用嗎？」；不是互動的（腳本、CI，或加了 `--json`）就只列出影響，結束代碼 3，沒有送出。加 `--yes` 就不問。
3. 送出變更集。你有核准的權限就馬上套用，印出新的藍圖版本；沒有就印「已送出 #N，等待核准」，結束代碼 0，之後由有權限的人用網頁或 `kumi changeset apply` 核准。
   送出成功、套用卻失敗（資料在這之間變了、逾時、網路斷了）時，變更集已經在伺服器上：錯誤訊息（`--json` 時也一樣）寫「已送出 #N，還沒套用」加原因，並說明可以用 `kumi changeset apply N --project KEY` 再試，或 `kumi changeset reject N --project KEY` 拒絕；結束代碼照原因（資料擋住是 3，其他是 1）。
4. 和目前的藍圖沒有差異時，印「不需要套用」，什麼都不送。

### 變更集

```bash
kumi changeset list --project AGY
kumi changeset show 3 --project AGY      # 差異、影響與擋住的原因
kumi changeset apply 3 --project AGY     # 核准並套用一份等待核准的
kumi changeset reject 3 --project AGY
```

### 從 monday 匯入

把 monday 的看板搬進來：項目保留建立的人與時間，留言與回覆保留作者與時間，留言裡的圖片與檔案搬成附件，**一則通知都不發**，時間軸上標「從 monday 匯入」。匯入的留言之後誰都不能編輯（`comment.imported`），只有專案管理者能刪。可以重跑：已經匯入的略過，只補新的。規格與規則見 `internal/HANDOFF-monday-import.md`。

```bash
export MONDAY_API_TOKEN=…     # monday 的個人 API 權杖（Profile → Developers）；只放在環境變數，kumi 不寫進任何檔案
kumi import monday fetch --board 123 --out ./monday-dump                 # 1. 抓看板的結構與項目（不下載檔案）
kumi import monday plan --dump ./monday-dump --out ./monday.yaml         # 2. 產生對應檔的草稿（人員依 Email 對上）
#    改 monday.yaml：群組 → 專案與類型（不匯的留白或寫 skip）、狀態的標籤 → 狀態、monday 的欄 → 自訂欄位
kumi import monday fetch --board 123 --out ./monday-dump --mapping ./monday.yaml --files   # 3. 只抓要匯的群組，下載檔案
kumi import monday run --mapping ./monday.yaml --dry-run --add-members   # 4. 檢查（什麼都不寫）
kumi import monday run --mapping ./monday.yaml --add-members             # 5. 匯入
```

- 先在網頁上建好專案（模板、代號）、加好人員（離職的人也建，加進專案再停用）；自訂欄位與選項（例如「客戶」）也先在設定頁加好。`run` 的檢查會一次列出缺什麼，有任何問題就什麼都不寫（結束代碼 2）。
- 權杖要有 `items:read`、`items:write`、`blueprint:read`；`--add-members` 另外要 `blueprint:write`（把作者、負責人、關注的人加進專案，角色預設 `editor`，`--member-role` 可以換）。你要是每個目標專案的管理者。
- `fetch` 只讀 `MONDAY_API_TOKEN`，不用登入 pm；`plan` 與 `run` 用登入的 pm。`--out` 的資料夾已經有別的東西（不是 dump）時拒絕覆蓋。超過 25 MiB 的檔案不下載，記在 `files/skipped.json`。`run` 讀 dump 的檔案時，只搬真的在 dump 資料夾裡的普通檔案：`files/` 底下的符號連結（或路徑上有符號連結）一律不搬，當作「dump 裡沒有這個檔案」。
- dump 與對應檔裡有真實的人名、Email、留言與檔案：**只放在自己的電腦，不要提交**（repo 的 `.gitignore` 擋了預設的名字 `monday-dump*/`、`monday*.yaml`）。
- `run` 的報告：每個專案新增、略過的項目、留言、附件與沒搬的檔案，加上提醒（對不上的優先度、略過的值、截斷的留言、改用 `fallbackPerson` 的作者…）。`--report 檔案` 另外寫一份 JSON。中途失敗時印出已經完成的部分；再跑一次會從失敗的地方接下去。
- 很久沒動的項目：對應檔的看板層可以寫 `stale: { before: 2026-09-01, state: canceled }`（`plan` 的草稿有一段註解掉的，取消註解就生效）。最後一次動（建立、留言、回覆裡最晚的，以 Asia/Taipei 計）早於 `before`、而且還沒結束（類別是 backlog、not_started、in_progress）的項目改匯成 `state`，不然舊的待辦會全擠進每個人「我的工作」的「已過期」。報告的提醒會彙總有幾個，乾跑也看得到。
- 維運這類看板（項目沒有類型、很多沒填客戶、長期服務的群組）：看板層的 `titleRules` 依標題的關鍵字決定類型、補欄位的值（`- { project: MNT, match: [漏洞, CVE], type: security }`、`- { match: [EXA], fields: { client: client_exa } }`，不分大小寫、第一條符合的生效、子項目不套）；群組可以寫 `fields: { client: client_x }`（整組同一個客戶）與 `cycle: monthly`（每個項目依建立的月份，Asia/Taipei，放進「2026 年 11 月」的週期，沒有就建立）。欄位的值的優先順序是 monday 的欄、群組的 `fields`、規則。報告的提醒會彙總改了幾個類型、補了幾個欄位、放進幾個週期，乾跑也看得到。
- 重跑只**新增**：monday 上後來改了狀態、欄位、標題的，這裡不跟著改。匯完封存的專案（做完的案子）重跑時沒有新的項目與留言就略過；有新的時在寫入任何東西之前停下，請先取消封存。
- 伺服器依人限流（每分鐘 600 個請求）。`kumi` 被限流時照伺服器的 `Retry-After` 等一下再送（每個請求最多重送 10 次，每次最多等 60 秒），所以匯入中途會停頓一下，不是當掉；同一個人這時在網頁上也可能被限流。上千個項目的看板在本機幾分鐘就匯完，重跑更快。

### 本機代理（kumi agent）

讓網頁的 AI 助理用**這台電腦上自己登入的 Claude Code 或 Codex**（你自己的訂閱，不用 API 金鑰）。`kumi agent start` 是給人開著的長駐指令：開著它，網頁的模型選單才會出現「Claude Code（電腦名稱）」「Codex（電腦名稱）」；關掉就消失。**AI 代理不要自己執行它。**規格見 `internal/HANDOFF-local-agent.md`。

```bash
# 1. 用瀏覽器登入，建立本機代理用的權杖（範圍 agent:run、blueprint:read、blueprint:write、items:read）
kumi agent login --server https://pm.example.com
# 2. 開著它（終端機視窗不要關）；想要登入電腦時自動在背景啟動，改用下面的 kumi agent enable
kumi agent start [--name 名稱] [--engine claude-code|codex]… [--max-jobs-per-hour 件數] [--max-jobs-per-day 件數] [--allow-untested-engine] [--json]
# 3. 在網頁的 AI 助理選「Claude Code（電腦名稱）」送訊息
```

- `--name`：這台電腦在網頁上的名稱，預設是主機名稱。`--engine`：只提供某一個引擎，可以重複；預設兩個都試。
- 組織要先開「AI 設定 → 允許成員使用自己的 AI 帳號」，不然登記時回 403（說明會寫出來）。`kumi agent start` 與 `enable` 交給引擎的權杖（`KUMI_TOKEN` 或設定檔裡的）範圍必須恰好是 `agent:run`、`blueprint:read`、`blueprint:write`、`items:read`：缺哪一個，指令一開始就說缺哪一個；多了任何一個（尤其 `items:write`：引擎拿得到這把權杖，就改得動項目）也拒絕並說明，兩種都不會重試（背景程序也是直接結束，代碼 1），都叫你執行 `kumi agent login`。本機代理優先用 `kumi agent login` 存的專用權杖，沒有才用命令列登入的那一把（`KUMI_TOKEN` 最優先）。2026-11-23 以前建立、少了 `items:read` 的權杖現在也不能用：請重新 `kumi agent login`。
- 模型選單的「預設」會顯示實際的思考深度，例如「預設（中）」：登記時每個模型帶 `defaultEffort`。Claude Code 依探到的模型代號照官方文件的預設（Fable 5.2、Opus 5.5、Sonnet 5.5、Haiku 5.5 是 medium，Opus 4.7 是 xhigh，其他 high；探不到是空），Codex 讀 `models_cache.json` 的 `default_reasoning_level`；不在該模型選項裡的一律不帶。
- 圖片與 PDF（kumi 0.10.0 起）：網頁訊息附的圖片與 PDF 交給引擎，Claude Code 圖片與 PDF 都收，Codex 只有圖片（PDF 在網頁就會被擋下）。`kumi` 領到工作後用同一把權杖從 `GET /v1/agent/jobs/{工作}/attachments/{附件}` 下載到這件工作的暫存資料夾（檔名一律是 `attachment-N.副檔名`，不用伺服器給的檔名），核對大小與 SHA-256，Claude Code 改用 `--input-format stream-json` 把它們放進標準輸入的那一行訊息、Codex 每張圖片一個 `-i <路徑>`；工作結束連資料夾一起刪。下載失敗或核對不符，這件工作以失敗結束（「下載附件失敗」），不啟動引擎。0.9.x 以前的 kumi 收不到附件，網頁會請你 `kumi update`。主機的 `kumi host run` 一樣，下載走 `GET /v1/host-agent/jobs/{工作}/attachments/{附件}?lease=<租約>`（機器憑證與 `X-Kumi-Machine`）。
- 這台電腦同時只能有一個本機代理（前景或背景）：設定檔的資料夾裡的 `agent.lock` 是單一執行的鎖，第二個會結束（代碼 1）並說誰拿著（PID）。旁邊的 `agent.lock.json` 記著拿鎖的 PID 與是不是背景（只在鎖被拿著時才可信）。
- 電腦編號存在設定檔的資料夾（`agent.json`；`agent.lock`、`agent.lock.json`、`agent-usage.json` 與主機代理的 `host-agent.json` 也一樣，建立時就是 600，發現別人讀得到的會收緊），之後都用同一個，伺服器才認得是同一台電腦。

**開機自動啟動（macOS、Windows）**

```bash
kumi agent enable [--server 網址] [--name 名稱] [--engine claude-code|codex]… [--max-jobs-per-hour 件數] [--max-jobs-per-day 件數] [--allow-untested-engine] [--json]   # 設定並立刻在背景啟動
kumi agent status [--json]                                                              # 設定了沒、有沒有在跑、連不連得上、這一小時與今天領了幾件、記錄檔最後 10 行
kumi agent disable [--json]                                                             # 停掉背景程序並取消
kumi agent restart [--json]                                                             # 背景程序照啟動項目重開一次（換了新版之後用）
```

- `enable` 的檢查（任何一項不過就結束代碼 1，什麼都不改）：作業系統要是 macOS 或 Windows；權杖要存在設定檔（背景程序讀不到你的環境變數，用 `KUMI_TOKEN` 時請先 `kumi agent login`）且有 `agent:run` 等範圍；PATH 上要找得到 `claude` 或 `codex`；kumi 的執行檔不能在下載資料夾、暫存資料夾或別的使用者也寫得進去的資料夾（先解析符號連結再判斷；macOS 的路徑比對不分大小寫；資料夾往上任何一層、或執行檔本身，其他使用者或群組寫得進去也算；Windows 只擋下載與暫存資料夾，不查權限，因為 Windows 的開機自動啟動還是實驗性的；開機自動啟動每次登入都會執行它，放在那種地方別的程式換掉它就會被執行；會說怎麼搬，例如搬到 `~/.local/bin`，Windows 是 `%LOCALAPPDATA%\Programs\Kumi`，再執行一次）；沒有前景的 `kumi agent start` 在跑（在跑的話說 PID，請先關掉那個視窗）。通過後先停掉舊的背景程序、寫入啟動項目、立刻在背景啟動，最多等 10 秒看到它拿到鎖；沒等到時啟動項目照樣留著，結束代碼 1，並印出記錄檔最後幾行。`--json` 印 `{"enabled":true,"running":…,"logPath":"…"}`。
- `disable`：停掉背景程序（先請系統收尾，8 秒沒停才強制結束，而且結束前會確認程序名稱是 kumi、啟動時間和鎖檔裡記的一樣：程序編號被別的程式重用時不會結束到它；鎖檔沒記啟動時間（舊版寫的）或現在查不出來時無從確認，也不動手，會說明並請你自己結束那個程序或重開機。啟動時間一律用 `ps` 的 UTC 時間記錄與比對）、移除啟動項目；沒設定過就說「沒有設定自動啟動。」（代碼 0）。前景的本機代理不受影響。`--json` 印 `{"enabled":false,"wasEnabled":…}`。
- `status`：在跑的本機代理（前景或背景）會回報和伺服器的連線，`status` 多印一行「連線：」——「正在連線」（剛啟動，連不上時要等一兩分鐘才逾時）、「已連上伺服器」或「連不上或伺服器有問題，某時起一直在重試：原因」（連不上、5xx、429；時間是第一次失敗的時間；網路、VPN、proxy、防火牆或伺服器暫時的問題排除之後會自己接上，不用重新啟動）。kumi 0.9.1 起才回報，0.9.0 以前的沒有這一行。設了自動啟動、記錄檔卻不在時（多半是整個資料夾被清理工具刪掉，launchd 不會自己建回來）提醒執行 `kumi agent enable` 重建。 另外印「領走的工作：這一小時 …，今天 …」（本機的上限，見下面的 `--max-jobs-per-hour`、`--max-jobs-per-day`）。
- `status --json`：`{"supported","enabled","running","pid","background","connection","logPath","logMissing","usage":{"hour":{"used","limit"},"day":{"used","limit"},"limitsAreDefaults"},"recentLog":[]}`；不支援的作業系統 `supported` 是 false。`connection` 沒在跑或沒有回報時是 null，不然是 `{"state":"connecting|connected|retrying","since":"ISO 8601","error":…}`（`error` 只有 `retrying` 有，最多 300 字）；`logMissing` 是「設了自動啟動而記錄檔不在」。`limit` 是 0 表示不限；代理在跑時 `limit` 是那次啟動的值，沒在跑時顯示預設值（30、200），`limitsAreDefaults` 是 true，文字輸出也會註明。
- 背景程序跑的是 `kumi agent start --service`：和 `start` 一樣，差別在連不上伺服器（登入電腦時網路常還沒好）、5xx、429 時**不結束**，依退避（1、2、4…最多 60 秒）一直重試；401、403（權杖失效或被撤銷、組織不開放）、權杖缺範圍與沒有能用的引擎才結束（代碼 1，原因寫在記錄檔）。輸出每行前面加時間，訊息裡的換行與控制字元換成空白（伺服器回的字不能偽造出另一行記錄）。連線狀態寫在設定檔資料夾的 `agent.lock.json`（拿著鎖時才可信，結束時刪掉），`kumi agent status` 讀它。
- **macOS**：`~/Library/LaunchAgents/kumi.local-agent.plist`（`RunAtLoad`，沒設 `KeepAlive`，401、403 時不會被一直重開），用 `launchctl bootstrap／bootout gui/<uid>/kumi.local-agent`；記錄檔 `~/Library/Logs/Kumi/agent.log`（資料夾 700；超過 5 MB 時 `enable` 與每次背景程序啟動都先清掉；Windows 也是啟動時清）。背景程序啟動時記錄檔不存在，代表 launchd 開不了它（資料夾被刪掉了），標準輸出沒有人接：kumi 自己建資料夾、自己寫進記錄檔（建不了就照舊只寫標準輸出，不影響執行）。plist 帶執行 `enable` 當下的 `PATH`，有設才帶 `XDG_CONFIG_HOME`、`KUMI_AGENT_CLAUDE_PATH`、`KUMI_AGENT_CODEX_PATH`；**不帶任何權杖**，權杖由背景程序從設定檔讀。
- **Windows**：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `KumiLocalAgent`（整行命令，含空白與引號的參數照 Windows 規則加引號）；背景程序藏起主控台視窗，輸出寫到 `%LOCALAPPDATA%\Kumi\agent.log`；停止是結束程序（不能請它收尾，正在跑的工作由伺服器當逾時）。**Windows 的開機自動啟動是實驗性的**：沒有在真的 Windows 上完整驗證過，`kumi agent enable` 的輸出也會這樣說明；有問題請看 `agent.log` 或用 `kumi agent disable` 取消，並回報。
- `restart`（0.13.0 起）：macOS 用 `launchctl kickstart -k gui/<uid>/kumi.local-agent`，結束正在跑的、照已經載入的啟動項目再開一個（伺服器、名稱、上限照舊，不重寫 plist），最多等 18 秒看到新的程序（編號或啟動時間和原本的不同）拿到鎖；設了自動啟動而程序已經不在時也會開起來。正在跑的工作會中斷（伺服器當成失敗）。在跑的是終端機視窗裡的 `kumi agent start`、沒有設定自動啟動、launchd 沒有載入啟動項目（請執行 `kumi agent enable`）、新的程序沒起來（印記錄檔最後幾行）、Windows（還不支援，請登出再登入）時結束代碼 1。不用登入。`--json` 印 `{"restarted":true,"pid":…}`。
- `kumi update` 換了新檔之後會自己重開背景程序（見上面的「更新」）。kumi 搬了位置時，執行一次 `kumi agent enable` 讓啟動項目指到新的路徑。

**它做什麼**

- **本機上限**：這台電腦每小時最多領 30 件、每天（本機時區的日曆日）最多 200 件，`--max-jobs-per-hour`、`--max-jobs-per-day` 可以調（0 是不限；`enable` 會把你給的值寫進啟動項目，沒給的用預設）。用完就不領工作（照樣登記，網頁看得到引擎；送出的工作伺服器等 20 秒沒人領就失敗），記錄寫一行說明；額度恢復（過一小時、隔天）會自動繼續。領走的時間存在設定檔旁邊的 `agent-usage.json`（Unix 上權限 600，先寫暫存檔再換名，當機不會留下半個檔案），重啟不會歸零；檔案壞掉讀不出來時不當作沒用過：這一小時與今天都當作已經用到上限，壞檔改名成 `agent-usage.json.corrupt` 留著，並寫一行記錄（過了一小時或隔天、或刪掉壞檔才恢復）；`kumi agent status` 顯示用了多少。一件工作引擎來回的次數本機上限是 60（伺服器送來的更大就壓到 60）。開機自動啟動之後沒有人看著，這是你自己的 AI 訂閱額度的最後一道門。
- 只往外連伺服器（長輪詢），不開任何連接埠。登記這台電腦有哪些引擎與模型，網頁送來一則回覆就領走，在本機用無頭模式執行 `claude -p` 或 `codex exec`，把文字與工具的進度每 300 毫秒（或累積 50 則）回報給伺服器，結束時送結果與 token 用量。同時最多跑 2 件。
- 引擎**只能用 Kumi 的 MCP 上的唯讀與提出變更的工具**（`get_blueprint`、`list_templates`、`get_template`、`describe_usage`、`preview_changeset`、`propose_changeset`、`get_changeset`，以及讀項目的 `get_item`、`search_items` 、建議修改項目的 `propose_item_changes` 與建議新增項目的 `propose_new_items`，以及派工的 `get_project`（讀專案的成員、角色、週期、里程碑）、`list_people`（查組織的人員與職稱）與建議成員變動的 `propose_member_changes`；不選專案的組織對話只開 `list_templates`、`get_template`、`list_projects`、建議新專案的 `propose_project` 與 `withdraw_proposal`，MCP 設定不帶 `X-Kumi-Project`，工作回報的進度帶建議的編號 `projectProposalId`），提出的變更集標「AI 代理提出」，還是要有人核准。助理能讀項目，但只能「建議」修改：`propose_item_changes` 只存一份建議，使用者在網頁按「套用」才會真的改；工具成功時，回報的進度會帶建議的編號（`itemProposalId`）給網頁顯示。`propose_new_items` 同樣只存一份「新增項目」的建議（使用者在網頁按「全部建立」才會真的建立），進度帶 `newItemProposalId`；要 kumi 0.15.0 以上，舊版照樣能用、只是少了這個工具（請 `kumi update`）。`propose_member_changes` 也只存一份「成員建議」（加入、改角色、移出專案；使用者在網頁按「套用」才會寫入），進度帶 `memberProposalId`；`get_project`、`list_people` 與它要 kumi 0.16.0 以上，舊版照樣能用、只是少了這三個工具（要查專案成員、組織的人或加人進專案時，請 `kumi update`）。`propose_item_deletions` 只存一份「刪除建議」（使用者在網頁按「刪除」才會刪，可以復原），進度帶 `itemDeletionProposalId`；`propose_bulk_item_changes` 只存一份「批次修改建議」（一次改好幾個項目，按「全部套用」才寫入），進度帶 `itemBatchProposalId`；`withdraw_proposal` 撤回自己還沒套用的建議（不佔提出的名額、進度不帶編號）；這三個工具要 kumi 0.17.0 以上，舊版照樣能用、只是少了它們（要刪除項目、一次改多個項目或撤回建議時，請 `kumi update`）。`propose_new_items` 另外收 `addMembers`（先加進專案的人）、`cycleId`、`milestoneId`，舊版 kumi 不檢查參數，照樣能傳。不能讀寫你的檔案、不能跑指令、不能上網：Claude Code 關掉所有內建工具，Codex 關掉執行指令的工具與其他碰得到電腦的功能；專案的設定是組織裡的人寫的，可能夾帶提示注入，不關的話可能被誘導去讀你電腦上的檔案。
- 每件工作在一個新的空暫存資料夾（權限 700）裡跑，結束就刪；對話從標準輸入送，不放在命令列（同一台電腦的其他使用者用 `ps` 看得到命令列，裡面不會有對話、專案代號或權杖；Codex 的專案代號與工作編號經環境變數 `KUMI_MCP_PROJECT`、`KUMI_MCP_JOB` 交給它的 `env_http_headers`）；不載入你自己的設定、hooks、技能與記憶（2026-11-26 在 Claude Code 2.1.332、Codex 0.163.0 實測：Claude Code 不載入使用者層級的 `CLAUDE.md`；Codex 全域的 `$CODEX_HOME/AGENTS.md`（含 `AGENTS.override.md`）沒有旗標或設定關得掉，照樣會帶進去，所以 `kumi agent start` 啟動時會列出這個檔案，檔案裡不要放機密）。Claude Code 的系統提示是工作資料夾裡的檔案（`--append-system-prompt-file`）；Codex 沒有這種旗標，系統提示用 `-c developer_instructions` 放在命令列：內容是公開的系統提示加上一句權限說明，沒有機密，而改放標準輸入會降成一般的使用者訊息、規則的效力變弱（只有 Windows 的命令列放不下時才改放標準輸入的最前面）。
- 子程序的環境是**白名單**：只留跑得起來、登入得了需要的（`PATH`、`HOME`、使用者與語系、終端機、時區、暫存資料夾、`HTTP_PROXY`／`HTTPS_PROXY`／`NO_PROXY`、`SSL_CERT_FILE`、`NODE_EXTRA_CA_CERTS`、`XDG_*`、`CLAUDE_CONFIG_DIR`、`CODEX_HOME`，Windows 另有 `SystemRoot`、`USERPROFILE`、`APPDATA` 這類），其他一律不交給引擎：你的 API 金鑰（`ANTHROPIC_API_KEY`、`OPENAI_API_KEY`、`CODEX_API_KEY`，要用的是你的訂閱登入，不是 API 計費）、雲端與資料庫的憑證、`KUMI_TOKEN` 都進不去。再加上給 MCP 設定引用的 `KUMI_MCP_TOKEN`（你的權杖，只存在這個子程序的環境裡，不寫進檔案或命令列）。公司網路用了非標準名稱的代理或憑證變數時，把名稱（逗號分隔）放進環境變數 `KUMI_ENGINE_PASSTHROUGH_ENV`，那幾個才會交給引擎。`KUMI_TOKEN`、`PM_TOKEN` 與 `KUMI_MCP_*` 不論怎麼寫都不放行（`KUMI_MCP_*` 由 kumi 自己設）；放行 `ANTHROPIC_API_KEY`、`OPENAI_API_KEY` 這類金鑰是你的選擇，但引擎會因此改走 API 計費、不再用你的訂閱登入。`kumi setup`、`kumi host login` 這類繼承終端機的子程序也一樣用白名單（另外放行開瀏覽器要用的 `DISPLAY`、`BROWSER` 之類），而且每次在一個新建、只有你進得去的空資料夾裡跑，用完就刪：共用暫存資料夾裡別人放的 `.mcp.json` 之類的專案設定影響不到它。
- 終端機每件工作印一行（時間、專案（組織對話印「不選專案」）、引擎、模型、結果、秒數），**不印對話內容**；`--json` 時標準輸出每行一個 JSON（`{"event":"ready",…}` 與每件工作的 `{"event":"job","status":"complete|stopped|failed",…}`），提示與警告寫標準錯誤。

**它不做**

- 不改、不包裝 `claude`／`codex` 的執行檔；不讀、不存、不轉送它們的登入憑證。
- 一件工作只會來自你自己在網頁上送出的訊息，伺服器不會把別人的工作派到你的電腦。
- 不碰項目（`items:*` 的工具）；不接續引擎自己的工作階段（每一則都把整段對話重送）。

**引擎的條件**

| 引擎 | 要有 | 檢查（只跑不送提示的指令） |
|---|---|---|
| Claude Code | `claude` 在 PATH 上、已登入 | `claude --version`、`claude auth status` |
| Codex | `codex` 在 PATH 上、已登入（ChatGPT）、版本夠新 | `codex --version`、`codex login status`、`codex exec --help`、`codex features list` |

- Codex 的 `codex features list` 一定要有 `shell_tool`（才關得掉執行指令的工具），沒有就**不提供 Codex**，訊息會說原因。`--disable` 只帶列得出來的功能。模型清單讀 `$CODEX_HOME/models_cache.json`（沒設 `CODEX_HOME` 時是 `~/.codex`），讀不懂就只有「預設」。Claude Code 的模型代號固定是 `default`、`opus`、`sonnet`、`haiku`（別名，Claude Code 更新後自然指到新的模型）；啟動時各用和跑工作相同的隔離旗標探一次實際的模型（平行、每個最多 20 秒，讀到 `system/init` 的 `model` 就結束整個程序樹，那時還沒有送出任何模型請求，不花額度），把版本放進顯示名稱（「預設（Opus 5.5）」「Sonnet 5.5」），探不到的退回「Opus」「Sonnet」這類名稱；啟動那一行也會印出預設模型。
- **已驗證的版本範圍**：Claude Code 2.1.330 以上、3.0.0 以下（不含），Codex 0.163.0 以上、1.0.0 以下（不含）。範圍外（含讀不出版本）預設不提供並說明；`--allow-untested-engine` 放行，啟動時警告。放行只跳過版本檢查，必要的旗標與 `shell_tool` 照樣要有。
- 找不到執行檔、沒登入的引擎會略過並說怎麼裝、怎麼登入；兩個都不能用就結束（代碼 1）。執行檔不在 PATH 上時，用環境變數 `KUMI_AGENT_CLAUDE_PATH`、`KUMI_AGENT_CODEX_PATH` 指定完整路徑。

**安全上的限制**

- **MCP 網址要同源**：伺服器送來的 MCP 網址必須和登入的伺服器同源（scheme、主機、連接埠一樣），路徑剛好是（伺服器基底路徑加）`/mcp`，沒有 query 與 fragment，也不能有 `$`、`{`、`}`、`"`、`\`、`%`、空白、控制字元；不合格的工作不跑。寫進引擎設定的是正規化後的網址。原因：Claude Code 會展開設定檔裡的 `${VAR}`，網址夾帶變數就會把你的環境變數送去別的主機。
- **MCP 限在對話的專案**：兩個引擎的 MCP 設定都固定帶標頭 `X-Kumi-Project: <專案代號>`（Claude Code 的 `mcp.json` 與 Codex 的 `http_headers`，字面值），伺服器對代理的權杖只准碰那個專案；同一處還有 `X-Kumi-Job: <工作編號>`（領到的那件工作，伺服器用它計這件工作的工具額度，0.9.0 起，舊版的 kumi 打不了工具、要 `kumi update`）；專案代號先用 `\A[A-Z][A-Z0-9]{0,19}\z` 檢查，不合格的工作不跑；組出啟動計畫、要變成環境變數與標頭之前再檢查一次專案代號與工作編號（GUID），不合格就不啟動那件工作。
- **工具只能是 Kumi 的十九個**（`get_blueprint`、`list_templates`、`get_template`、`describe_usage`、`preview_changeset`、`propose_changeset`、`get_changeset`、`get_item`、`search_items`、`propose_item_changes`、`propose_new_items`、`get_project`、`list_people`、`propose_member_changes`、`propose_item_deletions`、`propose_bulk_item_changes`、`withdraw_proposal`，加上組織對話的 `list_projects`、`propose_project`）：伺服器送來的 `allowedTools` 只要有別的名字，這件工作就不跑。
- **Windows 只接受 `.exe` 的引擎**：`.cmd`、`.bat` 要經過 cmd.exe，它的跳脫規則和一般程式不同，系統提示裡的特殊字元會變成命令注入，所以找到的是批次檔時不提供那個引擎（說明會寫出來），請改用 `.exe` 的安裝方式，或用 `KUMI_AGENT_CLAUDE_PATH`、`KUMI_AGENT_CODEX_PATH` 指向 `.exe`。PATH 裡的相對路徑項目一律略過。
- **必要的旗標**：啟動時用 `claude --help`、`codex exec --help` 確認安全用的旗標都在（Claude Code：`--tools`、`--allowedTools`、`--setting-sources`、`--strict-mcp-config`、`--disable-slash-commands`、`--append-system-prompt-file`、`--no-session-persistence`、`--permission-mode`、`--mcp-config`；Codex：`--strict-config`、`--ignore-user-config`、`--ignore-rules`、`--ephemeral`，並且要有 `shell_tool`），缺了就不提供那個引擎並請你更新；旗標要整個比對（`--tools` 不會因為 help 裡有 `--tools-extra` 就算有）。探測用的指令也各在新建、權限 700 的空暫存資料夾裡跑。
- **引擎一出現預期以外的東西就停下**：Claude Code 的 `system/init` 沒有出現、欄位缺漏、有內建工具、技能或斜線指令不是空的，或在 init 之前就有動作；Codex 出現 `agent_message`、`reasoning`、`mcp_tool_call`（server 要是 `kumi`）、`todo_list`、`error` 以外的項目。這件工作立刻結束並回報失敗。
- 使用者在網頁按「停止」或按 Ctrl-C 而停下的工作，一律以「中斷」結束，引擎被打斷時吐出的診斷字串不送給伺服器、也不印出來。
- 引擎正常結束後，它留下的背景程序也會被清掉（macOS、Linux）。程序收到 SIGTERM 或 SIGHUP 的處理和 Ctrl-C 一樣，收尾卡住時再來一次就直接結束。

**怎麼停**

- 背景的那一個：`kumi agent disable`。
- Ctrl-C（或程序收到 SIGTERM）：正在跑的工作照「中斷」結束、把結果送給伺服器，再離開（結束代碼 0）；再按一次 Ctrl-C 直接結束程序。
- 網頁上按「停止」：下一次回報時本機代理知道，先對引擎送中斷訊號，5 秒內沒結束就強制結束整個程序樹（macOS、Linux；Windows 直接強制結束主程序）。
- 本機的逾時是 5 分鐘 30 秒（伺服器的 5 分鐘加 30 秒）。網路中斷時每 1、2、4…最多 30 秒重試一次；權杖無效、被撤銷或沒有權限（401、403）會停下來說明（結束代碼 1）。

**疑難排解**

- 網頁上看不到「Claude Code（電腦名稱）」：`kumi agent start` 還開著嗎？（設了自動啟動的話，`kumi agent status` 看背景程序在不在、連不連得上、記錄檔最後幾行寫什麼。「連線：連不上或伺服器有問題」時先確認瀏覽器打得開 Kumi：伺服器限制來源位址時要先連 VPN。）（60 秒內有登記才算在線）組織有開「允許成員使用自己的 AI 帳號」嗎？啟動時有沒有說某個引擎被略過？
- 「這台電腦的 Claude Code 沒有登入」：在終端機執行 `claude`，依畫面登入；Codex 是 `codex login`。
- 「本機的 Claude Code 沒有照預期關掉內建工具」「連不上 Kumi 的 MCP」：引擎的版本和預期不同，或連不上 `/mcp`（網址是伺服器的網址加 `/mcp`，檢查 proxy 與防火牆）。這件工作會立刻停下，不會讓引擎繼續。
- 「本機的 Codex 想用 Kumi 以外的工具，已經停下」：Codex 嘗試執行指令、改檔案或上網，一律停下。更新 Codex 後再試。
- 送出 20 秒沒人領走，網頁會說「你電腦上的 Kumi 代理沒有回應」：看終端機有沒有「重試」的訊息。
- 錯誤訊息裡的權杖與本機路徑會換成 `…`；引擎輸出的內容（對話、工具結果）不寫進任何記錄。

### 主機 worker（kumi host）

Kumi 主機上常駐的 `ai-worker` 容器用的指令：讓全組織的成員在網頁的助理選「主機上的 Claude Code／Codex」。**給主機的服務與組織擁有者在主機上用，AI 代理不要自己執行。**規格見 `internal/HANDOFF-host-agent.md` 第 8 節；網頁上的新增、配對碼、登入碼都由組織擁有者操作。

```bash
kumi host pair [--server 網址] [--name 名稱]      # 配對碼從標準輸入讀
kumi host run [--engine claude-code|codex]…        # 服務本身（容器的 ENTRYPOINT）
kumi host login <claude-code|codex>                # 登入碼從標準輸入讀
kumi host status [--json]
kumi host logout <claude-code|codex>
```

- **碼不放在命令列**：`pair` 與 `login` 的碼在終端機上提示輸入（不回顯），不是終端機就讀標準輸入的第一行（一個位元組一個位元組讀到換行，後面的內容留給子程序：`printf '碼\n金鑰\n' | … kumi host login codex` 的金鑰會交給 codex）；命令列上給碼是用法錯誤（結束代碼 2）。典型用法：`docker compose exec -it ai-worker kumi host pair`。
- **狀態檔** `host-agent.json` 在 `$KUMI_HOST_STATE_DIR`（預設 `~/.local/state/kumi-host/`，容器裡是 `/home/kumi/.kumi-host/`，在資料卷上），資料夾 700、檔案 600，內容 `{ server, agentId, agentName, machineKey, credential, logins: { claude_code | codex: { loginId, fingerprint } } }`。`credential` 是機器憑證（`kha_…`，只能呼叫 `/v1/host-agent/*`）；`machineKey` 第一次產生後一直沿用（重新配對也不換）；`loginId` 是 Kumi 的登入流程完成的那一次登入，`fingerprint` 是當時登入帳號的雜湊（只存本機，不送出），用來發現「在 Kumi 以外換了登入」：指紋對不上就不再報登入編號（伺服器當作未授權）。憑證、登入碼、配對碼、機器憑證都不出現在命令列、記錄、輸出與引擎子程序的環境裡；引擎只拿到每件工作專用的工具授權（環境變數 `KUMI_MCP_TOKEN`）。
- **`run` 設定有問題時不結束**（容器才不會一直重啟）：沒配對 → 每 30 秒看一次狀態檔、記「等待配對」；伺服器回 401（代理被停用、憑證過期、機器編號和配對時的不同、或配對的人不再是擁有者）→ 不結束，記「伺服器不認這把機器憑證：被停用、過期或需要重新配對」，每 60 秒再試一次（重新啟用之後同一把憑證自己接上；每 30 秒看狀態檔有沒有換新憑證）；網路錯誤 → 退避 1～30 秒；伺服器轉址（設了 `Security:RequireHttps` 的部署）→ 說明要把 `AI_WORKER_SERVER` 換成對外的 https 網址，每分鐘再試；每次登記都帶 `machineKey` 與電腦名稱；伺服器在憑證剩不到 30 天時回 `rotatedCredential`（新的 `kha_…`），worker 等沒有工作在跑時才寫進狀態檔（暫存檔加 rename、權限 600；只換掉發新憑證時的那一把，中間被重新配對就不蓋掉），之後用新的，伺服器在新的第一次被用到時讓舊的作廢。第一次用一份憑證登記成功時記「已配對「名稱」，開始登記到 …」。SIGTERM／SIGINT：正在跑的以「中斷」結束、送出結果，結束代碼 0。
- **探測**：啟動時、每 5 分鐘、登出之後、狀態檔的登入變動時，以及工作失敗而錯誤像是登入失效時，重新探測兩個引擎（版本、安全用的旗標、登入狀態、計費方式），**不送任何模型請求**，結果放進每次登記；**每件工作開始前**再跑一次登入狀態（只有狀態指令），沒登入、不是 API 計費或帳號換了就不跑、那件以失敗結束。讀引擎家目錄的檔案（`auth.json`、Codex 的 `models_cache.json`；位置照 `CODEX_HOME` 等環境變數）一律只讀一般檔案、不跟隨符號連結、最多 1 MiB，讀不到、太大或讀不懂當作沒有（沒有指紋就不比對、模型清單用預設）並寫一行記錄，不會讓 kumi 當掉。Codex 的帳號指紋只看 `auth.json` 的登入方式、API 金鑰與 account_id（Codex 刷新權杖會改寫檔案，不能雜湊整個檔）。計費判斷：`claude auth status` 的 `authMethod` 是 `claude.ai` → 訂閱、`api_key` → API，其他 → 不明；`codex login status` 的「Logged in using ChatGPT」→ 訂閱、「Logged in using an API key」→ API，其他 → 不明。訂閱與不明的伺服器一律不接為組織來源。輸出裡的 email、組織名稱、金鑰片段不送、不印。
- **回報**：每批帶租約（`lease`）與這批第一則的流水號（`firstSeq`）；送出而不確定有沒有到的那一批原封不動重送，以伺服器回的 `nextSeq` 為準；收到 `cancelled`、401、404、409 就停下引擎、以「中斷」結束；連續三批的內容被拒（4xx）也停下。伺服器派來的工作超過這台同時能跑的件數時，立刻回報失敗（不默默丟掉）。
- **`login`**：認領登入碼後執行 CLI 自己的登入並**繼承終端機**（Claude Code：`claude auth login --console`；Codex：`codex login --with-api-key`，貼上金鑰後按 Ctrl-D），Kumi 不碰密碼、授權碼、金鑰。跑完探測登入與計費、回報伺服器；伺服器拒絕（例如訂閱帳號、期限過了、擁有者身分變了）就執行 CLI 的登出、清掉 `loginId`、印出原因、結束代碼 1。
- **`logout`**：先清掉狀態檔的登入編號，再執行 CLI 的登出（平常用網頁的「登出」，worker 下一次登記時收到並自己執行）。
- **`status`**：印配對狀態與兩個引擎的探測結果（含「Kumi 已授權」與否），不連伺服器、不印憑證。
- 伺服器用 `--server`、`KUMI_SERVER`（`pair`；之後存在狀態檔）。不是 https 又不是本機的網址（compose 裡的 `http://api:8080`）要明確放行：`KUMI_ALLOW_INSECURE=1`（compose 已設）。引擎的執行檔用 `KUMI_AGENT_CLAUDE_PATH`、`KUMI_AGENT_CODEX_PATH` 指定（演練時指到假引擎）。
- 結束代碼：`run` 只有被要求結束時才結束（0）；`pair`／`login`／`logout` 失敗是 1（登入被拒絕、連不上、沒配對）、碼不對或用法錯誤是 2。

映像檔與 compose：`deploy/ai-worker/Dockerfile`、`compose.yaml` 的 `ai-worker`（`profiles: ["ai-worker"]`，自己的 `ai-worker` 網路（連不到 db）、非 root、`cap_drop: ALL`、唯讀根目錄、`mem_limit`／`pids_limit`、資料卷 `ai-worker-home`）；演練用的假引擎在 `deploy/ai-worker/fake-engines/`，用 `deploy/ai-worker/compose.fake-engines.yaml` 掛進去，不進正式映像檔。

## 結束代碼

| 代碼 | 意思 |
|---|---|
| 0 | 成功 |
| 1 | 一般錯誤：連不上伺服器、沒登入或權杖無效、找不到、沒有權限或範圍不夠、伺服器出錯 |
| 2 | 驗證失敗：文件不合規定、欄位的值不對、不允許的狀態轉換；指令的用法不對（少了參數、不認得的選項）也是 2 |
| 3 | 計畫裡有需要人決定的資料影響，或現在還擋著套用（`plan`、`apply`、`changeset apply`） |

`kumi agent start` 是長駐指令：按 Ctrl-C 正常結束是 0；沒有能用的引擎、權杖不能用（缺範圍、無效、被撤銷）是 1；用法不對是 2。

錯誤訊息寫到標準錯誤：伺服器說的原因，加上每個細項（路徑與說明）。細項和主要訊息一樣時只印一次。給人看的輸出（`--json` 以外）印出之前，會濾掉伺服器回的文字裡的控制字元（例如 ESC 序列）與 Unicode 的格式字元（方向覆寫、零寬字元：它們能讓畫面上的字和實際內容不同），單獨的歸位字元（沒接著換行的 `\r`，會讓後面的字蓋掉前面的）換成空白，標題或留言裡夾帶的終端機指令不會生效。未預期的例外只印一行簡短的訊息（只有例外的型別，沒有內容與堆疊），結束代碼 1。`plan` 在有資料影響或擋住的原因時也回 3，所以 CI 可以用它檢查「這份修改會不會動到資料」。

## 在 CI 裡用

不用 `kumi auth login`；把權杖放進 CI 的機密變數，用環境變數交給 `kumi`：

```yaml
# 例：GitHub Actions
env:
  KUMI_SERVER: https://pm.example.com
  KUMI_TOKEN: ${{ secrets.KUMI_TOKEN }}
steps:
  - run: dotnet tool install --global PmTool.Cli --add-source ./nupkg
  - run: kumi blueprint validate -f blueprint/agy.yaml
  - run: kumi blueprint plan -f blueprint/agy.yaml      # 有資料影響會停在這裡（3）
  - run: kumi blueprint apply -f blueprint/agy.yaml --summary "CI 套用 ${{ github.sha }}"
```

建議：

- 給 CI 專用的權杖，範圍只勾要用的（例如只做 `plan` 就只要 `blueprint:read`），並設一個期限。
- 權杖不要印出來、不要放進記錄。`kumi` 不會把權杖寫到輸出；不要在腳本裡用 `set -x`。
- 自己改密碼**不會**撤銷存取權杖（CI 用的權杖不該因為換密碼就壞掉）。懷疑外流，到網頁撤銷。帳號被停用或管理員重設密碼時，那個人的權杖會全部撤銷。
- 腳本裡用 `--json` 取資料，用結束代碼判斷成敗，不要去解析給人看的文字。

## 給 AI 代理用

`skills/` 底下有兩份 Agent Skill，給 AI 代理（Claude Code、Codex）用，內嵌在 `kumi` 裡（單一執行檔也帶得到）：

- `kumi`：怎麼用 `kumi` 查項目、建立與修改、留言與提及、改藍圖，以及哪些事要先問人（自己寫的留言、刪除、批次修改、封存專案、套用會動到資料的藍圖）；不確定有沒有送成功時先查再重送。
- `kumi-planning`：東西放在哪裡、用什麼結構。透過問答了解團隊、案子與需求：從資料與系統查得到的，列出來請使用者確認；查不到的，附上建議的答案再問，一輪最多五題。導入時決定專案怎麼分、選哪個模板、類型／狀態／欄位怎麼設；把客戶來信、會議結論、需求文件拆成有負責人、到期日與完成條件的項目；定期檢視過期、卡在等客戶、沒人負責的工作，回答做到幾成（先報個數、估計填得夠才給百分比）；案子結束時檢查還有什麼沒收尾。系統沒有的（項目之間的先後、風險清單、每個人的負載、背景追蹤）照實說做不到。下指令的部分靠 `kumi`，所以兩份一起裝。

（改名前叫 `pm-tool`、`pm-tool-planning`；`kumi skill install` 會處理舊名稱的資料夾，見下面。）

```bash
kumi skill install                       # 裝給偵測得到的 Claude Code／Codex（使用者層，所有專案都看得到）
kumi skill install --project <資料夾>     # 只裝給某個專案（<資料夾>/.claude/skills 與 <資料夾>/.agents/skills）
kumi skill status                        # 每個位置裝了哪些、版本、是不是最新
kumi skill update                        # 換成這份 kumi 內含的版本
kumi skill uninstall
```

`kumi setup` 也會問要不要裝；`kumi update` 換好新執行檔之後，會用新的執行檔跑 `kumi skill update`，所以 skill 永遠和 kumi 同一版。

### kumi skill

- `install [--agent claude-code|codex|all] [--project 資料夾] [--force] [--yes]`：
  - 位置：Claude Code 是 `${CLAUDE_CONFIG_DIR:-~/.claude}/skills/<名稱>/`，Codex 是 `${CODEX_HOME:-~/.codex}/skills/<名稱>/`（兩個環境變數是相對路徑時，依目前所在的資料夾解析；家目錄在 Windows 先看 `USERPROFILE`，其他系統先看 `HOME`）。`--agent` 沒給時裝到**偵測得到**的引擎（PATH 上有 `claude`／`codex`——`KUMI_AGENT_CLAUDE_PATH`、`KUMI_AGENT_CODEX_PATH` 優先——或它的設定資料夾存在）；一個都偵測不到時說明原因、不寫任何東西、結束代碼 0。
  - `--project <資料夾>`：改裝到 `<資料夾>/.claude/skills/`（Claude Code）與 `<資料夾>/.agents/skills/`（Codex 在 repo 裡讀這裡）；沒給 `--agent` 就兩個都裝（不偵測）。資料夾不存在是用法錯誤（2）。
  - 每個裝好的資料夾有一個標記檔 `.kumi-skill.json`（名稱、kumi 版本、時間，以及裝的當下每個檔案的 SHA-256）。同名資料夾**有標記**、檔案也和標記記的一樣，就換成新版：先寫到旁邊的暫存資料夾、再用改名換掉，換到一半失敗不會留下半套（舊的放不回去時，舊的留在 `.kumi-skill-old-<名稱>-*` 並在錯誤訊息寫出位置；程序在兩次改名之間被中斷留下的備份，下一次 `install`／`update` 會先放回去）；版本一樣就說「已經是最新版」、不動。以下情況不覆蓋，說明要加 `--force`（結束代碼仍是 0）：**沒有標記**（使用者自己放的）；**檔案改過**（有檔案和標記的雜湊不一樣：改過、多了、少了，`.DS_Store` 不算；舊格式、沒有雜湊的標記當成沒改過）；**已安裝的版本比這份 kumi 內含的新**（不降級）。
  - 舊名稱：位置裡有 `pm-tool`、`pm-tool-planning`（`SKILL.md` 開頭的 front matter，也就是第一行 `---` 到下一個 `---` 之間，`name` 是這個名稱才算；正文裡寫的不算）時，同一份 skill 出現兩次會讓代理混淆：互動模式問要不要移除；`--yes`（`-y`）直接**整個資料夾刪除**（裡面你改過的東西也一起沒了）；非互動又沒有 `--yes` 就留著、在標準錯誤警告。
  - 結束代碼：全部成功、略過或有警告是 0；有任何一個寫入失敗是 1。
- `update [--agent …] [--project 資料夾]`：只更新**有標記**的（改過的、版本比較新的跳過並警告）（沒給 `--project` 看兩個引擎的使用者層位置，不管有沒有偵測到）；一個都沒有就說明並以 0 結束。
- `status [--agent …] [--project 資料夾]`：列出每個位置的 `kumi`、`kumi-planning` 是未安裝、有標記的版本（最新或不是最新）、檔案改過、版本比這份 kumi 新、有同名資料夾但沒有標記，以及舊名稱的殘留。
- `uninstall [--agent …] [--project 資料夾] [--force]`：只刪有標記的；沒有標記的說明並留著。檔案改過（雜湊和標記不符）的也留著，提示「檔案改過，要移除請加 --force」（結束代碼 0，`--json` 的 `result` 是 `skipped_modified`）；`--force` 才連同你改的內容一起刪。
- `--json`（四個都有）：`{ "version", "results": [ { "agent", "directory", "skill", "path", "result", "version", "error" } ] }`。`agent` 是 `claude-code` 或 `codex`；`version` 是裝好的版本（沒有時 `null`）；`error` 只有失敗時有值。`result`：install、update、uninstall 是 `installed`、`updated`、`up_to_date`、`skipped_foreign`（沒標記、沒加 `--force`）、`skipped_modified`（檔案改過）、`skipped_newer`（已安裝的版本比較新，`version` 是已安裝的版本）、`removed`、`not_ours`（uninstall 遇到沒標記的）、`legacy_removed`、`legacy_kept`、`failed`；status 是 `current`、`outdated`、`foreign`（同名但沒標記）、`modified`（檔案改過）、`newer`（已安裝的版本比較新）、`legacy`、`not_installed`。`--json` 一律不問問題，舊名稱沒有 `--yes` 就留著（`legacy_kept`）。

手動安裝（備案，例如拿不到 `kumi` 的時候）：把 repo 的 `skills/kumi`、`skills/kumi-planning` 兩個資料夾複製到上面的位置，但這樣沒有標記，之後 `kumi skill update` 不會更新它們。

```bash
cp -R skills/kumi skills/kumi-planning ~/.claude/skills/
# 或只給某個專案用
cp -R skills/kumi skills/kumi-planning <專案>/.claude/skills/
```

其他代理沒有 skill 機制時，在它的說明檔（例如 `AGENTS.md`）寫「操作 Kumi 時先讀 `skills/kumi/SKILL.md`；要規劃結構、記需求、看進度時再讀 `skills/kumi-planning/SKILL.md`」。

權杖由使用者自己用 `kumi auth login` 登入，不要貼給代理；範圍只勾要用的，例如只要它看進度就給 `items:read`。這個 repo 自己不會載入這兩份 skill（不在 `.claude/skills/` 底下），在這裡開發 Kumi 時不會被它們干擾。

裝了 skill 不影響本機代理（`kumi agent start`）：它跑引擎時不載入使用者與專案的 skill（Claude Code 只讀 `project` 設定、啟動事件的 skills 要是空的；Codex 關掉 `skills.include_instructions`），工作目錄也是空的暫存資料夾。
