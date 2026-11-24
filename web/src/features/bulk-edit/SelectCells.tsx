import { useLayoutEffect, useRef, type ChangeEvent } from 'react';
import type { Item } from '../../api/types';
import { useSelection } from './SelectionContext';
import styles from './BulkEdit.module.css';

/** Shift＋點：native event 是 click（勾選框的 change 由 click 觸發）。 */
function isShiftClick(event: ChangeEvent<HTMLInputElement>): boolean {
  const { nativeEvent } = event;
  return nativeEvent instanceof MouseEvent && nativeEvent.shiftKey;
}

/**
 * 表格第一欄每一列的勾選框（docs/DESIGN.md 7.1「多選」）：名稱「選取 AGY-3」。
 * 整格都可以點（label 撐滿格子）；是表格鍵盤操作的一格，空白鍵切換。Shift＋點選起一整段。
 */
export function SelectCell({ item }: { readonly item: Item }) {
  const selection = useSelection();
  if (selection === null) return null;
  return (
    <label className={styles.selectLabel}>
      <input
        type="checkbox"
        aria-label={`選取 ${item.key}`}
        checked={selection.isSelected(item.key)}
        onChange={(event) => {
          selection.toggle(item.key, isShiftClick(event));
        }}
      />
    </label>
  );
}

/** 表頭的勾選框「選取全部已載入的項目」：部分選取時是 indeterminate。取消選取之後焦點回到這裡。 */
export function SelectAllCheckbox() {
  const selection = useSelection();
  const inputRef = useRef<HTMLInputElement>(null);
  const level = selection?.level ?? 'none';
  // indeterminate 只能用程式設，不是屬性。
  useLayoutEffect(() => {
    if (inputRef.current !== null) inputRef.current.indeterminate = level === 'some';
  }, [level]);
  if (selection === null) return null;
  return (
    <label className={styles.selectLabel}>
      <input
        ref={inputRef}
        id={selection.headerId}
        type="checkbox"
        aria-label="選取全部已載入的項目"
        checked={level === 'all'}
        onChange={selection.toggleAll}
      />
    </label>
  );
}
