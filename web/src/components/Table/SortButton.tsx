import type { ComponentPropsWithRef } from 'react';
import type { SortDirection } from '../../api/blueprint';
import { cx } from '../../lib/cx';
import { Icon } from '../Icon/Icon';
import styles from './Table.module.css';

type SortButtonProps = Omit<ComponentPropsWithRef<'button'>, 'children'> & {
  /** 欄名（按鈕的名稱；欄比字窄時是省略號，完整的欄名在 title）。 */
  readonly label: string;
  /** 排的是這一欄時的方向；沒有排這一欄是 undefined。方向寫在 th 的 aria-sort（呼叫端給 ResizableHeader）。 */
  readonly direction: SortDirection | undefined;
};

/** 表頭的排序按鈕：欄名加方向的圖示（docs/DESIGN.md 7.1）。點了怎麼排由呼叫端決定（onClick）。 */
export function SortButton({ label, direction, className, type = 'button', ...rest }: SortButtonProps) {
  return (
    <button type={type} className={cx(styles.sortButton, className)} {...rest}>
      <span className={styles.headerLabel} title={label}>
        {label}
      </span>
      <Icon name={direction === 'desc' ? 'sortDesc' : 'sortAsc'} className={styles.sortIcon} />
    </button>
  );
}
