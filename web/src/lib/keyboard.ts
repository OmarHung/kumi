import type { KeyboardEvent } from 'react';

/** 輸入法正在處理這個按鍵時，瀏覽器給的 keyCode。 */
const IME_PROCESS_KEY_CODE = 229;

/**
 * 這個按鍵是輸入法在用的（例如注音選字的 Enter），不能當成送出或儲存。
 * Safari 在 compositionend 之後才送出選字用的 Enter：那時 isComposing 已經是 false，只看得出 keyCode 是 229。
 */
export function isComposingKey(event: KeyboardEvent): boolean {
  return isComposingNativeKey(event.nativeEvent);
}

/** 同 isComposingKey，給直接掛在 document 上的聽眾（lib/shortcuts.ts）用的原生事件版本。 */
export function isComposingNativeKey(event: globalThis.KeyboardEvent): boolean {
  // keyCode 已經不建議用，但 Safari 這個情況只能從它看出來；透過只有 keyCode 的型別讀，不碰標成過時的宣告。
  const legacy: { readonly keyCode: number } = event;
  return event.isComposing || legacy.keyCode === IME_PROCESS_KEY_CODE;
}

/** 使用者真的按下的 Enter（不是輸入法選字用的）。 */
export function isCommitEnter(event: KeyboardEvent): boolean {
  return event.key === 'Enter' && !isComposingKey(event);
}

/** 表單裡的文字框：選字用的 Enter 不能觸發瀏覽器預設的「按 Enter 送出表單」。 */
export function preventComposingSubmit(event: KeyboardEvent): void {
  if (event.key === 'Enter' && isComposingKey(event)) {
    event.preventDefault();
  }
}
