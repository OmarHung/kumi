import { useEffect, useLayoutEffect, useRef, type KeyboardEvent, type MouseEvent, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { focusableIn, trapStopsIn } from '../../lib/focusable';
import { isComposingKey } from '../../lib/keyboard';
import styles from './Dialog.module.css';
import { INERT_EXEMPT_ATTRIBUTE } from './inertExempt';

// Tab 走到最後一個時繞回第一個，反之亦然；焦點不會跑到對話框後面的畫面。
// 頭尾不算下拉的篩選框（trapStopsIn）：從篩選框按 Tab 時焦點先回到下拉的按鈕，按鈕才是那個位置。
function keepFocusInside(event: KeyboardEvent<HTMLElement>, container: HTMLElement): void {
  const elements = trapStopsIn(container);
  const first = elements[0];
  const last = elements.at(-1);
  if (first === undefined || last === undefined) {
    event.preventDefault();
    return;
  }
  const active = document.activeElement;
  if (event.shiftKey && (active === first || active === container)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && active === last) {
    event.preventDefault();
    first.focus();
  }
}

/** 打開時焦點移到第一個可操作的元素（裡面已經有人拿了焦點就不搶），關閉後回到打開它的那個元素，鍵盤使用者才不會迷路。 */
function useFocusWhileOpen(panelRef: RefObject<HTMLElement | null>, returnsFocus: boolean): void {
  useEffect(() => {
    const panel = panelRef.current;
    // 裡面的元件（例如面板的標題）掛上時會先拿走焦點，那時 activeElement 已經在裡面，不是打開它的元素。
    const active = document.activeElement;
    const opener = active instanceof HTMLElement && !panel?.contains(active) ? active : null;
    if (panel && !panel.contains(document.activeElement)) {
      (focusableIn(panel)[0] ?? panel).focus();
    }
    return () => {
      if (returnsFocus) opener?.focus();
    };
  }, [panelRef, returnsFocus]);
}

/**
 * 背後的頁面不能操作也不能用 Tab 走到：把 body 底下已經有的內容標成 inert（之後才開的浮層、對話框不受影響；
 * 帶 INERT_EXEMPT_ATTRIBUTE 的跳過：新通知的提示早於視窗出現時也不被標成 inert）。
 * 用 layout effect：卸下時要先解除，呼叫端接著才放得回焦點（inert 的元素拿不到焦點）。
 */
function useInertBackground(backdropRef: RefObject<HTMLElement | null>, isEnabled: boolean): void {
  useLayoutEffect(() => {
    if (!isEnabled) return undefined;
    const marked = Array.from(document.body.children).filter(
      (element) => element !== backdropRef.current && !element.hasAttribute('inert') && !element.hasAttribute(INERT_EXEMPT_ATTRIBUTE),
    );
    marked.forEach((element) => {
      element.setAttribute('inert', '');
    });
    return () => {
      marked.forEach((element) => {
        element.removeAttribute('inert');
      });
    };
  }, [backdropRef, isEnabled]);
}

/**
 * 背景（對話框外面）的滑鼠處理。按下時不讓焦點離開：否則焦點掉到頁面本體，之後的按鍵不經過對話框，會交給背後的畫面。
 * 按下與放開都在背景才算「點背景」：在面板裡選字、拖到外面放開不能關掉。
 */
function useBackdropPress(onBackdropClick: (() => void) | undefined) {
  const pressedBackdrop = useRef(false);
  return {
    onMouseDown: (event: MouseEvent<HTMLDivElement>) => {
      pressedBackdrop.current = event.target === event.currentTarget;
      if (pressedBackdrop.current) event.preventDefault();
    },
    // 放開在視窗裡面：click 仍然發在共同祖先（背景），所以要在放開時就記下來。
    onMouseUp: (event: MouseEvent<HTMLDivElement>) => {
      if (event.target !== event.currentTarget) pressedBackdrop.current = false;
    },
    onClick: (event: MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget && pressedBackdrop.current) onBackdropClick?.();
      pressedBackdrop.current = false;
    },
  };
}

/**
 * 背景上的滾輪與觸控移動不往背後的頁面傳：遮罩蓋住的區域捲動會鏈到背後的清單（overflow: hidden 與 inert 都擋不住）。
 * 只擋目標是背景本身的：視窗裡面自己的捲動照常（捲動容器用 overscroll-behavior: contain，不會鏈出來）。
 * 要非 passive 的原生監聽才能 preventDefault，React 的 onWheel 是 passive。
 */
function useBackdropScrollLock(backdropRef: RefObject<HTMLElement | null>, isEnabled: boolean): void {
  useEffect(() => {
    const backdrop = backdropRef.current;
    if (!isEnabled || backdrop === null) return undefined;
    const block = (event: Event) => {
      if (event.target === backdrop) event.preventDefault();
    };
    backdrop.addEventListener('wheel', block, { passive: false });
    backdrop.addEventListener('touchmove', block, { passive: false });
    return () => {
      backdrop.removeEventListener('wheel', block);
      backdrop.removeEventListener('touchmove', block);
    };
  }, [backdropRef, isEnabled]);
}

/** 背後的頁面不能操作（inert）與不能捲動。 */
function useBackgroundGuards(backdropRef: RefObject<HTMLElement | null>, isInert: boolean, locksScroll: boolean): void {
  useInertBackground(backdropRef, isInert);
  useBackdropScrollLock(backdropRef, locksScroll);
}

/** 外框上的按鍵：Esc（有給 onEscape 時）與 Tab 繞圈。 */
function handleFrameKey(event: KeyboardEvent<HTMLDivElement>, panel: HTMLElement | null, onEscape: (() => void) | undefined): void {
  // 輸入法選字中的 Esc 是取消選字（注音、倉頡），不是關掉對話框：打到一半的內容不能跟著不見。
  if (onEscape !== undefined && event.key === 'Escape' && !isComposingKey(event)) {
    event.stopPropagation();
    onEscape();
  } else if (event.key === 'Tab' && panel !== null) {
    keepFocusInside(event, panel);
  }
}

export interface ModalFrameProps {
  /** 名稱：標題元素的 id。 */
  readonly labelledBy: string;
  readonly describedBy?: string;
  /** md、lg 是對話框的寬度；item 是項目的浮動視窗（置中、寬、高度最多一個畫面，自己捲）。 */
  readonly size?: 'md' | 'lg' | 'item';
  /** 按 Esc 時呼叫；沒給就不聽（Esc 由呼叫端用 lib/shortcuts.ts 登記，讓格子、選單先處理）。 */
  readonly onEscape?: () => void;
  /** 點背景（對話框外面）時呼叫；沒給就點了沒反應。 */
  readonly onBackdropClick?: () => void;
  /** false：關閉後不由這裡放回焦點，呼叫端自己放（項目的浮動視窗要回到清單上「現在」那一筆，不是打開時那一筆）。預設 true。 */
  readonly returnsFocus?: boolean;
  /** 背後的頁面標成 inert。預設 false：一般的對話框開在頁面上，只靠焦點陷阱。 */
  readonly isBackgroundInert?: boolean;
  /** 鎖住背後的捲動（背景上的滾輪、觸控移動不傳給背後的頁面）。預設 false。 */
  readonly locksBackgroundScroll?: boolean;
  /** true：快捷鍵（lib/shortcuts.ts）不因為這個對話框開著而停用，用在由呼叫端自己處理按鍵的浮動視窗。 */
  readonly allowsShortcuts?: boolean;
  /** true：右下角的 AI 助理（features/assistant-panel/useAssistantHost）開著時放進這個視窗裡，才用得到、也唸得到。項目的浮動視窗用。 */
  readonly hostsAssistant?: boolean;
  /** 設在外框上的 CSS 變數：只放執行時才知道的版面數值（項目的浮動視窗使用者拉的寬度 --floating-panel-width）。 */
  readonly variables?: Readonly<Record<`--${string}`, string>>;
  readonly children: ReactNode;
}

/**
 * 對話框的外殼：放進 body 的背景加上有 role="dialog"、aria-modal 的面板，焦點鎖在裡面（Tab 繞圈）、開關時焦點的去處。
 * Dialog 與項目的浮動視窗（components/SidePanel/FloatingPanel）共用，焦點陷阱只有這一份。掛上就是打開、卸下就是關閉。
 */
export function ModalFrame({
  labelledBy,
  describedBy,
  size = 'md',
  onEscape,
  onBackdropClick,
  returnsFocus = true,
  isBackgroundInert = false,
  locksBackgroundScroll = false,
  allowsShortcuts = false,
  hostsAssistant = false,
  variables,
  children,
}: ModalFrameProps) {
  const backdropRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const backdropPress = useBackdropPress(onBackdropClick);
  useFocusWhileOpen(panelRef, returnsFocus);
  useBackgroundGuards(backdropRef, isBackgroundInert, locksBackgroundScroll);

  return createPortal(
    <div
      ref={backdropRef}
      className={styles.backdrop}
      data-size={size}
      {...backdropPress}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        aria-describedby={describedBy}
        tabIndex={-1}
        data-size={size}
        data-shortcuts-through={allowsShortcuts || undefined}
        data-assistant-host={hostsAssistant || undefined}
        className={styles.panel}
        style={variables}
        onKeyDown={(event) => {
          handleFrameKey(event, panelRef.current, onEscape);
        }}
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}
