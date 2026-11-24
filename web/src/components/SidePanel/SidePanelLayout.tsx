import { useRef, type CSSProperties, type FocusEvent, type ReactNode, type RefObject } from 'react';
import { useMediaQuery } from '../../lib/useMediaQuery';
import styles from './SidePanel.module.css';
import { SidePanelWidthContext, useSidePanelWidth } from './useSidePanelWidth';

export type SidePanelLayoutKind = 'fill' | 'page';

/**
 * 面板排成清單右邊一欄的寬度：和 SidePanel.module.css 的 @media (min-width: 80rem) 同一個值（docs/DESIGN.md 6.3）。
 * 以下面板蓋在清單上面。
 */
export const SIDE_PANEL_BESIDE_QUERY = '(min-width: 80rem)';

export interface SidePanelLayoutProps {
  /** 開著的 SidePanel；沒開時是 null、undefined 或 false。 */
  readonly panel?: ReactNode;
  /**
   * fill（預設）：工作區（表格、看板），撐滿剩下的高度，清單自己捲；面板是右邊一欄、自己直向捲。
   * page：清單頁與閱讀頁（我的工作、項目頁的下層項目），跟著頁面捲；面板黏在頁列下面。和 TableScroll、List 的 layout 同義。
   */
  readonly layout?: SidePanelLayoutKind;
  /**
   * 面板蓋在清單上面（80rem 以下）時，焦點走到清單裡被面板蓋住的地方（用 Tab 走到右邊的格子、卡片）：
   * 呼叫端收起面板，使用者才看得到焦點在哪裡（WCAG 2.4.11）。
   */
  readonly onCoveredFocus?: () => void;
  /**
   * true：開著的面板是浮動視窗（FloatingPanel，放在 body 的 modal），不排在清單旁邊：版面照沒有面板的樣子（清單用滿、
   * 沒有寬度與蓋住焦點的處理），面板仍然掛在這裡（切換樣子時內容的元件不重新掛上）。
   */
  readonly isPanelFloating?: boolean;
  /** 清單。開關面板時這一塊不會重新掛載（打的字、捲動位置、焦點都留著）。 */
  readonly children: ReactNode;
}

function overlaps(a: DOMRect, b: DOMRect): boolean {
  return a.right > b.left && a.left < b.right && a.bottom > b.top && a.top < b.bottom;
}

/** 蓋在清單上的面板：焦點到了它底下就通知呼叫端。面板排成右欄時不會蓋住，不必判斷。 */
function useCoveredFocus(
  layoutRef: RefObject<HTMLDivElement | null>,
  onCoveredFocus: (() => void) | undefined,
  isBeside: boolean,
) {
  return (event: FocusEvent<HTMLDivElement>) => {
    const panel = layoutRef.current?.querySelector(`:scope > .${styles.panel} > .${styles.region}`);
    if (onCoveredFocus === undefined || isBeside || panel === null || panel === undefined) return;
    if (overlaps(event.target.getBoundingClientRect(), panel.getBoundingClientRect())) onCoveredFocus();
  };
}

/**
 * 面板的寬度與最高要扣掉的高度要執行時才算得出來（使用者拉的、夾在清單那一塊的範圍裡；頁面捲到哪裡），
 * 只能用 style 設 CSS 變數（docs/DESIGN.md 第 13 節；scripts/check-styles.mjs 的允許清單有這個檔案）。
 * CSS 照它們排面板，沒開時不設。
 */
function panelStyle(hasPanel: boolean, width: number, blockOffset: number): CSSProperties | undefined {
  if (!hasPanel) return undefined;
  return { '--side-panel-current-width': `${width}px`, '--side-panel-block-offset': `${blockOffset}px` } as CSSProperties;
}

/**
 * 清單加旁邊的面板。80rem 以上面板是右邊一欄、清單跟著縮窄；以下面板蓋在清單上面（靠右）。
 * 放在 PageLayout 的內容區裡：面板抵掉右邊的 --page-gutter，貼著內容區的右緣。
 * 面板的寬度可以拖拉調整（useSidePanelWidth），範圍看這一塊有多寬，所以寬度的狀態放在這裡、經 Context 交給面板的把手。
 */
export function SidePanelLayout({ panel, layout = 'fill', onCoveredFocus, isPanelFloating = false, children }: SidePanelLayoutProps) {
  const isOpen = panel !== null && panel !== undefined && panel !== false;
  const hasPanel = isOpen && !isPanelFloating;
  const isBeside = useMediaQuery(SIDE_PANEL_BESIDE_QUERY);
  const layoutRef = useRef<HTMLDivElement>(null);
  const handleFocus = useCoveredFocus(layoutRef, onCoveredFocus, isBeside);
  const width = useSidePanelWidth(layoutRef, hasPanel, isBeside);
  return (
    <div
      ref={layoutRef}
      className={styles.layout}
      data-layout={layout}
      data-has-panel={hasPanel}
      style={panelStyle(hasPanel, width.width, width.blockOffset)}
    >
      <div className={styles.main} onFocus={hasPanel ? handleFocus : undefined}>
        {children}
      </div>
      <SidePanelWidthContext value={width}>{isOpen && panel}</SidePanelWidthContext>
    </div>
  );
}
