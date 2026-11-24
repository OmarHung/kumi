import { CSRF_HEADER, isApiError } from '../api/http';
import type { ClientErrorKind, ClientErrorReport } from '../api/types';

/*
 * 網頁的例外回報到伺服器（internal/HANDOFF-security-local-ops.md 第 2 節第 5 點、第 7 節第 4 點）：
 * 伺服器用 Error 等級記下來，和它自己的錯誤在同一個地方（scripts/ops/errors.sh 看得到）。
 * 回報是診斷用的附加功能：任何情況下都不能讓畫面多出一個錯誤，所以每一步都包在 try／catch 裡，
 * 回應不管是什麼（401 沒登入、429 太多次、網路失敗）都不處理。
 */

export const CLIENT_ERRORS_PATH = '/v1/client-errors';

/** 一次載入最多送這麼多筆：程式卡在迴圈裡一直出錯時，不會把伺服器的記錄（與每人每分鐘 10 次的額度）灌爆。 */
export const MAX_REPORTS_PER_LOAD = 20;

// 和伺服器的上限一樣（第 4 節的合約）：超過的部分截掉，不要整筆被擋下。
const MESSAGE_MAX = 1000;
const STACK_MAX = 4000;
const PATH_MAX = 500;
const KEYS_MAX = 20;

const NO_MESSAGE = '（沒有訊息）';

/**
 * 沒有例外物件、我們也修不了的雜訊：別的網域的腳本只給「Script error.」；ResizeObserver 這一輪沒來得及通知完
 * （Chrome 的「ResizeObserver loop completed with undelivered notifications.」、舊的「… limit exceeded」）是瀏覽器的提醒，
 * 畫面照常。送了只會蓋過真的錯誤。
 */
function isNoise(message: string): boolean {
  return message === 'Script error.' || message.startsWith('ResizeObserver loop');
}

export interface ErrorReporter {
  /** 回報一個例外（或被丟出來的任何值）。API 的失敗、重複的訊息、超過上限的、知道沒登入時都不送。絕不丟例外。 */
  readonly report: (kind: ClientErrorKind, error: unknown) => void;
  /** 只有瀏覽器給的訊息、沒有例外物件時（window 的 error 事件）：訊息照原樣送，沒有堆疊。 */
  readonly reportMessage: (kind: ClientErrorKind, message: string) => void;
}

interface ErrorReporterOptions {
  /** 送出一筆回報；預設是 sendClientError。 */
  readonly send?: (report: ClientErrorReport) => Promise<unknown>;
  /** 目前的路徑（不帶查詢字串：裡面可能有搜尋的字）。預設是 location.pathname。 */
  readonly currentPath?: () => string;
  /** 知道現在沒登入（例如在登入頁）時回 true：送了只會是 401。不知道（還在確認）時回 false，照樣送。 */
  readonly isSignedOut?: () => boolean;
}

/** 截到 max 個 UTF-16 單位；切在代理字元對的中間時連前半個一起去掉（半個字元是無效的字串，伺服器會整筆擋下）。 */
function truncate(text: string, max: number): string {
  const cut = text.slice(0, max);
  const last = cut.charCodeAt(cut.length - 1);
  const isHighSurrogate = last >= 0xd800 && last <= 0xdbff;
  return isHighSurrogate ? cut.slice(0, -1) : cut;
}

function typeNameOf(value: object): string {
  const prototype: unknown = Object.getPrototypeOf(value);
  const name = typeof prototype === 'object' && prototype !== null ? (prototype.constructor as { name?: unknown }).name : undefined;
  return typeof name === 'string' && name !== '' ? name : 'Object';
}

/**
 * 不是 Error 的值（被丟出來的字串、拒絕的原因是物件）只送型別名稱與鍵名，不送值：裡面可能有使用者的資料
 * （Email、打的字），伺服器的記錄不該留著它們。例：「string」「Object { email, status }」「Array(2)」。
 */
function describeValue(value: unknown): string {
  if (value === null) return 'null';
  if (typeof value === 'function') return 'function';
  if (typeof value !== 'object') return typeof value;
  if (Array.isArray(value)) return `Array(${String(value.length)})`;
  const keys = Object.keys(value).slice(0, KEYS_MAX);
  const name = typeNameOf(value);
  return keys.length === 0 ? name : `${name} { ${keys.join(', ')} }`;
}

function messageOf(error: unknown): string {
  if (error instanceof Error) return error.message === '' ? error.name : `${error.name}: ${error.message}`;
  return describeValue(error);
}

function stackOf(error: unknown): string | null {
  return error instanceof Error && typeof error.stack === 'string' ? truncate(error.stack, STACK_MAX) : null;
}

/** 一次載入用一個：記得送過哪些訊息、送了幾筆。 */
export function createErrorReporter({
  send = sendClientError,
  currentPath = () => window.location.pathname,
  isSignedOut = () => false,
}: ErrorReporterOptions = {}): ErrorReporter {
  const sentMessages = new Set<string>();

  function submit(kind: ClientErrorKind, rawMessage: string, stack: string | null): void {
    // 沒登入時不送，也不記成「送過了」：登入之後同一個錯誤照樣送得出去。
    if (sentMessages.size >= MAX_REPORTS_PER_LOAD || isSignedOut()) return;
    const message = truncate(rawMessage, MESSAGE_MAX);
    const report = { kind, message: message === '' ? NO_MESSAGE : message, stack, path: truncate(currentPath(), PATH_MAX) };
    if (sentMessages.has(report.message)) return;
    sentMessages.add(report.message);
    send(report).catch(() => undefined);
  }

  // 回報本身出錯也不往外丟：外面接到的話又是一個例外，可能一直循環下去。
  return {
    report: (kind, error) => {
      try {
        if (!isApiError(error)) submit(kind, messageOf(error), stackOf(error));
      } catch {
        // 見上面。
      }
    },
    reportMessage: (kind, message) => {
      try {
        submit(kind, message, null);
      } catch {
        // 見上面。
      }
    },
  };
}

/** 送出一筆回報。只給登入的人用（沒登入是 401），回應與網路失敗都靜靜放掉。 */
export async function sendClientError(report: ClientErrorReport): Promise<void> {
  try {
    // keepalive：出錯之後馬上換頁或關掉分頁時，請求照樣送得出去。
    await fetch(CLIENT_ERRORS_PATH, {
      method: 'POST',
      keepalive: true,
      credentials: 'same-origin',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', [CSRF_HEADER]: '1' },
      body: JSON.stringify(report),
    });
  } catch {
    // 網路失敗：診斷資料丟了就丟了，不重送。
  }
}

/**
 * 在 window 上聽 error 與 unhandledrejection，交給 reporter。回傳拿下來的函式。
 * 沒有例外物件的 error 回報瀏覽器給的訊息；其中的雜訊（isNoise）不回報。
 */
export function installErrorReporting(target: Window, reporter: ErrorReporter): () => void {
  const onError = (event: ErrorEvent) => {
    const error = event.error as unknown;
    if (error !== null && error !== undefined) {
      reporter.report('error', error);
    } else if (!isNoise(event.message)) {
      reporter.reportMessage('error', event.message);
    }
  };
  const onRejection = (event: PromiseRejectionEvent) => {
    reporter.report('unhandled_rejection', event.reason);
  };
  target.addEventListener('error', onError);
  target.addEventListener('unhandledrejection', onRejection);
  return () => {
    target.removeEventListener('error', onError);
    target.removeEventListener('unhandledrejection', onRejection);
  };
}

let pageReporter: ErrorReporter | null = null;
let signedOutCheck: () => boolean = () => false;

/**
 * 告訴這一次載入共用的 reporter 怎麼知道「現在沒登入」：main.tsx 建好 QueryClient 之後設（看登入狀態的快取）。
 * 沒設時當成不知道，照樣送。
 */
export function setSignedOutCheck(check: () => boolean): void {
  signedOutCheck = check;
}

/** 這一次載入共用的 reporter：main.tsx 掛在 window 上的、路由的錯誤頁用的是同一個（重複的訊息只送一次）。 */
export function pageErrorReporter(): ErrorReporter {
  pageReporter ??= createErrorReporter({ isSignedOut: () => signedOutCheck() });
  return pageReporter;
}

/** 測試用：每個測試從頭算（送過的訊息、筆數、怎麼知道沒登入）。 */
export function resetPageErrorReporter(): void {
  pageReporter = null;
  signedOutCheck = () => false;
}
