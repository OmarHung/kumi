import { useId } from 'react';
import { Link, type To } from 'react-router';
import { CountBadge, CountDescription, type CountTone } from '../CountBadge/CountBadge';
import styles from './NavTabs.module.css';

export interface NavTabCount {
  readonly value: number;
  /** 給報讀軟體的完整說法（含數字），例如「2 項還沒送出的修改」。 */
  readonly label: string;
  /** danger（要修正的問題）除了顏色還有錯誤的圖示（CountBadge）。 */
  readonly tone: CountTone;
}

export interface NavTab {
  readonly key: string;
  readonly label: string;
  readonly to: To;
  readonly isCurrent: boolean;
  readonly counts?: readonly NavTabCount[];
}

export interface NavTabsProps {
  /** nav 的名稱，例如「檢視」「設定的區塊」。 */
  readonly label: string;
  readonly tabs: readonly NavTab[];
}

function NavTabItem({ tab, countsId }: { readonly tab: NavTab; readonly countsId: string }) {
  const counts = tab.counts ?? [];
  const hasCounts = counts.length > 0;
  return (
    <li className={styles.item}>
      <Link
        to={tab.to}
        className={styles.tab}
        aria-current={tab.isCurrent ? 'page' : undefined}
        aria-describedby={hasCounts ? countsId : undefined}
      >
        {tab.label}
        {counts.map((count) => (
          <CountBadge key={count.label} value={count.value} tone={count.tone} />
        ))}
      </Link>
      {hasCounts && <CountDescription id={countsId} labels={counts.map((count) => count.label)} />}
    </li>
  );
}

/**
 * 用網址切換的分頁（表格／看板、設定頁的區塊）：nav 裡的連結，目前的那個 aria-current="page"。
 * 計數放在描述裡，分頁的名稱維持只有名字：測試與報讀軟體靠名稱找到它。
 */
export function NavTabs({ label, tabs }: NavTabsProps) {
  const baseId = useId();
  return (
    <nav aria-label={label} className={styles.nav}>
      <ul className={styles.list}>
        {tabs.map((tab, index) => (
          <NavTabItem key={tab.key} tab={tab} countsId={`${baseId}-${String(index)}`} />
        ))}
      </ul>
    </nav>
  );
}
