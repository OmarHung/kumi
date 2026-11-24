import { useRef, type ReactNode, type Ref } from 'react';
import { cx } from '../../lib/cx';
import styles from './Table.module.css';
import { useHorizontalOverflow } from './useHorizontalOverflow';

/** 表格的共用樣式。F2 的 TanStack Table 也用這組 class，畫面才會一致。 */
export { styles as tableStyles };

/**
 * 表格放在哪一種版面（docs/DESIGN.md 6.2、7.1）：
 * - inline：只橫向捲動（預設，原本的樣子）。
 * - fill：工作區頁。撐滿剩下的高度、兩個方向都自己捲，表頭黏在容器上方，橫向捲軸一直在畫面裡。
 * - page：清單頁。整頁捲動、表頭黏在頁列下面；表格放不下時才退回橫向捲動（那時表頭只黏在容器裡）。
 */
export type TableLayout = 'inline' | 'fill' | 'page';

/**
 * 執行時才算得出來的 CSS 變數（使用者拉的欄寬，docs/DESIGN.md 7.1「欄寬」、第 13 節）：名稱是表格自己 CSS 裡的區域變數，
 * 值是長度。設在捲動區塊上，裡面的欄寬、固定欄的位置、捲動讓出的寬度（scroll-padding）都看得到同一個值。
 */
export type TableVariables = Readonly<Record<`--${string}`, string>>;

interface TableScrollProps {
  /** 給輔助科技的名稱；表格比畫面寬時，這個區塊可以用鍵盤捲動。 */
  readonly label: string;
  readonly layout?: TableLayout;
  /** 只放版面（例如捲動時讓出固定欄的寬度）。 */
  readonly className?: string;
  /**
   * 區塊自己是不是 Tab 停駐點（預設是：表格比畫面寬時用方向鍵捲動）。表格的每一格都用方向鍵停得到、
   * 走到哪捲到哪時（專案的表格，lib/useRovingGrid.ts）給 false：表格只佔一個停駐點，不必在前面再多一個。
   */
  readonly isFocusable?: boolean;
  /**
   * 上下兩條收邊的細線（預設畫）。一個區塊裡放好幾張表格、每一張自己收邊時（我的工作的分組）給 false：
   * 否則第一組的標題上面會和工具列的細線疊成兩條（docs/DESIGN.md 7.1）。
   */
  readonly frame?: boolean;
  /** 使用者拉的欄寬（只放 CSS 變數）。 */
  readonly variables?: TableVariables;
  readonly children: ReactNode;
}

export function TableScroll({
  label,
  layout = 'inline',
  className,
  isFocusable = true,
  frame = true,
  variables,
  children,
}: TableScrollProps) {
  const ref = useRef<HTMLDivElement>(null);
  // 表頭要黏住頁列，這個容器就不能是捲動容器；只有放不下時才犧牲黏住、換成橫向捲動。
  const isOverflowing = useHorizontalOverflow(ref, layout === 'page');
  return (
    <div
      ref={ref}
      role="region"
      aria-label={label}
      tabIndex={isFocusable ? 0 : undefined}
      className={cx(styles.scroll, className)}
      data-layout={layout}
      data-frame={frame ? undefined : 'false'}
      data-overflowing={layout === 'page' ? String(isOverflowing) : undefined}
      // 第 13 節：style 只放執行時才算得出來的值，這裡是使用者拉的欄寬（CSS 變數）。
      style={variables}
    >
      {children}
    </div>
  );
}

/** 一欄：拉過的寬度（px），沒拉過就是 className 的預設寬度。 */
export interface TableColumn {
  readonly key: string;
  readonly className?: string;
  readonly width?: number;
}

interface TableColumnsProps {
  readonly columns: readonly TableColumn[];
  /**
   * 最後面多一個沒有寬度的欄（沒有格子）：欄寬固定（table-layout: fixed）的表格比容器窄時，剩下的寬度給它，
   * 畫面上是最後留白，每一欄都是使用者拉的寬度（不會被平均撐開）。
   */
  readonly hasFiller?: boolean;
  /** 照內容量欄寬時從這裡找到表格（useMeasuredWidths）。 */
  readonly ref?: Ref<HTMLTableColElement>;
}

/**
 * 表格的 colgroup（欄寬固定的表格，docs/DESIGN.md 7.1「欄寬」）：拉過的欄寬是執行時才知道的值，寫在 col 的 style（第 13 節）。
 */
export function TableColumns({ columns, hasFiller = false, ref }: TableColumnsProps) {
  return (
    <colgroup ref={ref}>
      {columns.map((column) => (
        <col
          key={column.key}
          className={column.className}
          style={column.width === undefined ? undefined : { width: `${String(column.width)}px` }}
        />
      ))}
      {hasFiller && <col />}
    </colgroup>
  );
}
