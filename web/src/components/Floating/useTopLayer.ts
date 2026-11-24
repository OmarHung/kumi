import { useLayoutEffect, type RefObject } from 'react';

export const SUPPORTS_TOP_LAYER = 'showPopover' in HTMLElement.prototype;

/**
 * 放進瀏覽器的 top layer（popover="manual"）：不被表格、看板的欄這類會裁切的捲動容器切掉，
 * 也不受祖先的 transform、container-type 影響 position: fixed。manual 是不讓瀏覽器自己用 Esc、點外面關上，
 * 開關一律由 Menu、Popover 的狀態決定。沒有 showPopover 的環境不加 popover 屬性（jsdom 有「沒打開的 [popover]
 * 不顯示」的預設樣式，卻打不開它），退回一般的 position: fixed 加 --z-popover。
 * Floating 與新通知的提示（features/notification-toasts）共用。
 */
export function useTopLayer(ref: RefObject<HTMLElement | null>): void {
  useLayoutEffect(() => {
    const element = ref.current;
    if (SUPPORTS_TOP_LAYER && element !== null && !element.matches(':popover-open')) {
      element.showPopover();
    }
  }, [ref]);
}
