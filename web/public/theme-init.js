// 使用者手動選的主題，在畫面出現之前設到 <html data-theme>，載入時才不會先閃一下另一個主題（docs/DESIGN.md 第 12 節）。
// 內容安全政策不允許行內腳本，所以是獨立的檔案，在 index.html 的 <head> 用一般的 <script src> 載入（不是 module：要比應用程式先執行）。
// 鍵名與可選的值要和 src/lib/theme.ts 一致（src/lib/themeInit.test.ts 會檢查）。
(() => {
  try {
    const choice = globalThis.localStorage.getItem('shell.theme');
    if (choice === 'light' || choice === 'dark') {
      globalThis.document.documentElement.setAttribute('data-theme', choice);
    }
  } catch {
    // 讀不到（私密瀏覽、被擋下）就跟著系統：只是個人偏好，不值得讓頁面出錯。
  }
})();
