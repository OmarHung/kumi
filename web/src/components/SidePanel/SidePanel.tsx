import { use, useCallback, useEffect, useId, useRef, type ReactNode, type Ref } from 'react';
import { SHORTCUTS, useShortcut } from '../../lib/shortcuts';
import { IconButton } from '../IconButton/IconButton';
import styles from './SidePanel.module.css';
import { SidePanelResizer } from './SidePanelResizer';
import { SidePanelWidthContext } from './useSidePanelWidth';

export interface SidePanelProps {
  /** complementary 區域（aside）的名稱，例如「項目 AGY-3」。 */
  readonly label: string;
  /** 標題列的文字（h2），例如「AGY-3」；可以是元素（類型加等寬的編號）。 */
  readonly title: ReactNode;
  /** 標題列右邊、關閉按鈕前面的動作：上一個、下一個、展開成整頁。 */
  readonly headerActions?: ReactNode;
  /** 按了關閉、或按 Esc 時呼叫。焦點回到清單上那一筆由呼叫端做（上一個、下一個會換掉「那一筆」）。 */
  readonly onClose: () => void;
  /** 關閉按鈕的名稱，預設「關閉」。 */
  readonly closeLabel?: string;
  /** 要再把焦點放回標題時用（標題是 tabIndex={-1}）。 */
  readonly headingRef?: Ref<HTMLHeadingElement>;
  readonly children: ReactNode;
}

function assignRef<T>(ref: Ref<T> | undefined, node: T | null): void {
  if (typeof ref === 'function') {
    ref(node);
  } else if (ref !== null && ref !== undefined) {
    ref.current = node;
  }
}

/** 掛上時焦點到標題；呼叫端也拿得到標題。FloatingPanel 共用。 */
export function useHeadingFocus(headingRef: Ref<HTMLHeadingElement> | undefined) {
  const ownRef = useRef<HTMLHeadingElement | null>(null);
  const setHeading = useCallback(
    (node: HTMLHeadingElement | null) => {
      ownRef.current = node;
      assignRef(headingRef, node);
    },
    [headingRef],
  );
  // 只在掛上時：換內容（上一個、下一個）時焦點留在使用者按的地方。
  useEffect(() => {
    ownRef.current?.focus({ preventScroll: true });
  }, []);
  return setHeading;
}

export interface PanelHeaderProps {
  readonly title: ReactNode;
  readonly headerActions?: ReactNode;
  readonly onClose: () => void;
  readonly closeLabel: string;
  readonly setHeading: (node: HTMLHeadingElement | null) => void;
  /** 標題的 id：浮動視窗拿它當對話框的名稱。 */
  readonly headingId?: string;
}

/** 標題列：標題（h2）、呼叫端的動作與關閉。側邊面板與浮動視窗長得一樣，樣式共用 SidePanel.module.css。 */
export function PanelHeader({ title, headerActions, onClose, closeLabel, setHeading, headingId }: PanelHeaderProps) {
  return (
    <div className={styles.header}>
      <h2 id={headingId} ref={setHeading} tabIndex={-1} className={styles.title}>
        {title}
      </h2>
      <div className={styles.actions}>
        {headerActions}
        <IconButton icon="close" label={closeLabel} onClick={onClose} />
      </div>
    </div>
  );
}

/**
 * 清單旁邊的面板（docs/DESIGN.md 第 7 節、8.3「項目面板」）。不是對話框：不鎖焦點、背景不變暗，清單照常可以操作。
 * 掛上就是打開、卸下就是關上，放在 SidePanelLayout 的 panel。換內容時不要用 key 重新掛上（焦點會被拉回標題）。
 * Esc 登記在 lib/shortcuts.ts：編輯中的格子、開著的選單、輸入框先處理，這裡只收沒人處理的。
 * 放在 SidePanelLayout 裡時左緣是調整寬度的把手（SidePanelResizer）。把手在 aside 外面、和它並排：aside 本身是捲動容器
 * （焦點在標題上時 PageDown 捲得動面板，換項目時捲回最上面也是捲它），把手放在裡面會跟著內容捲走、一半露在外面也會被裁掉。
 */
export function SidePanel({ label, title, headerActions, onClose, closeLabel = '關閉', headingRef, children }: SidePanelProps) {
  const setHeading = useHeadingFocus(headingRef);
  const width = use(SidePanelWidthContext);
  const regionId = useId();
  useShortcut(SHORTCUTS.closePanel, onClose);
  return (
    <div className={styles.panel}>
      {width !== null && <SidePanelResizer api={width} controls={regionId} />}
      <aside id={regionId} aria-label={label} className={styles.region}>
        <PanelHeader title={title} headerActions={headerActions} onClose={onClose} closeLabel={closeLabel} setHeading={setHeading} />
        <div className={styles.body}>{children}</div>
      </aside>
    </div>
  );
}
