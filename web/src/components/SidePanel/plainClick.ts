/** 判斷點擊用到的欄位：React 的 MouseEvent 與原生的都可以傳進來。 */
export type ClickLike = Pick<MouseEvent, 'button' | 'metaKey' | 'ctrlKey' | 'shiftKey' | 'altKey' | 'defaultPrevented'>;

/**
 * 清單上的連結要不要攔下來開面板：只攔左鍵、沒有按修飾鍵、還沒被處理過的點擊。
 * 按著 ⌘／Ctrl（新分頁）、Shift（新視窗）、Alt（下載）或用中鍵點，照瀏覽器原本的行為走 href。
 * 窄螢幕不開面板，由呼叫端用 useIsCompact() 另外判斷。
 */
export function isPlainLeftClick(event: ClickLike): boolean {
  return (
    event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey && !event.defaultPrevented
  );
}
