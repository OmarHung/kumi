/**
 * 區塊標題（SettingsSection 的 h2）拿來當焦點的退路：清單空了、按下的按鈕不在了之後，焦點要有地方落。
 * h2 本來不能被程式聚焦，這裡補上 tabIndex={-1}（不進 Tab 順序）。找不到就是 null。
 */
export function focusableHeading(headingId: string): HTMLElement | null {
  const heading = document.getElementById(headingId);
  if (heading === null) return null;
  heading.tabIndex = -1;
  return heading;
}
