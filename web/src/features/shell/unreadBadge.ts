/** 壓在圖示角落的膠囊最多三個字：超過 99 寫「99+」，不然會伸出那一格；報讀照舊唸完整的數字。 */
const BADGE_MAX = 99;

export function badgeText(count: number): string {
  return count > BADGE_MAX ? `${String(BADGE_MAX)}+` : String(count);
}
