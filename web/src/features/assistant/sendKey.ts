import { useSyncExternalStore } from 'react';
import type { KeyboardEvent } from 'react';
import { isCommitEnter } from '../../lib/keyboard';

/**
 * AI 助理的輸入框用哪個按法送出：Enter（Shift+Enter 換行），或 ⌘／Ctrl+Enter（Enter 換行）。
 * 個人的偏好，記在這個瀏覽器；寫法和 notification-toasts/toastMode.ts 一樣。
 */
export type SendKey = 'enter' | 'mod-enter';

export const SEND_KEYS: readonly SendKey[] = ['enter', 'mod-enter'];

export const DEFAULT_SEND_KEY: SendKey = 'enter';

export const SEND_KEY_STORAGE_KEY = 'assistant.sendKey';

function isSendKey(value: unknown): value is SendKey {
  return SEND_KEYS.some((key) => key === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readSendKey(): SendKey {
  try {
    const stored = localStorage.getItem(SEND_KEY_STORAGE_KEY);
    return isSendKey(stored) ? stored : DEFAULT_SEND_KEY;
  } catch {
    return DEFAULT_SEND_KEY;
  }
}

// 只有寫不進去時才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: SendKey | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

export function currentSendKey(): SendKey {
  return unsavedValue ?? readSendKey();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setSendKey(value: SendKey): void {
  try {
    localStorage.setItem(SEND_KEY_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    unsavedValue = value;
  }
  notify();
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存。 */
export function resetSendKeyForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === SEND_KEY_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useSendKey(): SendKey {
  return useSyncExternalStore(subscribe, currentSendKey, () => DEFAULT_SEND_KEY);
}

/** 這個平台的組合鍵叫什麼：Mac 是 ⌘，其他是 Ctrl。 */
export function modKeyName(): string {
  const platform = typeof navigator === 'undefined' ? '' : navigator.platform;
  return /mac|iphone|ipad/i.test(platform) ? '⌘' : 'Ctrl';
}

/** 目前的按法的說明（送出鈕的提示與輸入框的說明）。 */
export function sendKeyHint(sendKey: SendKey): string {
  return sendKeyDescription(sendKey);
}

/** 這個按鍵是「送出」：enter 模式是沒按 Shift 的 Enter；mod-enter 模式要同時按 ⌘ 或 Ctrl。輸入法選字用的 Enter 一律不算。 */
export function isSendKeystroke(event: KeyboardEvent, sendKey: SendKey): boolean {
  if (!isCommitEnter(event)) return false;
  return sendKey === 'enter' ? !event.shiftKey : event.metaKey || event.ctrlKey;
}

/** 選單裡的完整說法。 */
export function sendKeyDescription(sendKey: SendKey): string {
  return sendKey === 'enter' ? 'Enter 送出，Shift+Enter 換行' : `${modKeyName()}+Enter 送出，Enter 換行`;
}

/** 鈕上寫的按法（「Enter 送出」、「⌘+Enter 送出」）。 */
export function sendKeyLabel(sendKey: SendKey): string {
  return sendKey === 'enter' ? 'Enter 送出' : `${modKeyName()}+Enter 送出`;
}

/** 窄的時候鈕上只留按鍵（「↵」、「⌘↵」、「Ctrl↵」）。 */
export function sendKeyShort(sendKey: SendKey): string {
  return sendKey === 'enter' ? '↵' : `${modKeyName()}↵`;
}
