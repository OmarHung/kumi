import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type RefObject } from 'react';
import { useFloatingDismiss } from '../Floating/useFloatingDismiss';

/** 要展開到觸發按鈕上的屬性（ARIA 的 menu button）。 */
export interface MenuTriggerProps {
  readonly ref: RefObject<HTMLButtonElement | null>;
  readonly 'aria-haspopup': 'menu';
  readonly 'aria-expanded': boolean;
  readonly 'aria-controls': string | undefined;
  /** 停用時仍然停得到（焦點不會掉），只是打不開。 */
  readonly 'aria-disabled': true | undefined;
  readonly onClick: () => void;
  readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
}

type OpenFocus = 'first' | 'last';

/** 方向鍵在選項之間移動（不能用的也停得到，才聽得到原因），頭尾相接。 */
function nextIndex(key: string, current: number, count: number): number | null {
  switch (key) {
    case 'ArrowDown':
      return (current + 1) % count;
    case 'ArrowUp':
      return (current - 1 + count) % count;
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    default:
      return null;
  }
}

function menuItemsIn(list: HTMLElement | null): HTMLElement[] {
  return Array.from(list?.querySelectorAll<HTMLElement>('[role="menuitem"], [role="menuitemradio"]') ?? []);
}

/** 打開時焦點到第一個（在按鈕上按 ↑ 打開的到最後一個）選項。 */
function useInitialFocus(openFocus: OpenFocus | null, listRef: RefObject<HTMLUListElement | null>) {
  useEffect(() => {
    if (openFocus === null) return;
    const items = menuItemsIn(listRef.current);
    (openFocus === 'first' ? items[0] : items.at(-1))?.focus({ preventScroll: true });
  }, [openFocus, listRef]);
}

/**
 * 選單的狀態與鍵盤：方向鍵、Home、End 移動；Esc 關上回到按鈕（不往外傳，對話框才不會一起關）；
 * Tab 關上讓焦點照常往下走；點外面、焦點移到外面、按鈕被捲出看得到的範圍都關上（useFloatingDismiss）。
 */
export function useMenu(triggerRef: RefObject<HTMLButtonElement | null>, isDisabled: boolean, menuId: string) {
  const [openFocus, setOpenFocus] = useState<OpenFocus | null>(null);
  const isOpen = openFocus !== null;
  // 點外面關上的 effect 依賴它，要固定同一個函式，effect 才不會每次渲染都重新掛監聽。
  const close = useCallback(() => {
    setOpenFocus(null);
  }, []);
  const closeAndFocus = useCallback(() => {
    close();
    triggerRef.current?.focus();
  }, [close, triggerRef]);
  const { rootRef, handleBlur, closeWhenAnchorHidden } = useFloatingDismiss(isOpen, close, triggerRef);
  const listRef = useRef<HTMLUListElement>(null);
  useInitialFocus(openFocus, listRef);

  const triggerProps: MenuTriggerProps = {
    ref: triggerRef,
    'aria-haspopup': 'menu',
    'aria-expanded': isOpen,
    'aria-controls': isOpen ? menuId : undefined,
    'aria-disabled': isDisabled || undefined,
    onClick: () => {
      if (!isDisabled) setOpenFocus((current) => (current === null ? 'first' : null));
    },
    onKeyDown: (event) => {
      if (isDisabled || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) return;
      event.preventDefault();
      setOpenFocus(event.key === 'ArrowDown' ? 'first' : 'last');
    },
  };

  function handleListKeyDown(event: KeyboardEvent<HTMLUListElement>) {
    if (event.key === 'Escape') {
      event.stopPropagation();
      closeAndFocus();
      return;
    }
    if (event.key === 'Tab') {
      // 焦點先回到按鈕、不擋預設動作：瀏覽器從按鈕接著往下（Shift+Tab 往上）走，選單已經不在了。
      closeAndFocus();
      return;
    }
    const items = menuItemsIn(listRef.current);
    const index = nextIndex(event.key, items.findIndex((item) => item === document.activeElement), items.length);
    if (index !== null) {
      event.preventDefault();
      items[index]?.focus();
    }
  }

  return { isOpen, rootRef, listRef, triggerProps, handleListKeyDown, handleBlur, closeAndFocus, closeWhenAnchorHidden };
}
