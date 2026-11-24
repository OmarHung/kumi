import type { ReactNode } from 'react';
import styles from './PageLayout.module.css';

export type PageVariant = 'workspace' | 'list' | 'reading';

export interface PageLayoutProps {
  /**
   * workspace：剛好一個畫面高，內容自己捲（表格、看板），用滿寬度；list：整頁捲動，用滿寬度；
   * reading：整頁捲動，上限 --content-max、靠左，長文字由內容自己限在 --reading-max（docs/DESIGN.md 6.2）。
   */
  readonly variant: PageVariant;
  /** 頁列（PageBar）。 */
  readonly bar: ReactNode;
  /** 工具列（Toolbar），有才出現。 */
  readonly toolbar?: ReactNode;
  readonly children: ReactNode;
}

/** 三種頁面的骨架：頁列、工具列、內容，間距與捲動的方式在這裡統一。 */
export function PageLayout({ variant, bar, toolbar, children }: PageLayoutProps) {
  return (
    <div className={styles.layout} data-variant={variant}>
      {bar}
      {toolbar !== undefined && <div className={styles.toolbar}>{toolbar}</div>}
      <div className={styles.content}>{children}</div>
    </div>
  );
}
