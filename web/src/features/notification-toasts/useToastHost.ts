import { useSyncExternalStore } from 'react';

const MODAL_DIALOG = '[role="dialog"][aria-modal="true"]';

// 對話框一律用 createPortal 掛在 body 底下：只看 body 的直接子元素有沒有增減就夠了（不觀察整棵樹，頁面的每個變動都會觸發）。
function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.body, { childList: true });
  return () => {
    observer.disconnect();
  };
}

/** 最上面的（最後開的）aria-modal 視窗，沒有就是 body。同一個元素回傳同一個參考，沒變就不重新渲染。 */
function topmostHost(): HTMLElement {
  return Array.from(document.querySelectorAll<HTMLElement>(MODAL_DIALOG)).at(-1) ?? document.body;
}

/**
 * 提示層該放在哪個元素裡：有 aria-modal 的視窗（例如項目的浮動視窗）開著就放在它裡面，沒有就放在 body。
 * 理由：aria-modal 視窗開著時，瀏覽器把視窗以外的東西從無障礙樹拿掉，放在 body 底下的提示報讀軟體走不到、aria-live 也不唸；
 * 放進視窗裡，無障礙樹、Tab 繞圈都包含它。畫面不變（提示是 top layer 的 popover，不跟著視窗的版面）。
 * 視窗關掉時回到 body；提示的內容（entries）在外框的 state 裡，不會跟著視窗一起消失。
 */
export function useToastHost(): HTMLElement {
  return useSyncExternalStore(subscribe, topmostHost, () => document.body);
}
