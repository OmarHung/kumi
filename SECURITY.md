# 安全性政策

## 回報弱點

請不要開公開的 issue 或 Pull Request 來回報弱點。

- 優先用 GitHub 的私下回報：到這個 repo 的 **Security → Advisories → Report a vulnerability**（<https://github.com/OmarHung/kumi/security/advisories/new>）。內容只有維護者看得到。
- 不方便用 GitHub 的話，寄到 omar40276@gmail.com。

請附上：

- 受影響的版本或 commit、部署的方式（compose、VM、Cloud Run…）；
- 重現的步驟，或能證明問題存在的最小範例；
- 你評估的影響（誰、在什麼條件下、能做到什麼）。

修正發布之前請不要公開細節。修正之後會在 Security Advisory 裡說明，並在你同意的情況下列出你的名字。

## 支援的版本

只有 `main` 上最新的版本會收到安全性修正。自己架的請跟著更新，步驟在 [docs/OPERATIONS.md](docs/OPERATIONS.md) 與 [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)。

## 範圍

Kumi 本身的程式（伺服器、網頁、命令列工具 `kumi`、MCP 伺服器）都算。第三方套件的弱點請回報給上游；如果它在 Kumi 裡造成實際可利用的問題，也歡迎告訴我們。
