import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest';
import { resetActivityViewForTests } from '../features/item-page/activityView';
import { resetCalendarDateModeForTests } from '../lib/calendarDateMode';
import { resetEditorModeForTests } from '../lib/editorMode';
import { resetToastModeForTests } from '../features/notification-toasts/toastMode';
import { resetTabBadgeForTests } from '../features/inbox/tabBadgePreference';
import { resetItemPanelModeForTests } from '../lib/itemPanelMode';
import { resetPageErrorReporter } from '../lib/errorReporting';
import { server } from './server';

// jsdom 沒有版面：ProseMirror（所見即所得的編輯器）點擊、捲動、算游標位置時會用到這幾個，補成「什麼都量不到」。
const EMPTY_RECT = { x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) };

function defineIfMissing(target: object, name: string, value: unknown): void {
  if (!Reflect.has(target, name)) Object.defineProperty(target, name, { value, configurable: true, writable: true });
}

defineIfMissing(document, 'elementFromPoint', () => null);
defineIfMissing(document, 'elementsFromPoint', () => []);
defineIfMissing(Range.prototype, 'getClientRects', () => Object.assign([], { item: () => null }));
defineIfMissing(Range.prototype, 'getBoundingClientRect', () => EMPTY_RECT);

// findBy*、waitFor 等到 3 秒：慢的機器（CI）上 renderApp 之後第一個 findBy* 常常超過預設的 1 秒。
configure({ asyncUtilTimeout: 3000 });

// React 的重複 key 警告一律讓那個測試失敗：兄弟元件用了同一個 key，換資料時會留下舊的元件（2026-11-21 項目面板的「關注」
// 越換越多），只是一行警告的話測試照樣通過。用 spyOn 記下 console.error 的呼叫（訊息照樣印出來），在 afterEach 才丟：
// 在 console.error 裡丟會打斷 React 自己的流程。每個測試前重新掛上：有的測試會 vi.restoreAllMocks()。
const KEY_WARNING = 'Encountered two children with the same key';
let consoleError = vi.spyOn(console, 'error');

function keyWarningIn(calls: readonly unknown[][]): string | undefined {
  return calls.map((args) => args.map(String).join(' ')).find((message) => message.includes(KEY_WARNING));
}

beforeAll(() => {
  // 沒有宣告的請求一律算失敗：畫面偷偷多打一個 API 時，測試要看得出來。
  server.listen({ onUnhandledFrame: 'error' });
});

beforeEach(() => {
  consoleError = vi.spyOn(console, 'error');
});

afterEach(() => {
  cleanup();
  const keyWarning = keyWarningIn(consoleError.mock.calls);
  consoleError.mockClear();
  server.resetHandlers();
  // 表格記住的欄位設定放在 localStorage，不清掉會影響下一個測試。
  localStorage.clear();
  // 項目時間軸選的那一份（item.activityView）也在 localStorage，上一行一起清掉；寫不進去時的暫存在模組記憶體裡，要另外重設，不能漏到下一個測試。
  resetActivityViewForTests();
  // 行事曆的排法同理。
  resetCalendarDateModeForTests();
  // 項目面板的樣子（側邊面板或浮動視窗）同理。
  resetItemPanelModeForTests();
  // 通知提示的顯示方式同理。
  resetToastModeForTests();
  // 分頁圖示的圓點同理。
  resetTabBadgeForTests();
  // 編輯方式（所見即所得或 Markdown 原文）同理。
  resetEditorModeForTests();
  // 留言與說明的草稿放在 sessionStorage：上一個測試打到一半的字不能出現在下一個測試。
  sessionStorage.clear();
  // 錯誤回報記得一次載入送過哪些訊息：每個測試是一次新的載入。
  resetPageErrorReporter();
  // 最後才丟：前面的清理照樣做完，不影響下一個測試。
  if (keyWarning !== undefined) throw new Error(`React 的兄弟元件用了同一個 key：${keyWarning}`);
});

afterAll(() => {
  server.close();
});
