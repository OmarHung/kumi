import { useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { cx } from '../../lib/cx';
import { isComposingKey } from '../../lib/keyboard';
import { isRightToLeft } from '../../lib/rovingGrid';
import { useDragResize, type DragStart, type DragTarget } from '../../lib/useDragResize';
import { fitColumnWidth } from './columnFit';
import {
  clampWidth,
  DEFAULT_BOUNDS,
  remToPx,
  RESIZE_STEP,
  useColumnWidths,
  type ColumnWidthStore,
  type WidthBounds,
} from './columnWidths';
import styles from './Table.module.css';

/*
 * 可以調寬的表頭（docs/DESIGN.md 7.1「欄寬」、8.3「清單的鍵盤操作」）：
 * - 每一欄的右緣是把手：拖曳調寬，點兩下照內容調整。
 * - 鍵盤：焦點在表頭那一格（排序按鈕或格子自己）時 Shift+←／→ 一次 1rem，Shift+Enter 照內容調整。
 *   把手不是 Tab 停駐點：整張表格只有一個停駐點（8.1 第 8 點），表頭本來就走得到。所以把手對報讀軟體是隱藏的
 *   （aria-hidden）：不能聚焦的分隔線，瀏覽模式逐格讀表頭時每一欄都多唸一次「分隔線」；鍵盤的做法寫在表格的說明裡。
 */

/** 一欄的寬度記在哪裡、可以調多少。 */
export interface ColumnSizing {
  /** 欄位 id（store 裡的鍵）。 */
  readonly id: string;
  readonly store: ColumnWidthStore;
  readonly bounds?: WidthBounds;
  /** 照內容調整時量哪些格子：預設是這一欄所在的表格；好幾張表格共用欄寬時（我的工作）給包住它們的容器。 */
  readonly scopeRef?: RefObject<HTMLElement | null>;
}

type ResizeAction = 'narrower' | 'wider' | 'fit';

/** 調寬的按鍵：只按著 Shift 的左右鍵與 Enter（表格的方向鍵不處理帶 Shift 的，8.3）。左右照畫面的文字方向。 */
function resizeActionOf(event: KeyboardEvent, isRtl: boolean): ResizeAction | null {
  if (!event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) return null;
  if (event.key === 'Enter') return 'fit';
  if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return null;
  return (event.key === 'ArrowRight') !== isRtl ? 'wider' : 'narrower';
}

/** 表頭這一格畫出來的寬度（ResizeObserver 跟著變）；沒有 ResizeObserver 的環境（測試）是 undefined。 */
function useRenderedWidth(cellRef: RefObject<HTMLTableCellElement | null>): number | undefined {
  const [width, setWidth] = useState<number>();
  useLayoutEffect(() => {
    const cell = cellRef.current;
    if (cell === null || typeof ResizeObserver !== 'function') return undefined;
    const update = () => {
      setWidth(Math.round(cell.getBoundingClientRect().width));
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(cell);
    return () => {
      observer.disconnect();
    };
  }, [cellRef]);
  return width;
}

interface ColumnResize extends DragTarget {
  readonly valueNow: number | undefined;
  readonly fit: () => void;
  readonly handleKeyDown: (event: KeyboardEvent<HTMLTableCellElement>) => void;
}

/**
 * 拖曳時記多寬：被夾住的欄（開始時畫出來的比記住的窄：拉寬的固定標題欄在變窄的表格裡）往寬拉，畫面上寬不了，
 * 記住的也不能被改小——取記住的與拖到的較大的。拉得比開始畫出來的窄，照拖到的記。
 */
function draggedWidth(width: number, start: DragStart | undefined): number {
  const kept = start?.userWidth;
  if (start === undefined || kept === undefined || kept <= start.width || width < start.width) return width;
  return Math.max(kept, width);
}

/**
 * 鍵盤一次調一步：調寬從畫出來的與記住的較大的起算（被夾住的欄不會把記住的改小），調窄從畫出來的起算。
 * 上限是這一欄的上限與現在畫出來的寬度較大的：我的工作沒拉過的標題欄比上限寬，一按就跳回上限不對。
 */
function steppedWidth(isWider: boolean, rendered: number, kept: number | undefined, min: number, max: number): number {
  const step = remToPx(RESIZE_STEP);
  const next = isWider ? Math.max(rendered, kept ?? 0) + step : rendered - step;
  return Math.round(Math.min(Math.max(next, min), Math.max(max, rendered)));
}

function useColumnResize(cellRef: RefObject<HTMLTableCellElement | null>, sizing: ColumnSizing): ColumnResize {
  const { id, store, bounds = DEFAULT_BOUNDS, scopeRef } = sizing;
  const stored = useColumnWidths(store)[id];
  // 記住的寬度畫在範圍裡（localStorage 可能被改壞）：只在量不到畫出來的寬度時用（測試）。
  const storedInRange = stored === undefined ? undefined : clampWidth(stored, bounds);
  const rendered = useRenderedWidth(cellRef);
  const min = remToPx(bounds.min);
  const max = remToPx(bounds.max);
  // 從畫出來的寬度算起：被夾住的固定欄（表格變窄時）畫出來的比記住的窄。
  const current = () => {
    const width = cellRef.current?.getBoundingClientRect().width ?? 0;
    return width > 0 ? width : (storedInRange ?? min);
  };
  const userWidth = () => store.getSnapshot().user[id];
  const save = (width: number, isFinal: boolean) => {
    store.setWidth(id, Math.round(width), isFinal);
  };
  const fit = () => {
    const cell = cellRef.current;
    const scope = scopeRef?.current ?? cell?.closest('table') ?? null;
    if (cell !== null && scope !== null) save(clampWidth(fitColumnWidth(scope, cell.cellIndex), bounds), true);
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLTableCellElement>) => {
    const cell = cellRef.current;
    if (cell === null || event.defaultPrevented || isComposingKey(event)) return;
    const action = resizeActionOf(event, isRightToLeft(cell));
    if (action === null) return;
    // 排序按鈕上的 Shift+Enter 不能變成按下它。
    event.preventDefault();
    if (action === 'fit') fit();
    else save(steppedWidth(action === 'wider', current(), userWidth(), min, max), true);
  };
  return {
    valueNow: rendered ?? storedInRange,
    min,
    // 比上限寬的欄（我的工作沒拉過的標題欄），上限就是它現在的寬度：aria-valuenow 不會大於 aria-valuemax。
    max: Math.max(max, rendered ?? 0),
    current,
    userWidth,
    // 拖曳已經夾在 [min, max] 裡（lib/useDragResize.ts）。
    apply: (width, isFinal, start) => {
      save(draggedWidth(width, start), isFinal);
    },
    restore: (width) => {
      store.setWidth(id, width, false);
    },
    fit,
    handleKeyDown,
  };
}

function ColumnResizer({ label, resize }: { readonly label: string; readonly resize: ColumnResize }) {
  const { isDragging, ...handlers } = useDragResize(resize);
  return (
    // aria-hidden：見檔頭。角色、名稱與數值留著（開發與測試看得到它在調哪一欄）。
    <div
      role="separator"
      aria-hidden="true"
      aria-orientation="vertical"
      aria-label={`調整「${label}」欄寬`}
      aria-valuenow={resize.valueNow}
      aria-valuemin={Math.round(resize.min)}
      aria-valuemax={Math.round(resize.max)}
      className={styles.resizer}
      data-dragging={isDragging || undefined}
      onDoubleClick={resize.fit}
      {...handlers}
    />
  );
}

interface ResizableHeaderProps {
  /** 欄名：沒有 children 時顯示的字（一行，放不下用省略號，完整的欄名在 title）。 */
  readonly label: string;
  readonly sizing: ColumnSizing;
  readonly className?: string;
  readonly ariaSort?: 'ascending' | 'descending';
  /** 表頭的內容（排序按鈕）；沒給就是欄名。 */
  readonly children?: ReactNode;
}

/** 可以調寬的欄標頭（th scope="col"）：名稱是裡面的欄名（把手是 aria-hidden，不會算進來）。 */
export function ResizableHeader({ label, sizing, className, ariaSort, children }: ResizableHeaderProps) {
  const cellRef = useRef<HTMLTableCellElement>(null);
  const resize = useColumnResize(cellRef, sizing);
  return (
    <th
      ref={cellRef}
      scope="col"
      aria-sort={ariaSort}
      className={cx(styles.resizable, className)}
      onKeyDown={resize.handleKeyDown}
    >
      {children ?? (
        <span className={styles.headerLabel} title={label}>
          {label}
        </span>
      )}
      <ColumnResizer label={label} resize={resize} />
    </th>
  );
}
