import { useSyncExternalStore } from 'react';
import { setLauncherPosition } from './launcherPosition';

/**
 * 右下角的 AI 助理按鈕有沒有被使用者藏起來：個人的顯示偏好，記在這個瀏覽器（登出不清，和 shell.sidePanelWidth 一樣）。
 * 按鈕（AssistantPanel）與叫回來的帳號選單（features/shell/AccountMenu）不在同一棵樹裡，所以做成小小的外部狀態。
 * 讀寫被擋下（私密瀏覽）時讀不到當成沒藏；寫不進去時這次照樣依使用者按的，記在 override，下次載入回到顯示。
 */
export const LAUNCHER_HIDDEN_STORAGE_KEY = 'assistant.launcherHidden';

const listeners = new Set<() => void>();
let override: boolean | null = null;
let isFocusRequested = false;

function readHidden(): boolean {
  if (override !== null) return override;
  try {
    return localStorage.getItem(LAUNCHER_HIDDEN_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

export function setLauncherHidden(isHidden: boolean): void {
  try {
    if (isHidden) localStorage.setItem(LAUNCHER_HIDDEN_STORAGE_KEY, 'true');
    else localStorage.removeItem(LAUNCHER_HIDDEN_STORAGE_KEY);
    override = null;
  } catch {
    override = isHidden;
  }
  notify();
}

/** 從帳號選單叫回來：按鈕出現後焦點要在它上面（按鈕自己用 takeFocusRequest 領走）。 */
export function showLauncherAndFocus(): void {
  isFocusRequested = true;
  setLauncherHidden(false);
}

/** 從帳號選單放回右下角：清掉記住的位置，按鈕回到預設位置後拿到焦點。 */
export function resetLauncherPositionAndFocus(): void {
  isFocusRequested = true;
  setLauncherPosition(null);
}

export function takeFocusRequest(): boolean {
  const requested = isFocusRequested;
  isFocusRequested = false;
  return requested;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  // 別的分頁改了也跟著。
  const onStorage = (event: StorageEvent) => {
    if (event.key === LAUNCHER_HIDDEN_STORAGE_KEY || event.key === null) listener();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', onStorage);
  };
}

export function useLauncherHidden(): boolean {
  return useSyncExternalStore(subscribe, readHidden, () => false);
}
