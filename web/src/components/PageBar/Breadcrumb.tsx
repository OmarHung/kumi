import { Link, type To } from 'react-router';
import { cx } from '../../lib/cx';
import { Icon } from '../Icon/Icon';
import styles from './PageBar.module.css';

export interface Crumb {
  readonly label: string;
  /** 沒有就是目前的位置（項目頁的最後一個）。 */
  readonly to?: To;
  /** 編號與代號用等寬字。 */
  readonly mono?: boolean;
}

interface BreadcrumbProps {
  readonly crumbs: readonly Crumb[];
  /** 後面還接著頁列的標題時，最後一個也帶分隔的箭頭。 */
  readonly hasTrailingSeparator?: boolean;
}

function CrumbLabel({ crumb, isCurrent }: { readonly crumb: Crumb; readonly isCurrent: boolean }) {
  const className = cx(styles.crumbLabel, crumb.mono === true && styles.mono);
  if (crumb.to === undefined) {
    return (
      <span className={className} aria-current={isCurrent ? 'page' : undefined}>
        {crumb.label}
      </span>
    );
  }
  return (
    <Link to={crumb.to} className={cx(className, styles.crumbLink)}>
      {crumb.label}
    </Link>
  );
}

/**
 * 頁列上的位置：<nav aria-label="位置"> 裡的清單，分隔的箭頭是裝飾。
 * 後面還接著頁列的標題時，目前的頁面是那個標題，位置裡沒有一段標 aria-current；標題不在頁列時才是最後一段。
 * 窄螢幕只顯示最近的上一層與目前那一層（CSS，data-has-title 分兩種）。
 */
export function Breadcrumb({ crumbs, hasTrailingSeparator = false }: BreadcrumbProps) {
  return (
    <nav aria-label="位置" className={styles.breadcrumb}>
      <ol className={styles.trail} data-has-title={hasTrailingSeparator}>
        {crumbs.map((crumb, index) => {
          const isLast = index === crumbs.length - 1;
          return (
            <li key={`${String(index)}-${crumb.label}`} className={styles.crumb}>
              <CrumbLabel crumb={crumb} isCurrent={isLast && !hasTrailingSeparator} />
              {(!isLast || hasTrailingSeparator) && <Icon name="chevronRight" className={styles.separator} />}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
