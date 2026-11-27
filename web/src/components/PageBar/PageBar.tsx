import type { ReactNode, Ref } from 'react';
import { DocumentTitle } from '../DocumentTitle';
import { Breadcrumb, type Crumb } from './Breadcrumb';
import styles from './PageBar.module.css';

export type { Crumb } from './Breadcrumb';
export { Breadcrumb } from './Breadcrumb';

export interface PageBarProps {
  /** 瀏覽器分頁的標題；titleInBar 時也是 h1。 */
  readonly title: string;
  /**
   * 分頁的標題和 h1 不同時才給（設定頁的 h1 是「設定」、分頁是「設定 · 流程」，docs/DESIGN.md 8.3）：
   * 一頁只有一個 <title>，不靠 React 掛上的先後排出哪一個在前面。
   */
  readonly documentTitle?: string;
  /** 預設 true；項目頁是 false（h1 在內容區，頁列只有位置）。 */
  readonly titleInBar?: boolean;
  readonly crumbs?: readonly Crumb[];
  /** 這一頁的動作：最多兩顆看得到的按鈕，其餘收進「更多」選單。 */
  readonly actions?: ReactNode;
  /**
   * 要把焦點放到標題時給（換頁之後、按的按鈕不見了之後）：標題加上 tabIndex={-1}，程式可以把焦點移過去，Tab 不會停在它上面。
   */
  readonly headingRef?: Ref<HTMLHeadingElement>;
  /** 標題旁的小東西（專案頁的「管理員」標籤）：不縮、不進 h1，所以不影響標題與分頁標題。 */
  readonly titleAddon?: ReactNode;
}

/** 頁列動作之間的細直線（專案頁：幾顆頁面入口與「更多」之間）；裝飾，報讀軟體略過。 */
export function PageBarDivider() {
  return <span aria-hidden="true" className={styles.divider} />;
}

/** 每一頁一個（docs/DESIGN.md 6.1）：左邊位置與標題，右邊動作；高度固定，黏在內容區上方。 */
export function PageBar({ title, documentTitle, titleInBar = true, crumbs = [], actions, headingRef, titleAddon }: PageBarProps) {
  return (
    <header className={styles.bar}>
      <DocumentTitle title={documentTitle ?? title} />
      <div className={styles.location}>
        {crumbs.length > 0 && <Breadcrumb crumbs={crumbs} hasTrailingSeparator={titleInBar} />}
        {titleInBar && (
          <h1 ref={headingRef} tabIndex={headingRef === undefined ? undefined : -1} className={styles.title}>
            {title}
          </h1>
        )}
        {titleAddon}
      </div>
      {actions !== undefined && <div className={styles.actions}>{actions}</div>}
    </header>
  );
}
