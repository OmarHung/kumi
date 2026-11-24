import { useLayoutEffect, useRef, type RefObject } from 'react';
import { isFocusLost } from '../../lib/updateThenFocus';
import { RESTORE_KEY_ATTRIBUTE } from './RestoreButton';

/** 復原之後焦點的去處：某一列的「復原」，或頁面標題（沒有別列了）。 */
type FocusTarget = { readonly rowKey: string } | { readonly heading: true };

function restoreButtonOf(container: HTMLElement | null, itemKey: string): HTMLElement | null {
  return container?.querySelector<HTMLElement>(`[${RESTORE_KEY_ATTRIBUTE}="${CSS.escape(itemKey)}"]`) ?? null;
}

interface RestoreFocus {
  /** 一列拿掉之前呼叫：焦點要去的那一列的編號，沒有別列就是 null（去標題）。 */
  readonly planFocus: (focusKey: string | null) => void;
  /** 把焦點放到某一列的「復原」（「先復原上層」的連結用）。 */
  readonly focusRestoreButton: (itemKey: string) => void;
}

/**
 * 復原一列、那一列拿掉之後把焦點接手過去（下一列、上一列、標題）。畫面更新之後才放；
 * 焦點已經在別處（使用者自己移走了）就不搶。dataVersion 是清單的資料：變了才檢查要不要接手。
 */
export function useRestoreFocus(
  listRef: RefObject<HTMLElement | null>,
  headingRef: RefObject<HTMLElement | null>,
  dataVersion: unknown,
): RestoreFocus {
  const pending = useRef<FocusTarget | null>(null);
  useLayoutEffect(() => {
    const target = pending.current;
    if (target === null) return;
    pending.current = null;
    if (!isFocusLost()) return;
    if ('heading' in target) headingRef.current?.focus();
    else restoreButtonOf(listRef.current, target.rowKey)?.focus();
  }, [dataVersion, headingRef, listRef]);
  return {
    planFocus: (focusKey) => {
      pending.current = focusKey === null ? { heading: true } : { rowKey: focusKey };
    },
    focusRestoreButton: (itemKey) => {
      restoreButtonOf(listRef.current, itemKey)?.focus();
    },
  };
}
