import { useLayoutEffect, useRef, useState } from 'react';

/**
 * 快速新增的表單只畫一次（ItemTableView 用 portal 畫進這個容器），位置由結果決定：載入中在骨架的表頭下面、
 * 空白與錯誤在說明上面、有表格時在表頭下面的第一列（ItemTable 的 QuickAddLayer）。這幾種是不同的元件樹位置，
 * 表單直接畫在每個位置的話，載入完或第一個項目建立好、表格換上去的那一刻會整個重新掛上：標題框失去焦點、
 * 「已新增」的訊息消失。所以不搬 React 的元件，搬這個 DOM 容器：表單的元件與元素一直是同一個。
 */
export function useQuickAddHost(isWanted: boolean): HTMLDivElement | null {
  const [host] = useState(() => {
    if (typeof document === 'undefined') return null;
    const element = document.createElement('div');
    trackers.set(element, trackFocus(element));
    return element;
  });
  return isWanted ? host : null;
}

/*
 * 焦點的記錄跟著容器只建一次：舊的位置卸下、新的位置掛上是兩個不同的 QuickAddSlot，記錄放在 slot 裡的話，
 * 新的那一個讀不到舊的記著的焦點。監聽掛在容器自己身上，容器不再被引用時一起回收，不必另外移除。
 */
const trackers = new WeakMap<HTMLElement, { readonly last: () => HTMLElement | null }>();

/** 容器被搬走（舊的位置整個拿掉）時焦點會掉到頁面本體：記著最後在容器裡取得焦點的元素，搬到新位置之後放回去。 */
function trackFocus(host: HTMLElement): { readonly last: () => HTMLElement | null } {
  let focused: HTMLElement | null = null;
  const onIn = (event: FocusEvent) => {
    focused = event.target instanceof HTMLElement ? event.target : null;
  };
  // 焦點是真的移到別處（不是元素被拿掉）時 relatedTarget 不是 null，或容器還在文件裡：忘掉它。
  const onOut = (event: FocusEvent) => {
    if (event.relatedTarget !== null || host.isConnected) focused = null;
  };
  host.addEventListener('focusin', onIn);
  host.addEventListener('focusout', onOut);
  return { last: () => focused };
}

/** 現在的位置：把快速新增的容器放進來。 */
export function QuickAddSlot({ host }: { readonly host: HTMLDivElement }) {
  const slotRef = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const slot = slotRef.current;
    if (slot === null || host.parentElement === slot) return;
    const focused = trackers.get(host)?.last() ?? null;
    slot.append(host);
    if (focused !== null && host.contains(focused)) focused.focus({ preventScroll: true });
  });
  return <div ref={slotRef} />;
}
