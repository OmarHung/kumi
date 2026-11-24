import type { RefObject } from 'react';
import { flushSync } from 'react-dom';

/** 焦點的候選：元素的 id，或指向元素的 ref。 */
export type FocusTarget = string | RefObject<HTMLElement | null>;

function resolve(target: FocusTarget): HTMLElement | null {
  return typeof target === 'string' ? document.getElementById(target) : target.current;
}

function canFocus(element: HTMLElement | null): element is HTMLElement {
  return element !== null && element.isConnected && !element.hasAttribute('disabled');
}

/** 焦點掉回頁面本體了（按的按鈕或所在的格子被拿掉）：要接手放到別的地方。 */
export function isFocusLost(): boolean {
  const active = document.activeElement;
  return active === null || active === document.body;
}

/**
 * 先讓畫面更新完，再把焦點放到第一個還在、沒有停用的候選上。
 * 按的按鈕被拿掉（刪除、移除、整列換位置）或停用時，焦點不這樣接手就會掉回頁面本體，在對話框裡 Tab 會跑到背景。
 */
export function updateThenFocus(update: () => void, targets: readonly FocusTarget[]): void {
  flushSync(update);
  targets.map(resolve).find(canFocus)?.focus();
}
