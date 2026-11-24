import { useLayoutEffect, useRef, type RefObject } from 'react';
import { isFocusLost } from '../../lib/updateThenFocus';

/** 每一列的主要動作（刪除、取消、移除）標這個屬性，值是那一列的編號：焦點接手時照它找。 */
export const ROW_ACTION_ATTRIBUTE = 'data-row-action';

function actionOf(list: HTMLElement | null, rowId: string): HTMLElement | null {
  return list?.querySelector<HTMLElement>(`[${ROW_ACTION_ATTRIBUTE}="${CSS.escape(rowId)}"]`) ?? null;
}

/** 這一列的下一列、沒有就上一列的編號；都沒有是 null。 */
function neighborOf(list: HTMLElement | null, rowId: string): string | null {
  const rows = Array.from(list?.querySelectorAll<HTMLElement>('li') ?? []);
  const index = rows.findIndex((row) => row.querySelector(`[${ROW_ACTION_ATTRIBUTE}="${CSS.escape(rowId)}"]`) !== null);
  if (index === -1) return null;
  const neighbor = rows[index + 1] ?? rows[index - 1];
  return neighbor?.querySelector(`[${ROW_ACTION_ATTRIBUTE}]`)?.getAttribute(ROW_ACTION_ATTRIBUTE) ?? null;
}

interface Plan {
  readonly removedId: string;
  /** null：沒有別列了，去 fallback。 */
  readonly neighborId: string | null;
}

export interface RemovalFocus {
  /** 一列拿掉之前呼叫：焦點之後要去下一列、上一列的動作鈕，都沒有就去「上傳檔案」。 */
  readonly planRemoval: (rowId: string) => void;
  /** 這一列換成了另一列（上傳完成換成正式的列）：焦點在那一列的動作鈕上時，換過去之後接到新的那一列。 */
  readonly planReplacement: (rowId: string, replacementId: string) => void;
}

/**
 * 附件的某一列拿掉（刪除、取消、移除）之後，焦點接手過去（docs/design/08-interaction.md 8.3「項目的附件」）：
 * 按的按鈕跟著列不見，焦點不接手就掉回頁面本體。畫面更新之後才放；焦點已經在別處（使用者自己移走了）就不搶。
 * rowIds 是現在畫出來的每一列的編號：那一列真的不在了才動，中途因為別的列變動重畫不會把計畫用掉。
 */
export function useRemovalFocus(
  listRef: RefObject<HTMLElement | null>,
  fallbackRef: RefObject<HTMLElement | null>,
  rowIds: readonly string[],
): RemovalFocus {
  const pending = useRef<Plan | null>(null);
  const signature = rowIds.join('\n');
  useLayoutEffect(() => {
    const plan = pending.current;
    if (plan === null || signature.split('\n').includes(plan.removedId)) return;
    pending.current = null;
    if (!isFocusLost()) return;
    const target = plan.neighborId === null ? null : actionOf(listRef.current, plan.neighborId);
    (target ?? fallbackRef.current)?.focus();
  }, [signature, listRef, fallbackRef]);
  return {
    planRemoval: (rowId) => {
      pending.current = { removedId: rowId, neighborId: neighborOf(listRef.current, rowId) };
    },
    planReplacement: (rowId, replacementId) => {
      const isFocusedHere = document.activeElement?.getAttribute(ROW_ACTION_ATTRIBUTE) === rowId;
      if (isFocusedHere) pending.current = { removedId: rowId, neighborId: replacementId };
    },
  };
}
