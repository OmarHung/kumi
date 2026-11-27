# 貢獻 Kumi

謝謝你願意幫忙。送出之前請先讀完「授權」這一節：它決定你的貢獻之後可以怎麼被使用。

## 授權

Kumi 以 [Kumi License](LICENSE) 提供：Apache License 2.0 加上附加條件。它不是 OSI 定義的開源授權，對外提供代管服務或嵌進要賣的產品需要另外的商業授權。

送出貢獻（Pull Request、修補檔、issue、信件或其他任何形式）就表示你同意 LICENSE 的條件 6：

- 你聲明並保證：你有權利送出這份貢獻；就你所知它沒有侵害別人的著作權、專利或營業秘密；在職務上、用雇主的資源或依合約做的，你已經取得雇主或對方的書面同意。
- 你授權授權人永久、全球、非專屬、免權利金地使用這份貢獻：重製、修改、散布、用在任何合法的用途，也可以用別的條款（包括商業授權與不要求 Kumi 品牌的授權）再授權給別人，不需要另外給你報酬。
- 著作權還是你的：這是授權，不是轉讓。你有權授權的專利也一併授權。
- 對其他人，貢獻以整份 Kumi License 授權，不是只以其中的 Apache License 2.0（條件 7(b)）。
- 授權人可以把之後版本的授權條款調嚴或調鬆；已經發布的版本維持發布當時的條款。
- 授權人沒有義務接受、合併或發布任何貢獻。
- 不想讓某一次送出的內容算作貢獻，請明確標示「Not a Contribution」。

這裡是摘要，條文以英文的 [LICENSE](LICENSE) 為準。不同意的話請不要送出貢獻。

不要貼進別的專案的程式碼。真的需要時，先在 issue 裡說明來源與它的授權。

## 先談再做

- 錯誤修正、錯字、文件的小修改：直接送 Pull Request。
- 新功能、資料模型的修改、對外合約的修改（API、CLI 的輸出、藍圖的 Schema、`IssueCodes` 的代碼）：先開 issue 說明要解決的問題與做法，有共識再動手。做完才發現方向不合，兩邊都白費工夫。

issue 與 Pull Request 用繁體中文或英文都可以。

## 環境

- .NET SDK 10.0.201 以上（版本釘在 `global.json`）
- Node.js 24 以上
- Docker：身分、工作引擎與 API 的測試會用 Testcontainers 起真的 PostgreSQL

在本機把整套跑起來的做法見 [README](README.md) 的「啟動網頁版」。

## 送出前要過的檢查

在 repo 根目錄：

```
dotnet build
dotnet test /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line
bash scripts/precommit-scan.sh
```

改了網頁，在 `web/` 底下：

```
npm run typecheck
npm run lint
npm run test:coverage
npm run build
```

`precommit-scan.sh` 前面幾項有輸出不一定是錯，逐項看過再決定；最後一項（權杖、私鑰、私有網段的位址、本機路徑這類樣式）命中就是失敗：結束代碼 1 是有命中、2 是掃描本身出錯（沒有 perl、允許清單格式錯），都不是 0。不帶參數掃暫存區加上工作目錄尚未暫存的修改；選項：`--staged` 只掃暫存區（hook 用）、`--worktree` 只掃工作目錄、`--all` 掃全部追蹤的檔案、`--diff <基準> [<頂端>]` 掃從基準到頂端（預設 HEAD）的差異與範圍內每個提交新增的行、提交訊息（CI 與 pre-push 用的）。另外可以用環境變數 `KUMI_SCAN_PRIVATE_TERMS` 指一份 repo 外的名單（一行一個詞），擋掉樣式認不出來的真實名稱；沒有這份名單時略過。測試與文件裡的假值要放進 `scripts/precommit-scan-allowlist.txt`（路徑、樣式名稱、值的正規式，格式在檔頭）。

想每次提交都自動跑，裝成 git 的 pre-commit hook（在主工作目錄執行；worktree 共用主目錄的 hook。用符號連結，不要改 `core.hooksPath`，否則 `.git/hooks/` 裡其他的 hook 會整個失效）：

```
ln -s ../../scripts/hooks/pre-commit .git/hooks/pre-commit
```

推之前再掃一次要推的每一個提交，裝成 pre-push hook（已經有自己的 pre-push 時，在裡面呼叫它，並把 git 給的標準輸入轉給它）：

```
ln -s ../../scripts/hooks/pre-push .git/hooks/pre-push
```

## 規矩

完整的規矩在 [CLAUDE.md](CLAUDE.md) 的「架構」與「規矩」兩節。最常被退回的是這幾項：

- **先寫測試再寫實作。** 測試用 NUnit 的 `Assert.That`，名稱描述行為，結構是 Arrange／Act／Assert。測試透過公開介面觀察行為，不去查資料表。
- **警告視為錯誤。** 不要用 `#pragma warning disable`、`NoWarn`、`eslint-disable`、`@ts-ignore` 把警告關掉。
- **不可變。** 模型用 record 與 init 屬性，對外的集合用 `IReadOnlyList<T>`。
- **模組的界線。** 模組之間只透過 `*.Contracts` 的公開介面互相使用，不建跨模組的外鍵；權限檢查在模組裡做。
- **合約要一路改到底。** 合約加了屬性，模組、API 的 JSON 對應、API 的形狀測試、網頁的型別與假資料要一起改。
- **畫面照設計規範。** 新增或修改畫面前先讀 [docs/DESIGN.md](docs/DESIGN.md)；要偏離或擴充，在同一個 Pull Request 裡改規範並寫出理由。
- **給使用者看的訊息用繁體中文**，註解只寫「為什麼這樣做」。
- **不能出現真實的個人資料或任何公司的內部資訊。** 範例一律用 `example` 之類的假名。

### 新增套件

Kumi 同時以商業授權提供，所以相依的套件只收寬鬆授權：MIT、Apache-2.0、BSD、ISC 這一類。GPL、AGPL、LGPL 與帶商業使用限制的不收。版本集中在 `Directory.Packages.props`，在那裡用註解寫出套件的授權。

加了、升了或拿掉套件（NuGet 或 npm）之後，重新產生第三方聲明，和你的修改一起送出：

```
node scripts/licenses/third-party-notices.mjs
```

套件自己沒附授權檔時這支腳本會失敗，並告訴你要把條文補在哪裡。

這些套件不用：JsonSchema.Net、MassTransit v9、MediatR、AutoMapper、FluentAssertions v8、Duende IdentityServer。

## 提交訊息

```
<type>: <繁體中文說明>
```

`type` 用 feat、fix、refactor、docs、test、chore、perf、ci。一個提交做一件事。

## 回報安全性問題

不要開公開的 issue。優先用 GitHub 的私下回報（這個 repo 的 Security → Advisories → Report a vulnerability），不方便的話寄到 omar40276@gmail.com；寫出受影響的版本、重現的步驟與你評估的影響，修正發布之前請不要公開細節。完整的說明在 [SECURITY.md](SECURITY.md)。
