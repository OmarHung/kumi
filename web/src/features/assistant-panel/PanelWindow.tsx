import { useEffect, useLayoutEffect, useState, type KeyboardEvent, type MouseEvent, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { INERT_EXEMPT_ATTRIBUTE } from '../../components/Dialog/inertExempt';
import { trapStopsIn } from '../../lib/focusable';
import { isComposingKey } from '../../lib/keyboard';
import { PANEL_ID } from './panelId';
import { PanelResizers } from './PanelResizers';
import { toastPlacement, type PanelSize, type Viewport } from './panelSize';
import { usePanelResize } from './usePanelResize';
import styles from './AssistantPanel.module.css';

function viewportNow(): Viewport {
  return { width: window.innerWidth, height: window.innerHeight };
}

function useViewport(): Viewport {
  const [viewport, setViewport] = useState(viewportNow);
  useEffect(() => {
    const update = () => {
      setViewport(viewportNow());
    };
    window.addEventListener('resize', update);
    return () => {
      window.removeEventListener('resize', update);
    };
  }, []);
  return viewport;
}

/**
 * 大小放在根元素的 CSS 變數上，不寫在面板自己的 style：新通知的提示要知道面板多大才讓得出位置（它們在 top layer，
 * 在 body 底下、不在面板裡面）。面板開著時也告訴它們讓到哪裡（data-assistant，規則見 docs/design/07-components.md）。
 */
function usePublishCorner(size: PanelSize, isOpen: boolean, isCompact: boolean): void {
  const viewport = useViewport();
  const placement = toastPlacement(size, viewport, isCompact);
  useLayoutEffect(() => {
    const root = document.documentElement;
    root.style.setProperty('--assistant-width', `${String(size.width)}px`);
    root.style.setProperty('--assistant-height', `${String(size.height)}px`);
    return () => {
      root.style.removeProperty('--assistant-width');
      root.style.removeProperty('--assistant-height');
    };
  }, [size.height, size.width]);
  useLayoutEffect(() => {
    if (!isOpen) return undefined;
    const root = document.documentElement;
    root.dataset.assistant = `panel-${placement}`;
    return () => {
      delete root.dataset.assistant;
    };
  }, [isOpen, placement]);
}

/** 底部分頁列的標記：全螢幕的面板開著時它要照樣按得到（features/shell/TabBar.tsx）。 */
export const TAB_BAR_ATTRIBUTE = 'data-tab-bar';

/** 從 start 一路往上到 body，每一層的兄弟（不含走過的那條路、不含已經 inert 或標了例外的）。 */
function siblingsAlong(start: Element | null, panel: Element | null): Element[] {
  const found: Element[] = [];
  let node = start;
  while (node !== null && node !== document.body) {
    const parent: Element | null = node.parentElement;
    if (parent === null) break;
    for (const sibling of Array.from(parent.children)) {
      const isExcluded = sibling === node || sibling.contains(panel) || sibling.hasAttribute('inert') || sibling.hasAttribute(INERT_EXEMPT_ATTRIBUTE);
      if (!isExcluded) found.push(sibling);
    }
    node = parent;
  }
  return found;
}

/**
 * 窄螢幕的全螢幕面板當成 modal：背後的內容不能操作（inert，和 ModalFrame 同一套：帶 INERT_EXEMPT_ATTRIBUTE 的跳過）、
 * 也不能捲動（根元素的 data-assistant-modal）。面板自己掛在 body 底下，才不會連自己一起 inert。
 * 底部分頁列在外框裡面（不是 body 的直接子元素），所以從它往上把「路上每一層的兄弟」標成 inert，而不是整個外框：分頁列留著能按。
 */
function useModalBackground(panelRef: RefObject<HTMLElement | null>, isModal: boolean): void {
  useLayoutEffect(() => {
    if (!isModal) return undefined;
    const panel = panelRef.current;
    const tabBar = document.querySelector(`[${TAB_BAR_ATTRIBUTE}]`);
    const marked = Array.from(new Set([...siblingsAlong(panel, panel), ...siblingsAlong(tabBar, panel)])).filter(
      (element) => !element.contains(tabBar),
    );
    marked.forEach((element) => {
      element.setAttribute('inert', '');
    });
    document.documentElement.dataset.assistantModal = '';
    return () => {
      marked.forEach((element) => {
        element.removeAttribute('inert');
      });
      delete document.documentElement.dataset.assistantModal;
    };
  }, [isModal, panelRef]);
}

interface PanelWindowProps {
  readonly isOpen: boolean;
  readonly isCompact: boolean;
  readonly onClose: () => void;
  readonly panelRef: RefObject<HTMLElement | null>;
  /** 掛在哪個元素底下（useAssistantHost 的容器）。 */
  readonly host: HTMLElement;
  readonly children: ReactNode;
}

/** 面板裡的連結（開成整頁、到「我的 AI 助理」、變更集）換了頁：全螢幕的面板會蓋住新的頁面，要收起來；浮動的留著。 */
function closeOnLink(event: MouseEvent<HTMLElement>, isCompact: boolean, onClose: () => void): void {
  if (!isCompact) return;
  const target = event.target instanceof Element ? event.target.closest('a[href]') : null;
  if (target !== null) onClose();
}

/** 全螢幕（modal）時 Tab 在面板裡繞圈，和 ModalFrame 一樣：焦點不跑到瀏覽器本身。 */
function keepFocusInside(event: KeyboardEvent<HTMLElement>): void {
  const stops = trapStopsIn(event.currentTarget);
  const first = stops[0];
  const last = stops.at(-1);
  if (first === undefined || last === undefined) return;
  const active = document.activeElement;
  if (event.shiftKey && (active === first || active === event.currentTarget)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && active === last) {
    event.preventDefault();
    first.focus();
  }
}

function escapeHandler(onClose: () => void, isModal: boolean) {
  return (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Tab' && isModal) keepFocusInside(event);
    // 選單、下拉自己先處理 Esc（只關掉它們）；輸入法選字中的 Esc 是取消選字。
    if (event.key !== 'Escape' || event.defaultPrevented || isComposingKey(event)) return;
    // 不讓同一個 Esc 再關掉旁邊的項目面板（lib/shortcuts.ts 的 closePanel 聽在 document）。
    event.stopPropagation();
    event.preventDefault();
    onClose();
  };
}

/**
 * 面板的外框：寬螢幕是非 modal 的 dialog（aria-modal="false"，背後的頁面照樣能操作），窄螢幕全螢幕時是 modal；
 * 三個調大小的把手（左緣、上緣、左上角）、Esc 關閉。掛在 body 底下（窄螢幕才能把其他內容 inert）。
 * 關上時不卸下也不用 display:none（瀏覽器會丟掉捲動的位置）：用 visibility 藏起來並 inert，
 * 輸入框的草稿、捲動的位置與進行中的回覆都留著。
 */
export function PanelWindow({ isOpen, isCompact, onClose, panelRef, host, children }: PanelWindowProps) {
  const resize = usePanelResize(panelRef);
  usePublishCorner(resize.size, isOpen, isCompact);
  useModalBackground(panelRef, isOpen && isCompact);
  return createPortal(
    <section
      ref={panelRef}
      id={PANEL_ID}
      role="dialog"
      aria-modal={isOpen && isCompact}
      aria-labelledby="assistant-panel-title"
      aria-hidden={!isOpen}
      inert={!isOpen}
      data-open={isOpen}
      className={styles.panel}
      onKeyDown={escapeHandler(onClose, isOpen && isCompact)}
      onClick={(event) => {
        closeOnLink(event, isCompact, onClose);
      }}
    >
      <PanelResizers resize={resize} />
      {children}
    </section>,
    host,
  );
}
