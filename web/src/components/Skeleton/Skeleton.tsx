import styles from './Skeleton.module.css';

export interface SkeletonProps {
  /** 給報讀軟體的文字。 */
  readonly label?: string;
  readonly rows?: number;
  /** rows：表格與清單的列（一列 --row-height）；text：一段文字。 */
  readonly variant?: 'rows' | 'text';
}

const DEFAULT_ROWS = 3;

/**
 * 第一次載入時的骨架：形狀和要出現的內容一樣，載入完不會跳動。
 * 看得到的只有形狀；「載入中…」留給報讀軟體（docs/DESIGN.md 第 7 節）。
 */
export function Skeleton({ label = '載入中…', rows = DEFAULT_ROWS, variant = 'rows' }: SkeletonProps) {
  return (
    <div role="status" className={styles.skeleton} data-variant={variant}>
      <span className="visually-hidden">{label}</span>
      {Array.from({ length: rows }, (_, index) => (
        <span key={index} className={styles.bar} aria-hidden="true" />
      ))}
    </div>
  );
}
