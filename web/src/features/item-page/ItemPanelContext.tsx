import { createContext, use, useEffect, useId, type MouseEvent, type ReactNode, type RefObject } from 'react';
import { isPlainLeftClick } from '../../components/SidePanel/plainClick';
import type { ItemSequenceRegistry } from './itemSequences';

/**
 * 項目面板（docs/DESIGN.md 8.3）給清單用的部分：開著哪一個、怎麼開與關、清單的順序。
 * 由放面板的那一頁（ItemPanelHost：專案頁、我的工作、項目頁）提供；沒有提供的地方，清單上的連結照常換頁。
 */
export interface ItemPanelValue {
  /** 開著面板的項目；沒開、或是窄螢幕（不用面板）時是 null。 */
  readonly openKey: string | null;
  /** 寬螢幕才用面板：窄螢幕（48rem 以下）點清單直接進整頁。 */
  readonly isEnabled: boolean;
  /**
   * 從清單打開（新的一筆瀏覽紀錄：上一頁會關上面板）；面板已經開著時是換成這一個。
   * hash 是連結帶的（例如由留言產生的通知指到那一則留言）：放進網址讓面板裡的時間軸找得到；
   * 面板已經開著同一個項目時，有 hash 照樣導航一次（再點一次同一則要再閃一次），沒有就不動。
   */
  readonly open: (itemKey: string, hash?: string) => void;
  /** 面板換成另一個項目（上一個、下一個、面板裡的上層與下層）：換掉目前的瀏覽紀錄，不多一筆。 */
  readonly show: (itemKey: string) => void;
  /** 關閉按鈕與 Esc。焦點回到清單上那一筆。 */
  readonly close: () => void;
  /**
   * 關上面板、帶著 state 回到清單（在面板裡刪除之後的「已刪除 …」）：打開面板的那一筆紀錄先用上一頁退掉，
   * 再換掉清單那一筆加上提示，瀏覽紀錄裡不會多一筆一樣的清單。
   */
  readonly closeWith: (state: unknown) => Promise<void>;
  readonly registry: ItemSequenceRegistry;
}

export const ItemPanelContext = createContext<ItemPanelValue | null>(null);

/** 在面板裡面：連結換掉面板的內容，清單不登記順序（上一個、下一個走的是旁邊那份清單）。 */
const InPanelContext = createContext(false);

export function InItemPanel({ children }: { readonly children: ReactNode }) {
  return <InPanelContext value>{children}</InPanelContext>;
}

export function useItemPanel(): ItemPanelValue | null {
  return use(ItemPanelContext);
}

export function useIsInItemPanel(): boolean {
  return use(InPanelContext);
}

/**
 * 項目內容裡各區塊的標題（欄位、說明、下層項目、活動與留言）：整頁是 h2（h1 是項目的標題）；
 * 面板裡是 h3（面板的標題列與項目的標題都是 h2），一頁的標題層級照順序往下。
 */
export function SectionHeading({ id, className, children }: { readonly id: string; readonly className?: string; readonly children: ReactNode }) {
  const Heading = useIsInItemPanel() ? 'h3' : 'h2';
  return (
    <Heading id={id} className={className}>
      {children}
    </Heading>
  );
}

export interface ItemPanelLinkProps {
  readonly onClick?: (event: MouseEvent<HTMLElement>) => void;
  readonly 'aria-current'?: 'true';
}

/**
 * 攔下一般的左鍵點擊開面板：連結的 href 仍然是整頁的項目頁（報讀軟體、複製連結、⌘／Ctrl 點開新分頁都靠它），
 * 按著修飾鍵、中鍵、窄螢幕都照瀏覽器原本的行為走。回傳有沒有攔下來。
 */
function openFromClick(
  panel: ItemPanelValue | null,
  isInPanel: boolean,
  event: MouseEvent<HTMLElement>,
  itemKey: string,
): boolean {
  if (panel === null || !panel.isEnabled || !isPlainLeftClick(event)) return false;
  event.preventDefault();
  if (isInPanel) {
    panel.show(itemKey);
  } else {
    const link = event.currentTarget instanceof HTMLAnchorElement ? event.currentTarget : null;
    if (link !== null) panel.registry.rememberOpened(link);
    panel.open(itemKey, link?.hash);
  }
  return true;
}

/**
 * 清單上連到項目頁的連結用：一般的點擊開面板，開著的那一筆標 aria-current（樣式照它選）。
 * onlyInPanel：只有在面板裡才攔（例如欄位面板的「開啟上層項目」：整頁上照樣換到上層的項目頁）。
 */
export function useItemPanelLink(itemKey: string, onlyInPanel = false): ItemPanelLinkProps {
  const panel = useItemPanel();
  const isInPanel = useIsInItemPanel();
  if (panel === null || !panel.isEnabled || (onlyInPanel && !isInPanel)) return {};
  return {
    onClick: (event) => {
      openFromClick(panel, isInPanel, event, itemKey);
    },
    ...(!isInPanel && panel.openKey === itemKey && { 'aria-current': 'true' as const }),
  };
}

/**
 * 清單登記目前看得到的順序（照畫面的順序、已經載入的）。面板裡的清單不登記。
 * container：清單的容器，關上面板時在這裡面找那一筆的連結放焦點；拿不到時在整頁找（跳過面板）。
 */
export function useItemSequence(keys: readonly string[], container?: RefObject<HTMLElement | null>): void {
  const panel = useItemPanel();
  const isInPanel = useIsInItemPanel();
  const id = useId();
  const registry = panel?.registry;
  // 用字串比對內容：清單每次渲染給的都是新的陣列，內容沒變就不必重新登記。
  const signature = keys.join('\n');
  useEffect(() => {
    if (registry === undefined || isInPanel) return undefined;
    registry.set(id, { keys: signature === '' ? [] : signature.split('\n'), container: container?.current ?? null });
    return () => {
      registry.remove(id);
    };
  }, [registry, isInPanel, id, signature, container]);
}
