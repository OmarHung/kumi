/**
 * 焦點只是暫時在這裡的元素（下拉的篩選框：關上時焦點先回到按鈕）標這個屬性，對話框的焦點陷阱算頭尾時不算它。
 * 不然下拉是對話框最後一個可停的元素時，Tab 從篩選框出發，陷阱把還在 DOM 裡的篩選框當成最後一個，焦點就跑出對話框。
 */
export const TRANSIENT_FOCUS_ATTRIBUTE = 'data-transient-focus';

const FOCUSABLE = ['a[href]', 'button:not([disabled])', 'input:not([disabled])', 'select:not([disabled])', 'textarea:not([disabled])', '[tabindex]'].join(',');

/**
 * container 裡用 Tab 停得到的元素，照文件的順序（對話框鎖焦點、浮層打開時的焦點都用它）。
 * tabIndex 是負的不算：例如選單的項目（tabindex="-1"，方向鍵才到得了）。算進去的話，對話框裡的選單開著時，
 * 「最後一個」會是選單的項目而不是按鈕，Tab 就從按鈕跑出對話框。
 * 藏起來的（hidden 屬性，例如留言框旁邊藏著的選檔案 input）也不算：它不在 Tab 的順序裡，算進去會變成「最後一個」，
 * 真正的最後一顆按鈕按 Tab 就跑出去了。
 */
export function focusableIn(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    (element) => element.tabIndex >= 0 && element.closest('[hidden]') === null,
  );
}

/** 對話框的焦點陷阱用的頭尾：不算焦點只是暫時在那裡的元素（TRANSIENT_FOCUS_ATTRIBUTE 裡面的）。 */
export function trapStopsIn(container: HTMLElement): HTMLElement[] {
  // inert 的（關著的 AI 助理面板）也不算：焦點進不去，算進去頭尾就不是真的頭尾。
  return focusableIn(container).filter(
    (element) => element.closest(`[${TRANSIENT_FOCUS_ATTRIBUTE}]`) === null && element.closest('[inert]') === null,
  );
}
