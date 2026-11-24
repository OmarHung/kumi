import { useLayoutEffect, useState, useSyncExternalStore } from 'react';
import { INERT_EXEMPT_ATTRIBUTE } from '../../components/Dialog/inertExempt';
import { PANEL_ID } from './panelId';
import styles from './AssistantPanel.module.css';

/** 項目的浮動視窗（components/SidePanel/FloatingPanel）帶這個屬性：AI 助理認得它，開著時把按鈕與面板放進去。 */
export const ASSISTANT_HOST_ATTRIBUTE = 'data-assistant-host';

const MODAL_DIALOG = `[role="dialog"][aria-modal="true"]:not(#${PANEL_ID})`;

// 對話框一律用 createPortal 掛在 body 底下：只看 body 的直接子元素有沒有增減就夠了（和新通知的提示同一套）。
function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.body, { childList: true });
  return () => {
    observer.disconnect();
  };
}

/** 最上面的（最後開的）modal 視窗；它是項目的浮動視窗就是它，不是（確認框、設定的對話框）或沒有就是 body。 */
function currentHost(): HTMLElement {
  const top = Array.from(document.querySelectorAll<HTMLElement>(MODAL_DIALOG)).at(-1);
  return top?.hasAttribute(ASSISTANT_HOST_ATTRIBUTE) === true ? top : document.body;
}

/** 搬動時焦點與捲動的位置會被瀏覽器丟掉（元素離開文件）：先記下來、搬完放回去。 */
function moveKeepingState(container: HTMLElement, host: HTMLElement): void {
  if (container.parentElement === host) return;
  const active = document.activeElement;
  const focused = active instanceof HTMLElement && container.contains(active) ? active : null;
  const scrolls = Array.from(container.querySelectorAll<HTMLElement>('*'))
    .filter((element) => element.scrollTop > 0)
    .map((element) => ({ element, top: element.scrollTop }));
  host.appendChild(container);
  scrolls.forEach(({ element, top }) => {
    element.scrollTop = top;
  });
  focused?.focus({ preventScroll: true });
}

/**
 * AI 助理（按鈕、「×」與面板）放在哪裡：平常在 body 底下；最上面的 modal 是項目的浮動視窗時放進那個視窗裡
 * （aria-modal 的視窗開著時，瀏覽器把視窗以外的東西從無障礙樹拿掉、背後又是 inert，放在外面就用不了），關掉回到 body。
 * 回傳一個固定的容器元素：React 一直 portal 到它，搬的是這個元素（不是重新掛上），所以草稿、進行中的回覆都不中斷。
 * 容器帶 INERT_EXEMPT_ATTRIBUTE：浮動視窗背後 inert 時不被一起標成 inert。層級在視窗內容之上由 CSS 的 --z-assistant 決定。
 */
export function useAssistantHost(): HTMLElement {
  const [container] = useState(() => {
    const element = document.createElement('div');
    element.className = styles.host ?? '';
    element.setAttribute(INERT_EXEMPT_ATTRIBUTE, '');
    return element;
  });
  const host = useSyncExternalStore(subscribe, currentHost, () => document.body);
  useLayoutEffect(() => {
    moveKeepingState(container, host);
  }, [container, host]);
  useLayoutEffect(
    () => () => {
      container.remove();
    },
    [container],
  );
  return container;
}
