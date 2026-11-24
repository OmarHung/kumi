import { useEffect, useRef, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { useItemPanelMode } from '../../lib/itemPanelMode';
import { SidePanelLayout, type SidePanelLayoutKind } from '../../components/SidePanel/SidePanelLayout';
import { OpenItemProvider } from '../item-table/OpenItemContext';
import { itemPagePath, pathWithoutPanelItem, projectKeyOfItemKey, type ItemPageEntry } from '../work-items/itemLinks';
import { ItemPanel } from './ItemPanel';
import { ItemPanelContext } from './ItemPanelContext';
import { useRegisterItemPanel } from './ItemPanelRegistry';
import { isPanelEntry, useItemPanelController } from './useItemPanelController';

interface ItemPanelHostProps {
  /** fill：工作區（表格、看板）；page：清單頁與閱讀頁（我的工作、項目頁的下層項目）。 */
  readonly layout: SidePanelLayoutKind;
  /** 清單所在的專案：面板先用它讀專案內容。沒有（我的工作跨專案）時從編號猜，讀到項目之後以項目的專案為準。 */
  readonly projectKey?: string;
  /** 清單：開關面板時不重新掛載（SidePanelLayout）。 */
  readonly children: ReactNode;
}

/**
 * 窄螢幕（48rem 以下）不用面板：網址帶著 item 時（分享來的、開著面板時視窗變窄）換到整頁的項目頁，用 push（上一頁回到清單）。
 * 先讓清單那一筆不帶 item，再新開一筆整頁：上一頁回到清單，而不是又被換回整頁。這一筆是從清單打開面板時新開的，
 * 就用上一頁退回打開之前的清單（不帶 item 的那一筆本來就在），瀏覽紀錄裡才不會多一筆一樣的清單；不是的才換掉。
 */
function useCompactRedirect(compactKey: string | null, projectKey: string | undefined): void {
  const location = useLocation();
  const navigate = useNavigate();
  const handled = useRef<string | null>(null);
  const isOwnEntry = isPanelEntry(location.state);
  useEffect(() => {
    if (compactKey === null) {
      handled.current = null;
      return;
    }
    // 開發模式的 effect 會跑兩次：同一個項目只換一次頁。
    if (handled.current === compactKey) return;
    handled.current = compactKey;
    const list = pathWithoutPanelItem(location.pathname, location.search);
    const entry: ItemPageEntry = { returnTo: list };
    const page = itemPagePath(projectKey ?? projectKeyOfItemKey(compactKey), compactKey);
    void (async () => {
      if (isOwnEntry) await navigate(-1);
      else await navigate(list, { replace: true });
      // hash 一起帶：由留言產生的通知指著那一則留言（#comment-…），整頁的時間軸要讀得到。
      await navigate({ pathname: page, hash: location.hash }, { state: entry });
    })();
  }, [compactKey, projectKey, isOwnEntry, location.pathname, location.search, location.hash, navigate]);
}

/**
 * 放項目面板的那一頁（docs/DESIGN.md 8.3「項目面板」）：清單加旁邊的面板，面板開著哪一個記在網址的 item 參數。
 * 清單上的連結（useItemPanelLink）、新增之後的「開啟」（OpenItemProvider）在寬螢幕都開面板。
 * 窄螢幕（48rem 以下）不用面板（useCompactRedirect）。面板有兩種樣子（側邊面板、浮動視窗，lib/itemPanelMode.ts），
 * 兩種共用這個宿主、網址與瀏覽紀錄的規則。
 */
export function ItemPanelHost({ layout, projectKey, children }: ItemPanelHostProps) {
  const { value, compactKey } = useItemPanelController();
  useCompactRedirect(compactKey, projectKey);
  useRegisterItemPanel(value);
  const mode = useItemPanelMode();
  const { openKey } = value;
  const panel = openKey !== null && (
    <ItemPanel panel={value} itemKey={openKey} projectKey={projectKey ?? projectKeyOfItemKey(openKey)} />
  );
  return (
    <ItemPanelContext value={value}>
      <OpenItemProvider value={value.isEnabled ? value.open : null}>
        {/* 面板蓋在清單上（80rem 以下）時，用 Tab 走到面板底下的地方就收起面板，焦點才看得到。 */}
        <SidePanelLayout layout={layout} panel={panel} isPanelFloating={mode === 'modal'} onCoveredFocus={value.close}>
          {children}
        </SidePanelLayout>
      </OpenItemProvider>
    </ItemPanelContext>
  );
}
