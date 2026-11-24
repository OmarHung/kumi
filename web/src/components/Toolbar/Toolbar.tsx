import type { ReactNode } from 'react';
import styles from './Toolbar.module.css';

export interface ToolbarProps {
  /** 影響下面內容的控制項：檢視分頁、搜尋、篩選。 */
  readonly children: ReactNode;
  /** 靠右的控制項（例如「顯示的欄位」）。 */
  readonly end?: ReactNode;
}

/**
 * 內容上方的控制列（docs/DESIGN.md 6.1）。不用 role="toolbar"：那個角色要用方向鍵在控制項之間移動，
 * 這裡的欄位照一般的 Tab 走。欄位的標籤可以視覺上隱藏，但無障礙名稱一定要在。
 */
export function Toolbar({ children, end }: ToolbarProps) {
  return (
    <div className={styles.toolbar}>
      <div className={styles.start}>{children}</div>
      {end !== undefined && <div className={styles.end}>{end}</div>}
    </div>
  );
}
