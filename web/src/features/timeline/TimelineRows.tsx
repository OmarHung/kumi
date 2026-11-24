import { memo, useRef, type MouseEvent } from 'react';
import { Link } from 'react-router';
import { Icon } from '../../components/Icon/Icon';
import { useItemPanelLink, useItemSequence } from '../item-page/ItemPanelContext';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import { At } from './At';
import type { TimelineRow } from './timelineModel';
import styles from './Timeline.module.css';

/** 左右是不同的 DOM：哪一列正被滑過或取得焦點，由外層（TimelineGrid）記著，兩邊各自畫出對應的樣子。 */
export type OnRowActive = (itemKey: string | null) => void;

function clippedOf(row: TimelineRow): 'both' | 'start' | 'end' | undefined {
  const { isClippedStart, isClippedEnd } = row.placement;
  if (isClippedStart && isClippedEnd) return 'both';
  if (isClippedStart) return 'start';
  return isClippedEnd ? 'end' : undefined;
}

interface RowProps {
  readonly row: TimelineRow;
  readonly isActive: boolean;
  readonly onActive: OnRowActive;
}

const RowLabel = memo(function RowLabel({ row, isFirst, isActive, onActive }: RowProps & { readonly isFirst: boolean }) {
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(row.item.key);
  const key = row.item.key;
  return (
    <li className={styles.labelRow} data-content-start={isFirst || undefined}>
      <Link
        to={itemPagePath(row.item.projectKey, key)}
        state={entry}
        className={styles.label}
        aria-label={row.name}
        title={row.name}
        data-overdue={row.isOverdue}
        data-active={isActive}
        onMouseEnter={() => {
          onActive(key);
        }}
        onMouseLeave={() => {
          onActive(null);
        }}
        onFocus={() => {
          onActive(key);
        }}
        onBlur={() => {
          onActive(null);
        }}
        {...panelLink}
      >
        <span className={styles.key}>{row.item.key}</span>
        <span className={styles.title}>{row.item.title}</span>
      </Link>
    </li>
  );
});

/** 按下時不讓焦點落在 aria-hidden 的連結上（它不是停駐點）；click 照常，開面板。 */
function keepFocus(event: MouseEvent) {
  event.preventDefault();
}

/**
 * 時間軸上的條或點：和左邊那一筆是同一個連結，滑鼠點得到，但不是第二個 Tab 停駐點、報讀軟體也不重複唸
 * （名稱與日期在左邊那一筆）。截斷的那一邊畫箭頭，看得出前後還有。
 * 左邊那一筆取得焦點、被滑過、面板開著它時，這裡也有對應的樣子（data-active、data-selected）：右邊的條看得出是哪一列。
 */
const RowMark = memo(function RowMark({ row, isActive, onActive }: RowProps) {
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(row.item.key);
  const clipped = clippedOf(row);
  return (
    <At col={row.placement.col} span={row.placement.span} className={styles.mark}>
      <Link
        to={itemPagePath(row.item.projectKey, row.item.key)}
        state={entry}
        className={styles.markLink}
        aria-hidden="true"
        tabIndex={-1}
        title={row.title}
        onClick={panelLink.onClick}
        onMouseDown={keepFocus}
        onMouseEnter={() => {
          onActive(row.item.key);
        }}
        onMouseLeave={() => {
          onActive(null);
        }}
        data-testid={`mark-${row.item.key}`}
        data-kind={row.kind}
        data-state={row.item.stateCategory}
        data-overdue={row.isOverdue}
        data-active={isActive}
        data-selected={panelLink['aria-current'] === 'true'}
        data-clipped={clipped}
        data-clipped-start={row.placement.isClippedStart}
        data-clipped-end={row.placement.isClippedEnd}
      >
        {row.kind === 'bar' && (
          <span className={styles.markBody} data-mark-body>
            {row.placement.isClippedStart && <Icon name="chevronLeft" className={styles.edge} />}
            {row.placement.isClippedEnd && <Icon name="chevronRight" className={styles.edge} />}
          </span>
        )}
      </Link>
    </At>
  );
});

interface RowsProps {
  readonly rows: readonly TimelineRow[];
  readonly activeKey: string | null;
  readonly onActive: OnRowActive;
}

/** 左邊一欄（編號與標題）：每一列都是 --row-height，和右邊的列靠同樣的高度對齊。 */
export function RowLabels({ rows, activeKey, onActive }: RowsProps) {
  const listRef = useRef<HTMLUListElement>(null);
  // 項目面板的上一個、下一個走畫面上的順序。
  useItemSequence(
    rows.map((row) => row.item.key),
    listRef,
  );
  return (
    <ul ref={listRef} className={styles.labelList} aria-label="項目">
      {rows.map((row, index) => (
        <RowLabel key={row.item.key} row={row} isFirst={index === 0} isActive={activeKey === row.item.key} onActive={onActive} />
      ))}
    </ul>
  );
}

export function RowMarks({ rows, activeKey, onActive }: RowsProps) {
  return rows.map((row) => (
    <div key={row.item.key} className={styles.row}>
      <RowMark row={row} isActive={activeKey === row.item.key} onActive={onActive} />
    </div>
  ));
}
