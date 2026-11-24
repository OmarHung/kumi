import { useLayoutEffect, useRef, useSyncExternalStore, type RefObject } from 'react';
import { Navigate, useLocation } from 'react-router';
import { isApiError } from '../../api/http';
import { IconButton, IconLink } from '../../components/IconButton/IconButton';
import { FloatingPanel } from '../../components/SidePanel/FloatingPanel';
import { SidePanel } from '../../components/SidePanel/SidePanel';
import { EmptyState, ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { itemTypeName } from '../../domain/blueprint';
import { setItemPanelMode, useItemPanelMode, type ItemPanelMode } from '../../lib/itemPanelMode';
import { ariaKeyShortcuts, SHORTCUTS, useShortcut } from '../../lib/shortcuts';
import { OpenItemProvider } from '../item-table/OpenItemContext';
import { ProjectTableProvider } from '../item-table/ProjectTableContext';
import { itemPagePath, searchWithPanelItem, useItemPageEntry } from '../work-items/itemLinks';
import { FollowButton } from './FollowButton';
import { ShareItemButton } from './ShareItemButton';
import { ItemActions } from './ItemActions';
import {
  HTTP_FORBIDDEN,
  HTTP_NOT_FOUND,
  ITEM_FORBIDDEN,
  ITEM_NOT_FOUND,
  ItemArticle,
  useItemContent,
  useItemScope,
  useRecordRecentItem,
  type ItemContentState,
  type ItemScope,
} from './ItemContent';
import { InItemPanel, type ItemPanelValue } from './ItemPanelContext';
import styles from './ItemPanel.module.css';

interface PanelSteps {
  readonly prev: string | undefined;
  readonly next: string | undefined;
  readonly step: (target: string | undefined) => void;
}

/** 上一個、下一個：照清單現在已經載入的順序（看板是同一欄裡的順序），Alt+↑／↓ 也可以（lib/shortcuts.ts）。 */
function usePanelSteps(panel: ItemPanelValue, itemKey: string): PanelSteps {
  const { registry } = panel;
  const keys = useSyncExternalStore(registry.subscribe, () => registry.sequenceOf(itemKey));
  const index = keys?.indexOf(itemKey) ?? -1;
  const prev = index > 0 ? keys?.[index - 1] : undefined;
  const next = index >= 0 ? keys?.[index + 1] : undefined;
  const step = (target: string | undefined) => {
    if (target !== undefined) panel.show(target);
  };
  // 沒有上一個、下一個時不聽：按鍵交還給瀏覽器（Mac 的 Safari 用 Option+↑／↓ 捲動一頁）。
  useShortcut(SHORTCUTS.itemPrev, () => {
    step(prev);
  }, { isEnabled: prev !== undefined });
  useShortcut(SHORTCUTS.itemNext, () => {
    step(next);
  }, { isEnabled: next !== undefined });
  return { prev, next, step };
}

/**
 * 換了項目：面板捲回最上面。焦點原本在換掉的內容裡（按了面板裡的下層項目、用 Alt+↓ 時焦點在欄位上）就到面板的標題，
 * 不會掉回頁面本體；焦點在標題列的按鈕（上一個、下一個）上就留著。面板本身不重新掛上（docs/DESIGN.md 第 7 節）。
 */
function useKeepFocusInPanel(itemKey: string, headingRef: RefObject<HTMLHeadingElement | null>) {
  const shown = useRef(itemKey);
  useLayoutEffect(() => {
    if (shown.current === itemKey) return;
    shown.current = itemKey;
    const heading = headingRef.current;
    const panel = heading?.closest('aside, [data-panel-scroll]');
    if (panel !== null && panel !== undefined) panel.scrollTop = 0;
    const active = document.activeElement;
    if (active === null || active === document.body) heading?.focus({ preventScroll: true });
  }, [itemKey, headingRef]);
}

function readyItem(content: ItemContentState) {
  return content.status === 'ready' ? content.item : undefined;
}

/**
 * 標題列：類型與等寬的編號（「任務 SMK-1」）。類型要等專案的藍圖讀到才知道。
 * 位子不夠時省略的是類型的名稱，編號不縮（編號才是認得出是哪一個項目的字）；兩段之間的空白是真的文字，名稱才是「任務 SMK-1」。
 */
function PanelTitle({ itemKey, content }: { readonly itemKey: string; readonly content: ItemContentState }) {
  const typeName = content.status === 'ready' ? itemTypeName(content.detail.blueprint, content.item.type) : null;
  return (
    <span className={styles.title}>
      {typeName !== null && (
        <>
          <span className={styles.typeName}>{typeName}</span>{' '}
        </>
      )}
      <span className={styles.key}>{itemKey}</span>
    </span>
  );
}

/**
 * 側邊面板／浮動視窗的切換：名稱固定是「浮動視窗」，開著浮動視窗時是按下的樣子（aria-pressed，和「關注」同一套）。
 * 圖示固定是浮動視窗（這顆按鈕打開的東西），不跟著目前的樣子換：原本在側邊時顯示側邊的圖示，看起來像「按了會變成側邊」
 * （使用者 2026-11-20 問是不是反了）。滑過的說明寫按了會怎樣。
 */
function ModeToggle({ mode }: { readonly mode: ItemPanelMode }) {
  const isFloating = mode === 'modal';
  return (
    <IconButton
      icon="panelFloating"
      label="浮動視窗"
      description={isFloating ? '改回側邊面板' : '改用置中的浮動視窗'}
      aria-pressed={isFloating}
      className={styles.modeToggle}
      onClick={() => {
        setItemPanelMode(isFloating ? 'side' : 'modal');
      }}
    />
  );
}

interface PanelActionsProps {
  readonly mode: ItemPanelMode;
  readonly steps: PanelSteps;
  readonly scope: ItemScope;
  readonly content: ItemContentState;
  readonly itemKey: string;
  readonly projectKey: string;
}

/** 關閉前面的動作：上一個、下一個、關注、分享（複製連結）、更多動作（改類型、刪除）、浮動視窗、展開成整頁（連結：⌘／Ctrl 點開新分頁）。 */
function PanelActions({ mode, steps, scope, content, itemKey, projectKey }: PanelActionsProps) {
  // 整頁的麵包屑與刪除之後回到這份清單（檢視與條件照舊，不帶面板）。
  const entry = useItemPageEntry();
  const item = readyItem(content);
  return (
    <>
      <IconButton
        icon="arrowUp"
        label="上一個項目"
        aria-keyshortcuts={ariaKeyShortcuts(SHORTCUTS.itemPrev)}
        aria-disabled={steps.prev === undefined || undefined}
        onClick={() => {
          steps.step(steps.prev);
        }}
      />
      <IconButton
        icon="arrowDown"
        label="下一個項目"
        aria-keyshortcuts={ariaKeyShortcuts(SHORTCUTS.itemNext)}
        aria-disabled={steps.next === undefined || undefined}
        onClick={() => {
          steps.step(steps.next);
        }}
      />
      {/*
        以編號為 key：面板換了項目，關注的狀態、送出中的請求與分享的提示跟著原本的項目一起收掉。
        兩顆按鈕是兄弟，key 要加前綴分開：同一個 key 時 React 換項目會留下舊的按鈕（2026-11-21 鈴鐺越換越多）。
      */}
      {item !== undefined && <FollowButton key={`follow:${item.key}`} itemKey={item.key} isCompact />}
      {item !== undefined && <ShareItemButton key={`share:${item.key}`} item={item} isCompact />}
      {item !== undefined && scope.value !== null && (
        <ProjectTableProvider value={scope.value}>
          {/* 以編號為 key：面板換了項目（上一頁、別的分頁），開著的改類型、刪除對話框跟著原本的項目一起關掉。 */}
          <ItemActions key={item.key} item={item} />
        </ProjectTableProvider>
      )}
      <ModeToggle mode={mode} />
      <IconLink to={itemPagePath(projectKey, itemKey)} state={entry} icon="expand" label="展開成整頁" />
    </>
  );
}

function PanelLoadError({ itemKey, error, onRetry }: { readonly itemKey: string; readonly error: unknown; readonly onRetry: () => void }) {
  const status = isApiError(error) ? error.status : 0;
  if (status === HTTP_NOT_FOUND) {
    return <EmptyState title={ITEM_NOT_FOUND.title}>{ITEM_NOT_FOUND.message(itemKey)}</EmptyState>;
  }
  if (status === HTTP_FORBIDDEN) {
    return <EmptyState title={ITEM_FORBIDDEN.title}>{ITEM_FORBIDDEN.message}</EmptyState>;
  }
  return <ErrorState error={error} title="項目載入失敗" onRetry={onRetry} />;
}

function PanelBody({ itemKey, content, scope }: { readonly itemKey: string; readonly content: ItemContentState; readonly scope: ItemScope }) {
  const location = useLocation();
  const entryState: unknown = location.state;
  switch (content.status) {
    case 'loading':
      return <LoadingState label="正在載入項目…" />;
    case 'itemError':
      return <PanelLoadError itemKey={itemKey} error={content.error} onRetry={content.retry} />;
    case 'projectError':
      return <ErrorState error={content.error} title="專案載入失敗" onRetry={content.retry} />;
    case 'misplaced': {
      // 編號的寫法不是正規的（smk-7）：換成正規的，清單上那一筆才標得到、修改寫進的快取才是同一份。
      const search = searchWithPanelItem(location.search, content.item.key);
      return <Navigate to={{ pathname: location.pathname, search }} replace state={entryState} />;
    }
    case 'ready':
      return (
        scope.value !== null && (
          <ProjectTableProvider value={scope.value}>
            {content.notices}
            <ItemArticle item={content.item} headingLevel={2} />
          </ProjectTableProvider>
        )
      );
  }
}

interface ItemPanelProps {
  readonly panel: ItemPanelValue;
  readonly itemKey: string;
  /** 先用來讀專案內容的代號（清單所在的專案，或從編號猜的）；讀到項目之後以項目自己的專案為準。 */
  readonly projectKey: string;
}

/**
 * 項目面板（docs/DESIGN.md 8.3）：清單旁邊的 SidePanel，內容就是項目頁的內容（ItemContent.tsx）。
 * 換項目時面板不重新掛上（不用 key），只有裡面的內容以編號為 key 換掉：編輯中的格子、留言草稿都重新開始。
 * 面板裡的連結（上層、下層、新增之後的「開啟」）換掉面板的內容（InItemPanel、OpenItemProvider）。
 * 外殼有兩種（標題列的「浮動視窗」切換，lib/itemPanelMode.ts）：側邊面板，或置中的浮動視窗（FloatingPanel）；
 * 切換時資料不重讀（這個元件與讀進來的內容還在），但兩種外殼是不同的元件，畫面上的內容會重新掛上（捲動位置、開著的選單不保留）；
 * 焦點到新外殼的標題。
 */
export function ItemPanel({ panel, itemKey, projectKey }: ItemPanelProps) {
  const content = useItemContent(itemKey, projectKey, true);
  const scope = useItemScope(content.status === 'ready' ? content.detail : undefined);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const mode = useItemPanelMode();
  const steps = usePanelSteps(panel, itemKey);
  const item = readyItem(content);
  useRecordRecentItem(item);
  useKeepFocusInPanel(itemKey, headingRef);
  const Shell = mode === 'modal' ? FloatingPanel : SidePanel;
  const actions = (
    <PanelActions mode={mode} steps={steps} scope={scope} content={content} itemKey={itemKey} projectKey={item?.projectKey ?? projectKey} />
  );
  return (
    <InItemPanel>
      <OpenItemProvider value={panel.show}>
        <Shell
          label={`項目 ${itemKey}`}
          title={<PanelTitle itemKey={itemKey} content={content} />}
          headerActions={actions}
          onClose={panel.close}
          headingRef={headingRef}
        >
          {/* data-item-panel：清單上的連結在整頁找時跳過面板裡的（itemSequences.ts 的 PANEL_CONTENT_ATTRIBUTE）。 */}
          <div key={itemKey} data-item-panel="">
            <PanelBody itemKey={itemKey} content={content} scope={scope} />
          </div>
        </Shell>
        {scope.membersDialog}
      </OpenItemProvider>
    </InItemPanel>
  );
}
