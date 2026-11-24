import type { CSSProperties, ReactNode } from 'react';
import { cx } from '../../lib/cx';
import styles from './Timeline.module.css';

interface AtProps {
  /** 從畫面的第幾天開始（0 起算）、佔幾天。 */
  readonly col?: number;
  readonly span?: number;
  /** 畫面一共幾天：放在最外層，表頭、每一列、格線的寬度都靠它。 */
  readonly days?: number;
  readonly className?: string;
  /** 滑過時的提示（被截斷的名稱）：表頭是 aria-hidden，滑鼠照樣看得到。 */
  readonly title?: string;
  /** 給樣式選的屬性（data-*）。 */
  readonly attrs?: Readonly<Record<`data-${string}`, boolean | string | undefined>>;
  readonly children?: ReactNode;
}

type Vars = CSSProperties & Readonly<Record<`--${string}`, number>>;

/**
 * 時間軸上依日期排的東西（條、表頭的格子、里程碑的線）：位置與寬度是執行時才算得出來的天數，
 * class 表達不了，所以只設 CSS 變數，長相與換算（一天多寬）都寫在 Timeline.module.css（docs/DESIGN.md 第 13 節）。
 * 這個檔案是時間軸唯一用 style 的地方。
 */
export function At({ col, span, days, className, title, attrs, children }: AtProps) {
  const vars: Vars = {
    ...(col !== undefined && { '--col': col }),
    ...(span !== undefined && { '--span': span }),
    ...(days !== undefined && { '--days': days }),
  };
  return (
    <div className={cx(col !== undefined && styles.at, className)} style={vars} title={title} {...attrs}>
      {children}
    </div>
  );
}
