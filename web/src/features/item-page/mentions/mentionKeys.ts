import { isComposingNativeKey } from '../../../lib/keyboard';

/** 清單開著時，這個按鍵要做什麼；不是清單的按鍵是 null（交給編輯器照常處理）。 */
export type MentionKeyAction = 'dismiss' | 'next' | 'previous' | 'select';

/**
 * 輸入法選字中的任何按鍵都不是清單的（選字用的 Enter、方向鍵、Esc 是輸入法的）。
 * Esc 一直是清單的（沒有符合的成員時也是）；方向鍵、Enter、Tab 只在有選項時才攔：沒有選項時 Enter 照舊是換行、Tab 照舊換焦點。
 * Ctrl／⌘ + Enter 是送出，Shift + Tab 是往回走，都不攔。
 */
export function mentionKeyAction(event: KeyboardEvent, hasOptions: boolean): MentionKeyAction | null {
  if (isComposingNativeKey(event)) return null;
  if (event.key === 'Escape') return 'dismiss';
  if (!hasOptions) return null;
  if (event.key === 'ArrowDown') return 'next';
  if (event.key === 'ArrowUp') return 'previous';
  if (event.key === 'Enter' && !event.ctrlKey && !event.metaKey) return 'select';
  if (event.key === 'Tab' && !event.shiftKey) return 'select';
  return null;
}
