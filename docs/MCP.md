# 用自己的 Claude Code／Codex 接 Kumi（MCP）

> 2026-11-23 產品定名 Kumi，MCP 伺服器的名稱從 `pm-tool` 改成 `kumi`。已經用舊名稱接好的不必重接，照樣能用；名稱只是你在 Claude Code／Codex 裡怎麼叫它。想換成新名稱時，先 `claude mcp remove pm-tool`（Codex 用 `codex mcp remove pm-tool`），再照下面重新加。

Kumi 提供一個 MCP 伺服器。你電腦上的 Claude Code 或 Codex（用你自己的 Claude 或 ChatGPT 訂閱登入）連過來之後，就能在對話裡直接查專案與項目、建立與修改項目、留言，以及讀專案的設定、預覽並提出變更集。

- **用的是你自己的訂閱額度**，Kumi 不付模型的費用，也不需要組織管理員設定 API 金鑰（那是網頁裡「AI 助理」用的，見 `docs/OPERATIONS.md` 第 15 節）。
- **看得到的、做得到的和你自己一樣**，再受你授權時勾選的範圍限制。
- **變更集只能提出**，要有權限的人在網頁的「變更集」頁核准才會生效；透過 MCP 提出的會標「AI 代理提出」。

## 連線

MCP 的網址是 Kumi 的網址加上 `/mcp`，例如 `https://pm.example.com/mcp`。最省事的是用 `kumi setup`（網頁「設定 → 命令列與整合」最上面的「設定精靈」會給一行指令）：它偵測到 Claude Code、Codex 就幫你加上並開授權頁，下面是想自己來的做法。網頁的「連接 Claude Code／Codex」有這個部署的網址與可以直接複製的指令。

### Claude Code

```bash
claude mcp add --scope user --transport http kumi https://pm.example.com/mcp
claude mcp login kumi
```

第一行加進 Claude Code：`--scope user` 讓所有資料夾都能用（預設的 local 範圍只在執行指令的那個資料夾有效）。第二行（或在 Claude Code 裡執行 `/mcp`，選 `kumi` 登入）會打開瀏覽器到 Kumi 的授權頁（沒登入會先到登入頁），確認是你的帳號、勾選要給的範圍，按「允許」，回到 Claude Code 就連好了。

### Codex

```bash
codex mcp add kumi --url https://pm.example.com/mcp
codex mcp login kumi
```

第一行加進 Codex，第二行打開瀏覽器到 Kumi 的授權頁，按「允許」。

### 不支援 OAuth 的用戶端

到網頁「設定 → 命令列與整合」的「存取權杖」建一把權杖（範圍只勾要用的），用標頭帶：

```bash
claude mcp add --transport http kumi https://pm.example.com/mcp --header "Authorization: Bearer pmt_…"
```

## 在網頁的 AI 助理裡用（本機代理）

上面是「在 Claude Code／Codex 裡用 Kumi」。反過來，想在 Kumi 網頁的 AI 助理（整頁或右下角的浮動對話框）裡用自己電腦上的 Claude Code／Codex，在自己的電腦開著 `kumi agent start`（見 `docs/CLI.md` 的「本機代理」）：模型選單會多出「Claude Code（電腦名稱）」「Codex（電腦名稱）」。用的是那台電腦上登入的 CLI 與你自己的訂閱；它只能用這裡的藍圖工具（讀設定、預覽與提出變更集）與項目工具（`get_item`、`search_items` 讀項目，`propose_item_changes` 建議怎麼改），不能碰你電腦上的檔案。選了項目的對話裡，它讀得到那個項目；要改項目時只能「建議」：卡片出現在網頁的對話裡，你按「套用」才會以你自己的身分寫入。讀項目要權杖有 `items:read`（見下）。組織要在「AI 設定」允許成員使用自己的 AI 帳號。

### `propose_item_changes`（只給本機代理）

本機代理（權杖有 `agent:run`）的每個 `/mcp` 請求都要帶 `X-Kumi-Project` 標頭（那則對話的專案代號），沒帶整個請求拒絕；所有工具只准碰那個專案，別的專案的項目與專案當作不存在，`my_work` 這類跨專案的工具不能用。標頭由 `kumi agent start` 寫進引擎的 MCP 設定，模型改不了，所以項目內容裡夾帶的指令也讓它碰不到別的專案。

本機代理的權杖在伺服器端還有兩層限制，因為它會交給你電腦上的引擎：`/mcp` 只列也只讓它叫本機代理的十個工具（`get_blueprint`、`list_templates`、`get_template`、`describe_usage`、`preview_changeset`、`propose_changeset`、`get_changeset`、`get_item`、`search_items`、`propose_item_changes`），`update_item`、`create_item`、`list_projects` 這些就算權杖另外有範圍也不能用；REST API 上只能領工作、確認自己是誰，以及查詢與下載命令列工具（`GET /v1/cli`、`GET /v1/cli/download/{platform}`），其他端點一律拒絕。新建的權杖不能同時有 `agent:run` 和 `items:write`。一般的權杖（OAuth、個人存取權杖）不受影響。

`propose_item_changes`（參數 `projectKey`、`itemKey`、`changes`（`[{field, value}]`）、`descriptionRevision`、`comment`）只建立「修改建議」，不改項目。它的範圍是 `agent:run`，只有 `kumi agent start` 用的權杖有；OAuth 授權不發這個範圍，`tools/list` 也不會把它列給沒有這個範圍的權杖。一般的 MCP 用戶端要改項目，用 `update_item`、`move_item`、`add_comment`（`items:write`）。

## 授權的範圍

| 範圍 | 授權頁的說法 | 可以 |
|---|---|---|
| `items:read` | 看專案與項目 | 列出專案、找項目、看項目與活動、我的工作 |
| `items:write` | 建立與修改項目、留言 | 建立、修改、改狀態、留言 |
| `blueprint:read` | 看專案的設定與變更集 | 讀藍圖、模板、使用狀況的統計，預覽變更集 |
| `blueprint:write` | 提出變更集（還是要有人核准） | 提出變更集 |

只想讓代理查資料時，授權頁只勾兩個「看」的。範圍之後要改：到「設定 → 命令列與整合」撤銷，再連一次重新勾選。

## 撤銷

網頁「設定 → 命令列與整合」的「已授權的 AI 代理」列出你授權過的應用、範圍與最後使用的時間，按「撤銷」立刻失效；之後那個應用要重新登入授權才能再連。電腦遺失或懷疑外流時先撤銷。

改密碼（自己改或管理員重設）與帳號被停用，都會撤銷你所有已授權的 AI 代理（授權、存取權杖與更新權杖一起），重新啟用帳號也不會讓它們復活，要重新授權。你自己建的個人存取權杖不受改密碼影響（停用與管理員重設才撤銷）。有好幾個 api 程序時，其他程序最多晚 30 秒（工作階段的快取時間）才看到撤銷。「登出其他裝置」只結束網頁的工作階段，不動已授權的 AI 代理。

## 運作方式（給管理員）

- Kumi 自己當 OAuth 2.1 的授權伺服器（MCP 2026-07-28 版的授權規格）：授權碼加 PKCE（S256）、權杖只發給這個部署的 `/mcp`（RFC 8707）、支援 Client ID Metadata Document 與動態註冊、更新權杖每次輪替。
- 存取權杖一小時、更新權杖 30 天（一直在用就一直有效）。OAuth 發的權杖只能用在 `/mcp`，不能拿去打 API 的其他端點。
- 授權伺服器的網址（issuer）是 `https://{Security:PublicHost}`；沒有設 `Security:PublicHost` 時用請求的網址（只適合本機）。上線一定要設，見 `docs/DEPLOYMENT.md`。
- 用戶端（Claude Code、Codex）在使用者的電腦上，授權之後導回本機（`127.0.0.1`）；Kumi 的伺服器要連得到 Client ID Metadata Document 的網址（對外的 443）。
- claude.ai 網頁版的「自訂連接器」要從 Anthropic 的雲端連到 Kumi，有防火牆擋著的部署用不了；用 Claude Code。
