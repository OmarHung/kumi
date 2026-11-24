# 設計規範 — 第 2 節：設計變數

> `docs/DESIGN.md`（設計規範）的一部分；索引、通則與其他各節在那裡。節號是整份規範共用的：別的地方寫「第 2 節」「2.1」「2.2」指的就是這一份。

## 2. 設計變數

`web/src/styles/tokens.css` 是唯一的來源。元件的 CSS **一定**只用變數，不直接寫顏色、字級、圓角、陰影、層級與動畫時間。

### 2.1 內容〔已有〕

用 `light-dark()`：一個變數一行、淺色與深色並排，使用者也能手動切換（`data-theme`）。建置的 CSS 目標一定要是第 12 節的瀏覽器（`vite.config.ts` 的 `build.cssTarget`）：目標比較舊時，建置會把 `light-dark()` 改寫成模擬的變數。下面是 2026-11-17 視覺翻新之後的內容（和原本相比：強調色換成紫藍、中性色帶一點紫藍、字型自帶、圓角 6／8／12／16 加上 `--radius-xl`、`--weight-bold`、`--color-focus-ring`、實心的底用的 `--color-accent-solid`／`--color-danger-solid`（`--color-accent-contrast` 兩種模式都是白色）、`--color-surface-raised`、狀態類別各有字色與淡底、頭像的六種色調、藍圖選項的藍與紫、陰影帶色相並多了 `--shadow-raised-hover`；理由見 2.2）。2026-11-23 品牌定名 Kumi 時，紙、桌面、側欄與中性色換成藤灰（淺色）與鐵紺（深色），強調色不變，深色的淡底跟著調亮（理由見 2.2 最後一條）。

```css
/*
 * 設計變數（docs/DESIGN.md 第 2 節）。元件只用這裡的變數，不直接寫顏色與尺寸；換主題或調密度時只改這一份。
 * 方向是「明亮的工作室」（第 1 節）：年輕但不失專業——紫藍的強調色、圓一點的角、柔和的陰影、友善的字型；
 * 顏色仍然只拿來表達意義（互動、狀態、危險），沒有裝飾用的色條。
 * 顏色用 light-dark()：淺色與深色並排寫在同一行，使用者手動選的主題（<html data-theme>）蓋過系統設定。
 */
:root {
  color-scheme: light dark;

  /*
   * 字型：拉丁字母與數字用自帶的 Plus Jakarta Sans（main.tsx 匯入 @fontsource-variable，檔案和網站同網域，
   * 內容安全政策 default-src 'self' 允許）；中文沒有自帶（一套繁中字型好幾 MB），落到系統的台灣字體。
   * 等寬字（編號、代號）用自帶的 Geist Mono。
   */
  --font-sans: 'Plus Jakarta Sans Variable', system-ui, -apple-system, 'Segoe UI', 'PingFang TC', 'Noto Sans TC',
    'Microsoft JhengHei', sans-serif;
  --font-mono: 'Geist Mono Variable', ui-monospace, 'SF Mono', SFMono-Regular, Menlo, Consolas, monospace;

  --text-xs: 0.75rem; /* 12 */
  --text-sm: 0.8125rem; /* 13 */
  --text-base: 0.875rem; /* 14 */
  --text-md: 1rem; /* 16 */
  --text-lg: 1.25rem; /* 20 */
  --text-xl: 1.5rem; /* 24 */
  --text-control: var(--text-base); /* 輸入框裡的字，觸控時換成 16px */

  --leading-tight: 1.3;
  --leading-normal: 1.55;
  --leading-relaxed: 1.65; /* 長段文字：項目的說明與留言的內文 */
  --weight-regular: 400;
  --weight-medium: 500;
  --weight-semibold: 600;
  --weight-bold: 700; /* 只給頁面與內容區的大標題 */

  --space-0-5: 0.125rem; /* 2px，緊貼的小間隙 */
  --space-1: 0.25rem;
  --space-2: 0.5rem;
  --space-3: 0.75rem;
  --space-4: 1rem;
  --space-5: 1.25rem;
  --space-6: 1.5rem;
  --space-8: 2rem;
  --space-10: 2.5rem;
  --space-12: 3rem;

  --radius-sm: 6px; /* 外框裡面的內層項目（選單項目、分段切換的一格）、勾選框、小方塊 */
  --radius-md: 8px; /* 按鈕、輸入框、選單與浮層的外框 */
  --radius-lg: 12px; /* 看板的卡片、區塊的卡片、面板 */
  --radius-xl: 16px; /* 對話框、看板的欄 */
  --radius-pill: 999px; /* 標籤、頭像、計數 */

  --control-height: 2rem;
  --control-height-sm: 1.75rem;
  --row-height: 2.25rem; /* 表格與清單一列的高度 */
  --target-min: 1.5rem; /* 任何可以點的東西的最小邊長 */
  --scrollbar-size: 0.625rem; /* 捲軸的寬（直向）與高（橫向）：把手兩邊各讓出 2px，看得到的把手是 6px 的膠囊 */
  --page-bar-height: 3rem;
  --page-bar-inset: 0rem; /* 頁列開頭讓出的寬度：外框（AppShell）把收合側欄與「選單」的按鈕疊在那裡時設定 */
  --page-gutter: var(--space-6); /* 內容區左右的留白 */
  --sidebar-width: 14rem;
  --lane-width: 17.5rem;
  --side-panel-width: 28rem; /* 清單旁邊的面板（SidePanel）的預設寬度：使用者可以拖拉調整（第 7 節），點兩下把手回到這個寬度 */
  --content-max: 72rem;
  --reading-max: 46rem; /* 長段文字（說明、留言）一行的最大寬度 */
  --assistant-button-size: 3rem; /* 右下角的 AI 助理按鈕：新通知的提示讓出這個高度 */
  --dialog-width: 30rem;
  --dialog-width-lg: 40rem; /* 有表格的對話框 */
  --dialog-width-item: 64rem; /* 項目的浮動視窗最少這麼寬（畫面放得下時）：欄位排在右邊一欄的版面 */
  --dialog-width-item-max: 100rem; /* 項目的浮動視窗預設最寬（畫面的 85% 超過時；使用者可以再拉寬，第 7 節 FloatingPanel） */

  --duration-fast: 120ms;
  --duration-normal: 200ms;
  --duration-spin: 0.8s; /* 載入指示轉一圈：唯一允許無限循環的動態 */
  --duration-highlight: 1.4s; /* 剛移好的卡片閃一下：要慢到看得出落在哪裡 */
  --duration-target: 2.4s; /* 點通知後被標出來的那一筆留言淡出：和 features/item-page/commentTarget.ts 的 HIGHLIGHT_MS 同一個數字 */
  --duration-pulse: 1.6s; /* 骨架明暗變化一次：慢到不搶眼，看得出還在載入 */
  --ease-out: cubic-bezier(0.2, 0.8, 0.2, 1);

  /* 層級由低到高。浮層在對話框上面：對話框裡也可以開選單。 */
  --z-sticky: 5; /* 黏住的表頭、工具列 */
  --z-topbar: 10; /* 外框疊在頁列左上角的按鈕（收合側欄、窄螢幕的「選單」），在黏住的頁列上面 */
  --z-drawer: 20;
  --z-assistant: 30; /* 右下角的 AI 助理按鈕與浮動面板：不是對話框（背後照樣能操作），在抽屜與項目的側邊面板上面、對話框下面 */
  --z-dialog: 40;
  --z-popover: 50; /* 選單、浮層 */

  /*
   * 中性色帶一點紫藍（和強調色同一個色相），整體不會冷冰冰的灰。
   * 紙與桌面明暗都不用純白與近黑：淺色是帶紫的藤灰，深色是深藍紫的鐵紺（docs/design/02-tokens.md 2.2）。
   */
  --color-canvas: light-dark(#eeedf5, #1c1d2e);
  --color-surface: light-dark(#f7f6fb, #232438);
  /* 浮在紙上的小塊（分段切換目前的那一格、看板的卡片）：淺色就是紙；深色比紙亮一階，黑色的陰影看不到，要靠亮度分出來。 */
  --color-surface-raised: light-dark(#f7f6fb, #2c2e45);
  --color-surface-sunken: light-dark(#e7e5f0, #17182a);
  --color-sidebar: light-dark(#ebe9f3, #1f2033);
  --color-hover: light-dark(rgb(23 23 42 / 5%), rgb(236 236 244 / 6%));
  --color-selected: var(--color-accent-soft); /* 選取的列、目前的導覽項目（反白的選項是實心的 --color-accent） */
  --color-border: light-dark(#dddbe8, #333552);
  --color-border-strong: light-dark(#c6c3d6, #45486a);
  --color-control-border: light-dark(#76798f, #7c80a3); /* 輸入框與次要按鈕的外框：對比 3:1 以上 */
  --color-scrollbar-thumb: light-dark(rgb(23 23 42 / 30%), rgb(236 236 244 / 28%)); /* 捲軸的把手：半透明的字色，在紙、側欄、凹下去的底上都一樣 */
  --color-scrollbar-thumb-hover: light-dark(rgb(23 23 42 / 50%), rgb(236 236 244 / 45%)); /* 滑過與拖曳中的把手：對底 3:1 以上 */
  --color-text: light-dark(#17172a, #ececf4);
  --color-text-muted: light-dark(#565a71, #aaadc6);
  --color-accent: light-dark(#5a4ae3, #a99eff);
  --color-accent-hover: light-dark(#4837d0, #c2baff);
  --color-accent-soft: light-dark(#e6e2ff, #322d63);
  /*
   * 實心的強調色（主要按鈕、反白的選項、勾選的方框、選的日期）：深色模式不用 --color-accent 的淡紫，
   * 否則實心按鈕像糖果；用飽和的紫藍配白字。--color-accent 留給連結、文字、焦點外框這些「線與字」。
   */
  --color-accent-solid: light-dark(#5a4ae3, #6354e8);
  --color-accent-solid-hover: light-dark(#4837d0, #5546d9);
  --color-accent-contrast: #ffffff; /* 實心強調色、實心危險色上的字 */
  --color-focus: var(--color-accent);
  /* 輸入框、下拉取得焦點時外框外面的一圈光暈（3px，第 2.2 節允許的焦點寬度）：外框本身換成 --color-focus。 */
  --color-focus-ring: light-dark(rgb(90 74 227 / 22%), rgb(169 158 255 / 30%));
  --color-danger: light-dark(#cc2a1f, #ff8b7c);
  --color-danger-hover: light-dark(#a81f16, #ffaca1);
  --color-danger-soft: light-dark(#fff0ee, #4e211e);
  --color-danger-solid: light-dark(#cc2a1f, #c9372c); /* 實心的危險按鈕、危險選單項目的反白；理由同 --color-accent-solid */
  --color-danger-solid-hover: light-dark(#a81f16, #b02d23);
  --color-warning: light-dark(#9c5100, #f5b553);
  --color-warning-soft: light-dark(#fff0db, #39342a);
  --color-success: light-dark(#0f7a4e, #5ed39b);
  --color-success-soft: light-dark(#e2f5eb, #153c26);
  --color-neutral-soft: light-dark(#e0deeb, #2d303d);
  --color-overlay: light-dark(rgb(15 16 30 / 40%), rgb(0 0 0 / 60%));

  /*
   * 狀態類別。進行中是自己的藍（不再借強調色）：紫藍是「可以按」，藍是「正在做」，綠是「做完了」，
   * 一眼分得出互動與狀態。每個類別有字色與淡底（標籤用）。
   */
  --color-state-idle: var(--color-text-muted); /* 待規劃、未開始、已取消 */
  --color-state-idle-soft: var(--color-neutral-soft);
  --color-state-active: light-dark(#1a5fd0, #6fa8ff); /* 進行中 */
  --color-state-active-soft: light-dark(#e8f0fe, #1d345c);
  --color-state-done: var(--color-success); /* 完成 */
  --color-state-done-soft: var(--color-success-soft);

  /*
   * 頭像的六種色調：照名稱算出固定的一種（components/Avatar），同一個人在哪裡都是同一個顏色，
   * 一眼認得出是誰。字色對底色都在 4.5:1 以上（第 2.2 節）。
   */
  --color-avatar-1: light-dark(#4b3cc9, #c3bbff);
  --color-avatar-1-soft: light-dark(#ece9ff, #36306a);
  --color-avatar-2: light-dark(#1b57c0, #93bdff);
  --color-avatar-2-soft: light-dark(#e3eeff, #1c3057);
  --color-avatar-3: light-dark(#0d6b62, #6fd6c8);
  --color-avatar-3-soft: light-dark(#dcf4f1, #163d39);
  --color-avatar-4: light-dark(#1d6b37, #86d99e);
  --color-avatar-4-soft: light-dark(#e1f5e7, #1a3c23);
  --color-avatar-5: light-dark(#8a4b00, #f3c26b);
  --color-avatar-5-soft: light-dark(#fdf0d7, #413013);
  --color-avatar-6: light-dark(#a8285f, #ff9cc7);
  --color-avatar-6-soft: light-dark(#fde7f1, #4b2237);

  /*
   * 藍圖選項的顏色（單選、多選欄位的標籤）：藍與紫各有自己的變數。強調色換成紫藍之後，借它來畫「藍」的選項會變成紫色，
   * 藍與紫也分不出來。其他顏色（紅、橙、黃、綠、灰）照舊對到語意色的標籤。
   */
  --color-option-blue: light-dark(#1d5fc4, #7fb2ff);
  --color-option-blue-soft: light-dark(#e5efff, #1b3052);
  --color-option-purple: light-dark(#7a3fc2, #cfa6ff);
  --color-option-purple-soft: light-dark(#f2e9fd, #392858);

  /* 陰影帶一點強調色的色相、比較柔：浮起來的東西看起來輕，不是一塊黑影。 */
  --shadow-raised:
    0 1px 2px light-dark(rgb(23 23 42 / 6%), rgb(0 0 0 / 40%)),
    0 1px 1px light-dark(rgb(23 23 42 / 3%), rgb(0 0 0 / 20%));
  --shadow-raised-hover: /* 滑過看板的卡片：浮起來一點，看得出拿得起來 */
    0 4px 12px light-dark(rgb(23 23 42 / 8%), rgb(0 0 0 / 45%)),
    0 1px 2px light-dark(rgb(23 23 42 / 6%), rgb(0 0 0 / 30%));
  --shadow-popover:
    0 12px 32px -4px light-dark(rgb(23 23 42 / 14%), rgb(0 0 0 / 55%)),
    0 2px 6px light-dark(rgb(23 23 42 / 6%), rgb(0 0 0 / 30%));
  --shadow-dialog:
    0 24px 64px -12px light-dark(rgb(23 23 42 / 26%), rgb(0 0 0 / 65%)),
    0 4px 12px light-dark(rgb(23 23 42 / 8%), rgb(0 0 0 / 30%));
}

/* 觸控：同一份畫面，目標變大、輸入框的字不小於 16px（否則 iOS 聚焦時會放大整頁）。 */
@media (pointer: coarse) {
  :root {
    --control-height: 2.75rem;
    --control-height-sm: 2.75rem;
    --row-height: 2.75rem;
    --target-min: 2.75rem;
    --text-control: 1rem;
  }
}

@media (max-width: 48rem) {
  :root {
    --page-gutter: var(--space-4);
  }
}

/* 使用者手動選的主題蓋過系統設定；沒選就跟著系統。 */
:root[data-theme='light'] {
  color-scheme: light;
}

:root[data-theme='dark'] {
  color-scheme: dark;
}
```

### 2.2 規則

- 新的變數**一定**先加在 `tokens.css` 與本節，再拿去用。一個變數只有一個意思：不要因為「顏色剛好一樣」借用別的語意（例如拿 `--color-success` 當「新增」的顏色）。
- 允許直接寫的數值只有：`0`、`1px`（細線）、`2px`／`3px`（焦點外框、輸入框焦點的光暈、編輯中格子的框）、百分比、`1fr` 之類的格線單位，以及寫在該元件 CSS 最上面、用區域變數命名的固定寬度（例如 `--item-key-width: 6.5rem`）。從變數算出來的區域變數（勾選框的圓角 `--checkbox-radius: calc(var(--radius-sm) - 2px)`）可以，理由寫在註解裡：變數改了它跟著變，不是換個名字藏起寫死的值。
- 顏色對比（已經算過，改色時**一定**重算）：

  | 組合 | 淺色 | 深色 | 門檻 |
  |---|---|---|---|
  | `text` 對 `surface`／`canvas` | 16.4／15.2 | 12.9／14.1 | 4.5 |
  | `text`、`text-muted` 對 `surface-raised`（分段切換目前的那一格） | 16.4、6.3 | 11.3、6.0 | 4.5 |
  | `text-muted` 對 `surface`／`canvas`／`surface-sunken`／`sidebar` | 6.3／5.8／5.5／5.7 | 6.9／7.5／7.9／7.2 | 4.5 |
  | `text-muted` 對 `neutral-soft`（中性的標籤、計數、沒有色調的頭像、資訊提示的圖示） | 5.11 | 5.93 | 4.5 |
  | `text` 對 `neutral-soft`（資訊提示的標題與內文） | 13.3 | 11.2 | 4.5 |
  | `text-muted` 對 `warning-soft` | 6.05 | 5.59 | 4.5 |
  | `text-muted` 對 `hover`（疊在紙上：不能選的選項反白時） | 5.7 | 5.9 | 4.5 |
  | `danger` 對 `surface`（欄位的錯誤、危險的選單項目） | 5.0 | 6.7 | 4.5 |
  | `accent` 對 `surface`／`accent-soft`（＝`selected`）／`sidebar`／`canvas` | 5.5／4.74／5.0／5.1 | 6.5／5.3／6.9／7.1 | 4.5 |
  | `danger`、`warning`、`success` 對各自的 `-soft`（提示的圖示與標題、標籤） | 4.84／5.22／4.73 | 5.92／6.84／6.60 | 4.5 |
  | `option-blue`、`option-purple` 對各自的 `-soft`（藍圖選項的標籤） | 5.20／5.38 | 6.11／6.52 | 4.5 |
  | `state-active` 對 `state-active-soft`／`surface` | 5.1／5.4 | 5.1／6.3 | 4.5 |
  | `accent-contrast`（白）對 `accent-solid`／`accent-solid-hover`（主要按鈕、反白的選項、勾選的方框、日曆選的那一天） | 5.96／7.68 | 5.30／6.43 | 4.5 |
  | `accent-contrast`（白）對 `danger-solid`／`danger-solid-hover`（危險的按鈕、危險選單項目的反白） | 5.36／7.31 | 5.16／6.47 | 4.5 |
  | `control-border` 對 `surface`／`canvas`／`surface-sunken`／`sidebar` | 4.0／3.7／3.4／3.6 | 4.0／4.3／4.6／4.2 | 3.0 |
  | `control-border` 對 `neutral-soft`／`warning-soft`（淡底上的控制項） | 3.23／3.82 | 3.42／3.22 | 3.0 |
  | 頭像六種色調（`avatar-N` 對 `avatar-N-soft`） | 6.3／5.7／5.5／5.7／6.0／5.7 | 6.6／6.8／6.9／7.3／7.7／6.8 | 4.5 |
  | `focus`（焦點的外框）對 `surface` | 5.5 | 6.5 | 3.0 |
  | `focus-ring`（疊在紙上的光暈）對 `surface` | 1.4 | 1.8 | 輔助：不單獨表示焦點 |
  | `border-strong` 對 `surface` | 1.6 | 1.7 | 不能當控制項的外框 |
  | `scrollbar-thumb`／`scrollbar-thumb-hover` 對 `surface`（側欄、凹下去的底、桌面差不到 0.1） | 1.9／3.3 | 2.3／3.8 | 滑過時 3.0 |

- `--color-border` 與 `--color-border-strong` 只用在分隔線與裝飾（對比不到 3:1）。**輸入框、下拉選單、勾選框、次要按鈕的外框一定用 `--color-control-border`**：外框是使用者認出「這裡可以輸入」的唯一線索。
- `--color-focus-ring` 是輸入框、多行文字框、`Select` 的按鈕取得焦點時外框外面那一圈 3px 的光暈（第 9 節）。它淡（對紙 1.4／1.8），只是讓焦點在一整排欄位裡好找；焦點本身靠外框換成 2px 的 `--color-focus`（1px 的外框加 1px 同色的內環線，對紙 6.0／7.6；和原本全域的焦點外框一樣粗，只換外框色的話 1px 對平常的灰框只有 1.6:1，太難看出來）表示，強制色彩時靠透明的 `outline`。**不要**把它當成唯一的焦點線索用在別的元件上。
- 頭像的色調（`--color-avatar-1`～`6` 與各自的 `-soft`）只用在 `Avatar`：照名稱算出固定的一種（第 4 節），不拿來表示狀態或分類。改色時六組的字對底都要重算；淡底也不要和別的變數同色（深色的 `avatar-2-soft` 原本和 `state-active-soft` 一模一樣，進行中的標籤旁邊的頭像看起來像同一塊）。
- **實心的底一定用 `-solid`**（`--color-accent-solid`、`--color-danger-solid` 與它們的 `-hover`）：主要與危險的按鈕、`Select`／`Menu` 的反白、危險項目的反白、勾選的方框、日曆選的那一天；上面的字是 `--color-accent-contrast`（兩種模式都是白色）。`--color-accent`、`--color-danger` 留給線與字（連結、焦點外框、今天的環線、錯誤的字）。理由：深色的 `--color-accent` 是淡紫（#a99eff，當字和線對深色的紙夠亮），當實心的底像糖果、只能配深色字。
- `--color-surface-raised` 是浮在紙上的小塊（分段切換目前的那一格）：淺色就是紙；深色比紙亮一階——深色模式的陰影看不到，紙和頁面又同色，不亮一階看不出浮起來。
- 淺色的 `--color-neutral-soft` 是 #e0deeb：要比紙（#f7f6fb）深一階，放在凹下去的底上（看板欄頂「待辦」的標籤）才看得出是一個膠囊（更早的版本比 #e6e8f0 還淡，就是這樣看不出來）。深色的 `--color-warning-soft` 是 #39342a：原本偏橄欖褐，看起來髒。
- **紙與桌面明暗都不用純白與近黑**（2026-11-23，使用者要求；品牌定名 Kumi 的同一輪）：淺色是帶紫的藤灰（紙 #f7f6fb、桌面 #eeedf5、側欄 #ebe9f3），深色是深藍紫的鐵紺（紙 #232438、桌面 #1c1d2e、側欄 #1f2033），強調色、狀態色、危險、警告、成功的字色都不變。深色的淡底（`-soft`：選取、中性、危險、警告、成功、進行中、頭像、藍圖選項）**一定**比紙亮一階：原本是照近黑的紙調的，紙變亮以後原樣會比紙暗、看起來像凹洞，所以保持色相與飽和度、把亮度調到和紙的亮度比與原本一樣（1.13～1.29）。之後再改深色的紙，淡底照同樣的比例重算。淺色的選取色換成 #e6e2ff（原本 #efedff 放在藤灰的側欄上幾乎看不出來）；強調色放在它上面是 4.74，是這一輪最接近門檻的一組。
- **捲軸**〔2026-11-24，使用者要「全域的 scrollbar 改成客製的」：側欄的專案清單出現作業系統那條灰色的捲軸，和紙、桌面的色調不搭〕：全站的捲軸在 `global.css` 一份。Chrome、Edge、Safari 用 `::-webkit-scrollbar`：寬（橫向是高）`--scrollbar-size`，軌道與角落透明，把手是 `--radius-pill` 的膠囊、`--color-scrollbar-thumb`，兩邊用透明的框線讓出 `--space-0-5`（`background-clip: padding-box`），滑過與拖曳中換成 `--color-scrollbar-thumb-hover`。Firefox 沒有 `::-webkit-scrollbar`，改用標準的 `scrollbar-width: thin` 與 `scrollbar-color`（`@supports not selector(::-webkit-scrollbar)`，只給它；Chrome 同時有兩種寫法時標準的會蓋掉 `::-webkit-scrollbar`，就不能是膠囊）。顏色是半透明的字色，不是實心的灰：同一個把手放在紙、側欄、凹下去的底與對話框上看起來一樣深。平常淡一點（對紙約 2:1，看得出位置、不搶內容），要操作時（滑過、拖曳）到 3:1 以上。強制色彩時把手畫成 `CanvasText`、軌道 `Canvas`（不然系統拿掉作者的顏色後把手可能和軌道同色）。觸控的瀏覽器照樣用系統自己浮起來的捲軸（它們不理 `::-webkit-scrollbar`）。
- 藍圖選項的藍與紫有自己的變數（`--color-option-blue`／`-purple` 與 `-soft`），不借強調色：強調色是紫藍，借它畫「藍」會變紫、和「紫」分不出來。
