import { useSyncExternalStore } from 'react';

// 和 lib/shortcuts.ts 的 MODAL_DIALOG 不同：不排除項目的浮動視窗（data-shortcuts-through），它開著時 ⌘K 也停用，不在對話框上再開對話框。
const MODAL_DIALOG = '[role="dialog"][aria-modal="true"]';

// components/Dialog 一律用 createPortal 掛在 body 底下：只看 body 的直接子元素有沒有增減就夠了。
function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.body, { childList: true });
  return () => {
    observer.disconnect();
  };
}

function hasModalDialog(): boolean {
  return document.querySelector(MODAL_DIALOG) !== null;
}

/**
 * 現在有沒有強制回應的對話框開著（成員、新增專案、確認…）。
 * 快速跳轉用它決定 ⌘K 要不要作用：對話框上面不再開對話框（docs/DESIGN.md 6.2）。
 */
export function useIsModalDialogOpen(): boolean {
  return useSyncExternalStore(subscribe, hasModalDialog, () => false);
}
