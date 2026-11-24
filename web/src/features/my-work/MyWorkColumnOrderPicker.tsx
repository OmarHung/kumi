import { useId, useState } from 'react';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { Popover } from '../../components/Popover/Popover';
import { updateThenFocus } from '../../lib/updateThenFocus';
import toolbarStyles from '../item-table/ItemToolbar.module.css';
import { stepColumn, useColumnOrder, type ColumnOrderStore } from './myWorkColumnOrder';
import { myWorkColumns, type MyWorkColumn, type MyWorkColumnId } from './myWorkColumns';
import styles from './MyWork.module.css';

interface MyWorkColumnOrderPickerProps {
  readonly orderStore: ColumnOrderStore;
  /** 依專案分組時沒有專案欄：清單裡也不列。 */
  readonly showProject: boolean;
}

interface MoveButtonProps {
  readonly column: MyWorkColumn;
  readonly step: -1 | 1;
  /** 移不了（已經在最前、最後，或越過之後不符合規則）是 null。 */
  readonly next: readonly MyWorkColumnId[] | null;
  readonly id: string;
  /** 移不了時 next 是 null（呼叫端說明為什麼）。 */
  readonly onMove: (next: readonly MyWorkColumnId[] | null) => void;
}

/**
 * 往前、往後移一欄。移不了時 aria-disabled（停駐點留著，第 7 節）：剛移到最前面時焦點不會掉；
 * 按了照樣交給呼叫端，唸出為什麼移不了（報讀軟體按了才知道）。
 */
function MoveButton({ column, step, next, id, onMove }: MoveButtonProps) {
  const word = step === -1 ? '往前移' : '往後移';
  return (
    <IconButton
      id={id}
      icon={step === -1 ? 'arrowUp' : 'arrowDown'}
      label={`把「${column.label}」${word}`}
      size="sm"
      aria-disabled={next === null || undefined}
      onClick={() => {
        onMove(next);
      }}
    />
  );
}

/** 移不了的原因：已經在最前、最後，或越過之後標題左邊會有放不下的欄。 */
function blockedMessage(column: MyWorkColumn, step: -1 | 1, columns: readonly MyWorkColumn[]): string {
  const index = columns.findIndex(({ id }) => id === column.id);
  if (step === -1 && index === 0) return `「${column.label}」已經在最前面。`;
  if (step === 1 && index === columns.length - 1) return `「${column.label}」已經在最後面。`;
  const word = step === -1 ? '往前' : '往後';
  return `「${column.label}」不能再${word}移：標題和它左邊的欄固定在左邊，只放得下編號與專案。`;
}

/**
 * 給 status 的一句話。同一句再說一次時（連按兩次）文字要有變化，報讀軟體才會再唸：交替在後面加一個零寬的空白。
 */
function useAnnouncement(): [string, (text: string) => void] {
  const [state, setState] = useState({ text: '', serial: 0 });
  const announce = (text: string) => {
    setState((previous) => ({ text, serial: previous.serial + 1 }));
  };
  const shown = state.text === '' || state.serial % 2 === 1 ? state.text : `${state.text}\u200B`;
  return [shown, announce];
}

interface OrderListProps {
  readonly order: readonly MyWorkColumnId[];
  readonly columns: readonly MyWorkColumn[];
  readonly buttonId: (column: MyWorkColumnId, step: -1 | 1) => string;
  readonly onMove: (column: MyWorkColumn, step: -1 | 1, next: readonly MyWorkColumnId[] | null) => void;
}

/** 一欄一行，由上到下就是表格由左到右。 */
function OrderList({ order, columns, buttonId, onMove }: OrderListProps) {
  const visible = columns.map((column) => column.id);
  return (
    <ol className={styles.orderList}>
      {columns.map((column) => (
        <li key={column.id} className={styles.orderItem}>
          <span className={styles.orderLabel}>{column.label}</span>
          {([-1, 1] as const).map((step) => (
            <MoveButton
              key={step}
              column={column}
              step={step}
              next={stepColumn(order, visible, column.id, step)}
              id={buttonId(column.id, step)}
              onMove={(next) => {
                onMove(column, step, next);
              }}
            />
          ))}
        </li>
      ))}
    </ol>
  );
}

/**
 * 「欄的順序」（docs/DESIGN.md 8.3「我的工作」）：拖表頭之外的另一條路——鍵盤、觸控與不方便拖曳的人用（WCAG 2.5.7）。
 * 一欄一行，每一行有往前、往後移；最下面「回到預設的順序」（按了不會不見、焦點留著）。
 * 按了焦點跟著那一欄的同一顆按鈕走（可以一直按下去），移到哪裡由 status 唸出來。
 */
export function MyWorkColumnOrderPicker({ orderStore, showProject }: MyWorkColumnOrderPickerProps) {
  const { order } = useColumnOrder(orderStore);
  const columns = myWorkColumns(order, showProject);
  const baseId = useId();
  const [status, announce] = useAnnouncement();
  const buttonId = (column: MyWorkColumnId, step: -1 | 1) => `${baseId}-${column}-${step === -1 ? 'up' : 'down'}`;
  const move = (column: MyWorkColumn, step: -1 | 1, next: readonly MyWorkColumnId[] | null) => {
    if (next === null) {
      announce(blockedMessage(column, step, columns));
      return;
    }
    updateThenFocus(() => {
      orderStore.set(next);
    }, [buttonId(column.id, step)]);
    const position = myWorkColumns(next, showProject).findIndex(({ id }) => id === column.id) + 1;
    announce(`「${column.label}」移到第 ${String(position)} 欄。`);
  };
  return (
    <Popover
      label="欄的順序"
      align="end"
      renderTrigger={(trigger) => (
        <Button {...trigger} size="sm" title="欄的順序">
          <Icon name="columns" />
          <span className={toolbarStyles.pickerLabel}>欄的順序</span>
        </Button>
      )}
    >
      <p className={styles.orderNote}>由上到下是表格由左到右，也可以直接拖表頭。標題和它左邊的欄固定在左邊，只放得下編號與專案。</p>
      <OrderList order={order} columns={columns} buttonId={buttonId} onMove={move} />
      <p role="status" className="visually-hidden">
        {status}
      </p>
      <div className={toolbarStyles.pickerFooter}>
        <Button
          size="sm"
          onClick={() => {
            orderStore.reset();
            announce('已回到預設的順序。');
          }}
        >
          回到預設的順序
        </Button>
      </div>
    </Popover>
  );
}
