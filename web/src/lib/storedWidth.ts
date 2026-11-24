/*
 * 拖拉調整、記在這個瀏覽器的寬度（像素）：項目的側邊面板與浮動視窗共用（個人的顯示偏好，docs/DESIGN.md 8.3「網址就是狀態」）。
 * 讀不到（私密瀏覽、被擋下）或內容不認得時當成沒有設定；寫不進去時這次照樣用新的寬度，下次載入回到預設——
 * 只是個人偏好，不值得打斷使用者。
 */

export function readStoredWidth(key: string): number | null {
  try {
    const value = Number(localStorage.getItem(key) ?? Number.NaN);
    return Number.isFinite(value) && value > 0 ? value : null;
  } catch {
    return null;
  }
}

/** null 是回到預設、拿掉設定。 */
export function saveStoredWidth(key: string, width: number | null): void {
  try {
    if (width === null) {
      localStorage.removeItem(key);
    } else {
      localStorage.setItem(key, String(Math.round(width)));
    }
  } catch {
    // 見檔頭：寫不進去不算錯誤。
  }
}
