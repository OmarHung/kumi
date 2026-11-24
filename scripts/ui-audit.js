// 量目前這一頁的版面指標，給 docs/DESIGN.md 第 14 節的驗收用。
// 用法：chrome-devtools 的 evaluate_script，sourcePath 指到這個檔案（先登入、開到要量的頁面、設好寬度）。
// 可以一次量好幾頁：先用另一次 evaluate_script 設 window.__UI_AUDIT_ROUTES = ['/my-work', '/projects/AGY', …]，
// 再跑這個檔案；沒設就只量目前這一頁。
// 只讀畫面，不送任何會改資料的請求。
async () => {
  const ROUTES = Array.isArray(window.__UI_AUDIT_ROUTES) ? window.__UI_AUDIT_ROUTES : [];
  const SETTLE_MS = 1200;
  const MIN_TARGET = 24;
  const TOUCH_TARGET = 44;
  const INTERACTIVE =
    'a[href], button, select, input, textarea, [role="button"], [role="menuitem"], [role="tab"], [role="checkbox"]';
  // 還沒標 data-content-start 的頁面，退而求其次找第一列資料、第一張卡片或第一個區塊標題。
  const CONTENT_FALLBACK = 'tbody tr, ol > li, ul > li, h2';

  const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

  function isVisible(element) {
    const rect = element.getBoundingClientRect();
    // 整個在畫面上方之外的（跳到主要內容的連結，沒取得焦點時移出畫面）不算。
    const isAboveViewport = rect.bottom <= 0;
    return rect.width > 0 && rect.height > 0 && !isAboveViewport && getComputedStyle(element).visibility !== 'hidden';
  }

  // 內文裡的連結不算觸控目標（WCAG 2.5.8 的行內例外）。
  function isInlineLink(element) {
    return element.tagName === 'A' && getComputedStyle(element).display === 'inline';
  }

  // 勾選框與單選鈕的點擊範圍是包住它的 <label>（或用 for 指過來的那一個），量的是那個範圍。
  function hitArea(element) {
    const isToggle = element.tagName === 'INPUT' && (element.type === 'checkbox' || element.type === 'radio');
    if (!isToggle) {
      return element;
    }
    const byFor = element.id ? document.querySelector(`label[for="${CSS.escape(element.id)}"]`) : null;
    return element.closest('label') ?? byFor ?? element;
  }

  function nameOf(element) {
    const text = element.getAttribute('aria-label') ?? element.textContent ?? '';
    return `${element.tagName.toLowerCase()}:${text.trim().slice(0, 12)}`;
  }

  function measure() {
    const viewport = window.visualViewport;
    const height = Math.round(viewport?.height ?? window.innerHeight);
    const main = document.querySelector('main') ?? document.body;
    // 麵包屑也是清單，不算內容。
    const fallback = [...main.querySelectorAll(CONTENT_FALLBACK)].find((element) => !element.closest('nav'));
    const start = main.querySelector('[data-content-start]') ?? fallback ?? null;
    const startTop = start ? Math.round(start.getBoundingClientRect().top + window.scrollY) : null;
    const targets = [...document.querySelectorAll(INTERACTIVE)].filter(
      (element) => isVisible(element) && !isInlineLink(element),
    );
    const sizes = targets.map((element) => ({ element, rect: hitArea(element).getBoundingClientRect() }));
    const tooSmall = sizes.filter(({ rect }) => rect.width < MIN_TARGET || rect.height < MIN_TARGET);
    const belowTouch = sizes.filter(({ rect }) => rect.height < TOUCH_TARGET);
    const root = document.documentElement;
    return {
      path: window.location.pathname + window.location.search,
      viewport: { width: Math.round(viewport?.width ?? window.innerWidth), height },
      // 第一筆內容離頁面頂端多遠，以及佔第一個畫面的幾成：越小越好。
      contentStart: startTop,
      contentStartShare: startTop === null ? null : Number((startTop / height).toFixed(2)),
      contentStartMarked: start?.hasAttribute('data-content-start') ?? false,
      pageHeightInScreens: Number((root.scrollHeight / height).toFixed(1)),
      // 整頁不該橫向捲動。
      horizontalOverflow: root.scrollWidth > root.clientWidth,
      targets: targets.length,
      targetsUnder24: tooSmall.length,
      targetsUnder44High: belowTouch.length,
      smallest: tooSmall.slice(0, 5).map(({ element }) => nameOf(element)),
    };
  }

  if (ROUTES.length === 0) {
    return measure();
  }
  const results = [];
  for (const route of ROUTES) {
    // 站內換頁：React Router 聽 popstate，不必重新載入整個應用程式。
    window.history.pushState(null, '', route);
    window.dispatchEvent(new PopStateEvent('popstate'));
    await wait(SETTLE_MS);
    window.scrollTo(0, 0);
    results.push(measure());
  }
  return results;
}
