import type { CSSProperties } from 'react';
import { Link } from 'react-router';
import type { OverviewStateCount } from '../../api/overview';
import { overviewLinks } from './overviewLinks';
import styles from './StateBar.module.css';

type SegmentVars = CSSProperties & Readonly<Record<'--count', number>>;

interface StateBarProps {
  readonly projectKey: string;
  readonly states: readonly OverviewStateCount[];
}

/**
 * 依狀態分段的橫條：一個狀態一段，長度是它的項目數佔全部的比例，顏色照狀態類別（待規劃、未開始、進行中、完成、已取消各一種）。
 * 段的寬度是執行時才算得出來的數字，class 表達不了，所以只設一個 CSS 變數 `--count`，長相與換算都寫在 StateBar.module.css；
 * 這個檔案是總覽唯一用 style 的地方（docs/DESIGN.md 第 13 節）。
 * 顏色只是輔助：每一段有名稱與數量（連結的名稱、title），整條的說法放在 role="group" 的名稱裡；沒有項目時只畫一條空的底。
 */
export function StateBar({ projectKey, states }: StateBarProps) {
  if (states.length === 0) {
    return <span className={styles.empty}>沒有項目</span>;
  }
  const total = states.reduce((sum, state) => sum + state.count, 0);
  return (
    <ul className={styles.bar} aria-label={`${projectKey} 各狀態的項目數，共 ${String(total)} 個`}>
      {states.map((state) => {
        const vars: SegmentVars = { '--count': state.count };
        const label = `${state.name} ${String(state.count)}`;
        return (
          <li key={state.key} className={styles.segment} style={vars}>
            <Link
              to={overviewLinks.state(projectKey, state.key)}
              className={styles.link}
              data-category={state.category}
              aria-label={label}
              title={label}
            />
          </li>
        );
      })}
    </ul>
  );
}
