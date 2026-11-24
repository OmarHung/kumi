import { useSyncExternalStore } from 'react';

// 確認類的對話框（components/Dialog）：role="dialog" 加 aria-modal。項目的浮動視窗帶 data-shortcuts-through，不算：
// 它會開很久，提示照樣顯示（它的背後是 inert，提示另外豁免，見 components/Dialog/inertExempt.ts）。
const CONFIRM_DIALOG = '[role="dialog"][aria-modal="true"]:not([data-shortcuts-through])';

// 對話框一律用 createPortal 掛在 body 底下：只看 body 的直接子元素有沒有增減就夠了。
function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.body, { childList: true });
  return () => {
    observer.disconnect();
  };
}

function hasConfirmDialog(): boolean {
  return document.querySelector(CONFIRM_DIALOG) !== null;
}

/** 現在有沒有確認類的對話框開著：有的話新的提示先不顯示（對話框很短，按鈕在右下會被蓋住），關掉再顯示。 */
export function useIsConfirmDialogOpen(): boolean {
  return useSyncExternalStore(subscribe, hasConfirmDialog, () => false);
}
