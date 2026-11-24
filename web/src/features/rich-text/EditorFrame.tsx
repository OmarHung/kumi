import type { DragEventHandler, ReactNode, Ref } from 'react';
import { Icon } from '../../components/Icon/Icon';
import { cx } from '../../lib/cx';
import type { CountNote } from './fieldText';
import styles from './RichTextEditor.module.css';

export interface EditorFrameProps {
  readonly id: string;
  readonly label: string;
  readonly isLabelHidden: boolean;
  readonly rows: number;
  readonly hint: string;
  readonly formatHint: string;
  readonly count: CountNote | null;
  readonly error: string | null;
  readonly isCollapsed: boolean;
  readonly isDropping: boolean;
  readonly isBusy: boolean;
  /** 高度由呼叫端定（填滿畫面）：內容區不設最高。 */
  readonly fillsViewport?: boolean;
  /** 外框本身（提及的清單浮在它旁邊）。 */
  readonly frameRef?: Ref<HTMLDivElement>;
  readonly dragHandlers?: {
    readonly onDragEnter: DragEventHandler<HTMLDivElement>;
    readonly onDragOver: DragEventHandler<HTMLDivElement>;
    readonly onDragLeave: DragEventHandler<HTMLDivElement>;
    readonly onDrop: DragEventHandler<HTMLDivElement>;
  };
  /** 點標籤要做的事（把焦點放進編輯器）。 */
  readonly onLabelClick?: () => void;
  /** 點在內容區自己的空白處（內距、編輯器沒有撐滿的地方）。 */
  readonly onScrollClick?: () => void;
  /** 浮在別處或看不見的東西（提及的清單、給報讀軟體的即時說明）。 */
  readonly overlay?: ReactNode;
  readonly toolbar: ReactNode;
  readonly children: ReactNode;
}

/** 讓內容區至少有 rows 行高：看不見的幾行空白和編輯器疊在同一格，內容比較長時由內容撐高。 */
function Sizer({ rows }: { readonly rows: number }) {
  return (
    <div className={styles.sizer} aria-hidden="true">
      {Array.from({ length: rows }, (_, index) => (
        <span key={index} className={styles.sizerLine}>
          {' '}
        </span>
      ))}
    </div>
  );
}

/**
 * 編輯器的外框（和原本的文字框同一套外框、焦點環、錯誤色、拖檔案時的樣子）：標籤、工具列加內容、提示、字數、錯誤。
 * 延後載入下載好之前的備用畫面用同一個外框，編輯器出現時版面不跳。
 */
export function EditorFrame(props: EditorFrameProps) {
  const { id, label, isLabelHidden, rows, hint, formatHint, count, error, isCollapsed, isDropping, isBusy, fillsViewport = false } = props;
  const { frameRef, dragHandlers, onLabelClick, onScrollClick, toolbar, overlay, children } = props;
  return (
    <div className={styles.field} data-collapsed={isCollapsed} data-dropping={isDropping || undefined} data-busy={isBusy || undefined} data-fills={fillsViewport || undefined}>
      <span id={`${id}-label`} className={cx(styles.label, isLabelHidden && 'visually-hidden')} onClick={onLabelClick}>
        {label}
      </span>
      <div ref={frameRef} className={styles.frame} data-invalid={error !== null || count?.isOver === true || undefined} {...dragHandlers}>
        {toolbar}
        <div
          className={styles.scroll}
          data-editor-scroll=""
          onClick={(event) => {
            if (event.target === event.currentTarget) onScrollClick?.();
          }}
        >
          <Sizer rows={rows} />
          <div className={styles.surface}>{children}</div>
        </div>
      </div>
      <div className={styles.hints}>
        <span id={`${id}-hint`}>{hint}</span>
        <span id={`${id}-format`}>{formatHint}</span>
        {count !== null && (
          <span id={`${id}-remaining`} className={styles.remaining} data-over={count.isOver || undefined}>
            {count.text}
          </span>
        )}
      </div>
      {overlay}
      {error !== null && (
        <p id={`${id}-error`} role="alert" className={styles.error}>
          <Icon name="error" className={styles.errorIcon} />
          <span>{error}</span>
        </p>
      )}
    </div>
  );
}
