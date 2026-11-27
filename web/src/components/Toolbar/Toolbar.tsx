import type { ReactNode } from 'react';
import styles from './Toolbar.module.css';

export interface ToolbarProps {
  /** 影響下面內容的控制項：檢視分頁、搜尋、篩選。 */
  readonly children: ReactNode;
  /** 靠右的控制項（例如「顯示的欄位」）。 */
  readonly end?: ReactNode;
  /**
   * 窄螢幕（48rem 以下）放不下時不折行，排成一行、左右滑（docs/DESIGN.md 6.1，2026-11-27 使用者看過手機版設計稿後採用）。
   * 控制項多的清單頁（專案、我的工作）用；其餘的頁面照舊折行。
   */
  readonly isScrollable?: boolean;
}

/**
 * 內容上方的控制列（docs/DESIGN.md 6.1）。不用 role="toolbar"：那個角色要用方向鍵在控制項之間移動，
 * 這裡的欄位照一般的 Tab 走。欄位的標籤可以視覺上隱藏，但無障礙名稱一定要在。
 */
export function Toolbar({ children, end, isScrollable = false }: ToolbarProps) {
  return (
    <div className={styles.toolbar} data-scrollable={isScrollable || undefined}>
      <div className={styles.start}>{children}</div>
      {end !== undefined && <div className={styles.end}>{end}</div>}
    </div>
  );
}
