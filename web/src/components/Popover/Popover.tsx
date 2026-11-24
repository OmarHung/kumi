import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { focusableIn } from '../../lib/focusable';
import type { FloatingAlign } from '../../lib/useAnchoredPosition';
import { Floating } from '../Floating/Floating';
import { useFloatingDismiss } from '../Floating/useFloatingDismiss';
import styles from './Popover.module.css';

/** 要展開到觸發按鈕上的屬性。 */
export interface PopoverTriggerProps {
  readonly ref: RefObject<HTMLButtonElement | null>;
  readonly 'aria-expanded': boolean;
  readonly 'aria-controls': string | undefined;
  readonly onClick: () => void;
}

export interface PopoverProps {
  readonly renderTrigger: (props: PopoverTriggerProps) => ReactNode;
  /** 浮層的名稱（role="dialog" 的 aria-label），通常和按鈕的文字一樣。 */
  readonly label: string;
  /** 函式的寫法拿得到 close：裡面選了值就收起來，焦點回到按鈕（和按 Esc 一樣）。 */
  readonly children: ReactNode | ((api: PopoverApi) => ReactNode);
  readonly align?: FloatingAlign;
}

export interface PopoverApi {
  readonly close: () => void;
}

/** 打開時焦點到第一個可以操作的元素；裡面沒有的話到浮層本身。 */
function useFocusInside(isOpen: boolean, panelRef: RefObject<HTMLDivElement | null>) {
  useEffect(() => {
    const panel = panelRef.current;
    if (isOpen && panel !== null) {
      (focusableIn(panel)[0] ?? panel).focus({ preventScroll: true });
    }
  }, [isOpen, panelRef]);
}

function usePopover() {
  const [isOpen, setIsOpen] = useState(false);
  // 點外面關上的 effect 依賴它，要固定同一個函式，effect 才不會每次渲染都重新掛監聽。
  const close = useCallback(() => {
    setIsOpen(false);
  }, []);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const { rootRef, handleBlur, closeWhenAnchorHidden } = useFloatingDismiss(isOpen, close, triggerRef);
  useFocusInside(isOpen, panelRef);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && isOpen) {
      event.stopPropagation();
      close();
      triggerRef.current?.focus();
    }
  }

  const toggle = () => {
    setIsOpen((open) => !open);
  };
  const closeAndFocus = () => {
    close();
    triggerRef.current?.focus();
  };
  return { isOpen, triggerRef, panelRef, rootRef, toggle, handleKeyDown, handleBlur, closeWhenAnchorHidden, closeAndFocus };
}

/**
 * 不是動作清單的浮層（顯示的欄位、手機的篩選），從「顯示的欄位」抽出來。非強制的對話框：不鎖焦點，
 * Esc 關上焦點回到按鈕（不往外傳）；點在外面、Tab 或焦點移到外面、按鈕被捲出看得到的範圍都關上，
 * 焦點留在使用者去的地方。內容多的在窄螢幕改用 Dialog。
 */
export function Popover({ renderTrigger, label, children, align = 'start' }: PopoverProps) {
  const { isOpen, triggerRef, panelRef, rootRef, toggle, handleKeyDown, handleBlur, closeWhenAnchorHidden, closeAndFocus } = usePopover();
  const panelId = useId();
  return (
    <div ref={rootRef} className={styles.root} onKeyDown={handleKeyDown} onBlur={handleBlur}>
      {renderTrigger({ ref: triggerRef, 'aria-expanded': isOpen, 'aria-controls': isOpen ? panelId : undefined, onClick: toggle })}
      {isOpen && (
        <Floating anchorRef={triggerRef} align={align} onAnchorHidden={closeWhenAnchorHidden} className={styles.surface}>
          <div ref={panelRef} id={panelId} role="dialog" aria-label={label} tabIndex={-1} className={styles.panel}>
            {typeof children === 'function' ? children({ close: closeAndFocus }) : children}
          </div>
        </Floating>
      )}
    </div>
  );
}
