/**
 * 把畫面捲回頁面的最上面。頁列黏在上方，焦點移到頁列上的標題（h1）時標題一直「看得到」，
 * 瀏覽器不會為了焦點捲動：使用者會停在頁面中間，眼睛和焦點不在同一個地方。
 */
export function scrollToPageTop(): void {
  window.scrollTo({ top: 0 });
}

/**
 * 換頁、捨棄、套用、放棄之後（按的按鈕跟著不見）：焦點移到頁面的標題，畫面捲回最上面（docs/DESIGN.md 8.3）。
 * 標題在內容區的頁面（變更集預覽）也一樣：那裡的標題就在最上面。
 */
export function focusPageHeading(heading: HTMLElement | null): void {
  heading?.focus();
  scrollToPageTop();
}
