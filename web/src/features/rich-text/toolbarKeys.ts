import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import { isComposingKey } from '../../lib/keyboard';

/**
 * 工具列的按鍵：兩種編輯方式（所見即所得與 Markdown 原文）共用。放在自己的檔案、不碰 Tiptap：
 * Markdown 原文的文字框在主程式裡，從 editorInteractions 匯入會把 ProseMirror 與 Markdown 的轉換一起拉進主程式檔。
 */
export interface ToolbarKeyActions {
  readonly onSubmit: () => void;
  readonly onCancel: (() => void) | undefined;
}

/**
 * 焦點在工具列的按鈕上時的 Ctrl／⌘+Enter 與 Esc：和在內容裡一樣。內容裡沒有取消的處理時 Esc 什麼都不做
 * （全域的「關閉面板」不看文字框裡的 Esc），所以這裡一樣吃掉，不讓它落到全域把面板關了。
 * 連結的小框自己處理它裡面的按鍵（Esc 先關小框），已經處理過（defaultPrevented）的不碰。
 */
export function handleToolbarKeyDown(current: ToolbarKeyActions, event: ReactKeyboardEvent): void {
  if (event.defaultPrevented || isComposingKey(event)) return;
  if (event.target instanceof Element && event.target.closest('[role="dialog"]') !== null) return;
  if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
    event.preventDefault();
    current.onSubmit();
  } else if (event.key === 'Escape') {
    event.preventDefault();
    current.onCancel?.();
  }
}
