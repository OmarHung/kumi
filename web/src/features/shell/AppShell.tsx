import { useRef, useState, type RefObject } from 'react';
import { Outlet } from 'react-router';
import { useUnreadCount } from '../../api/notificationQueries';
import { DocumentTitleCountContext } from '../../components/DocumentTitle';
import { IconButton } from '../../components/IconButton/IconButton';
import { useFaviconBadge } from '../inbox/useFaviconBadge';
import { createItemPanelRegistry, ItemPanelRegistryContext } from '../item-page/ItemPanelRegistry';
import { useLiveUpdates } from '../live/LiveUpdates';
import { NotificationToasts } from '../notification-toasts/NotificationToasts';
import { useNotificationToasts } from '../notification-toasts/useNotificationToasts';
import { AssistantPanel } from '../assistant-panel/AssistantPanel';
import { JumpProvider } from '../jump/JumpProvider';
import { Rail } from './Rail';
import { Sidebar } from './Sidebar';
import { TabBar } from './TabBar';
import { useSidebarCollapsed } from './sidebarPreference';
import { useDrawer } from './useDrawer';
import { useLayoutTier } from '../../lib/useLayoutTier';
import styles from './AppShell.module.css';

const SIDEBAR_ID = 'app-sidebar';

interface BarStartProps {
  readonly drawer: ReturnType<typeof useDrawer>;
  readonly menuButtonRef: RefObject<HTMLButtonElement | null>;
  readonly isCollapsed: boolean;
  readonly onToggleCollapsed: () => void;
}

/**
 * 疊在頁列左上角的按鈕（docs/DESIGN.md 6.1）：寬螢幕是收起／展開側欄的「側欄」，窄螢幕與圖示列不顯示（改由底部分頁列的「更多」或圖示列的「專案」打開抽屜，TabBar.tsx、Rail.tsx），
 * 兩顆都用 aria-expanded 說明開著沒有，名稱固定。
 * 放在外框而不是頁列裡：沒有頁列的頁面（程式出錯的畫面）也按得到。兩顆都在 DOM 裡，CSS 依寬度只顯示一顆。
 */
function BarStart({ drawer, menuButtonRef, isCollapsed, onToggleCollapsed }: BarStartProps) {
  return (
    <div className={styles.barStart}>
      <span className={styles.drawerToggle}>
        <IconButton
          ref={menuButtonRef}
          icon="menu"
          label="選單"
          aria-expanded={drawer.isOpen}
          aria-controls={SIDEBAR_ID}
          onClick={drawer.toggle}
        />
      </span>
      <span className={styles.collapseToggle}>
        <IconButton
          icon="sidebar"
          label="側欄"
          aria-expanded={!isCollapsed}
          aria-controls={SIDEBAR_ID}
          onClick={onToggleCollapsed}
        />
      </span>
    </div>
  );
}

/** 即時更新、新通知的提示與未讀數：有工作階段才在外框裡，登出、工作階段失效時外框被換掉，連線跟著關。 */
function useShellLiveData() {
  const toasts = useNotificationToasts();
  useLiveUpdates(toasts.live);
  // 看別的分頁時側欄看不到、新通知的提示也不跳：未讀數放在分頁的標題與圖示上（docs/design/08-interaction.md 8.3「收件匣」）。
  const { data: unreadCount } = useUnreadCount();
  useFaviconBadge(unreadCount !== undefined && unreadCount > 0);
  return { toasts, unreadCount };
}

/**
 * 登入後的外框：側欄（寬螢幕可以收起來，48～64rem 有圖示列、窄螢幕有分頁列，側欄都是抽屜）、疊在頁列上的按鈕、主要內容。
 * 快速跳轉（⌘K 與側欄最上面的按鈕）掛在這一層：每一頁都打得開，選了之後在主要內容裡找新頁面的標題放焦點。
 */
export function AppShell() {
  const { toasts, unreadCount } = useShellLiveData();
  const [panelRegistry] = useState(createItemPanelRegistry);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const moreButtonRef = useRef<HTMLButtonElement>(null);
  const drawerRef = useRef<HTMLElement>(null);
  const mainRef = useRef<HTMLElement>(null);
  const mode = useLayoutTier();
  // 分頁列的「更多」與圖示列的「專案」不會同時存在，共用一個 ref：抽屜關上時焦點回到打開它的那一格。
  const drawer = useDrawer({ buttonRef: menuButtonRef, compactButtonRef: moreButtonRef, drawerRef, isDrawerMode: mode !== 'wide' });
  const [isCollapsed, toggleCollapsed] = useSidebarCollapsed();
  // 收合只在寬螢幕有意義：窄螢幕與圖示列的側欄一律是抽屜，記著收起來也照樣打得開。
  const isSidebarHidden = isCollapsed && mode === 'wide';

  return (
    // 抽屜裡按「搜尋或跳到…」（或抽屜開著時按 ⌘K）：先收起抽屜、焦點交給「選單」（窄螢幕是分頁列的「更多」），對話框不疊在抽屜上，關上後焦點回到那裡。
    <DocumentTitleCountContext value={unreadCount}>
      <ItemPanelRegistryContext value={panelRegistry}>
        <JumpProvider mainRef={mainRef} onOpen={drawer.isOpen ? drawer.closeAndRefocus : undefined}>
          <div className={styles.shell} data-sidebar={isSidebarHidden ? 'collapsed' : 'expanded'}>
            <a className="skip-link" href="#main-content">
              跳到主要內容
            </a>
            <Sidebar
              ref={drawerRef}
              id={SIDEBAR_ID}
              isOpen={drawer.isOpen}
              isCollapsed={isSidebarHidden}
              onClose={drawer.closeAndRefocus}
              navLabel={mode === 'tablet' ? '更多導覽' : undefined}
            />
            {drawer.isOpen && <div className={styles.backdrop} aria-hidden="true" onClick={drawer.close} />}
            {mode === 'tablet' && (
              <Rail
                isDrawerOpen={drawer.isOpen}
                onToggleDrawer={drawer.toggle}
                onCloseDrawer={drawer.close}
                projectsButtonRef={moreButtonRef}
                sidebarId={SIDEBAR_ID}
              />
            )}
            <BarStart drawer={drawer} menuButtonRef={menuButtonRef} isCollapsed={isCollapsed} onToggleCollapsed={toggleCollapsed} />
            <main ref={mainRef} id="main-content" tabIndex={-1} className={styles.main}>
              <Outlet />
            </main>
            <TabBar isDrawerOpen={drawer.isOpen} onToggleDrawer={drawer.toggle} onCloseDrawer={drawer.close} moreButtonRef={moreButtonRef} sidebarId={SIDEBAR_ID} />
            <NotificationToasts view={toasts.view} />
            <AssistantPanel />
          </div>
        </JumpProvider>
      </ItemPanelRegistryContext>
    </DocumentTitleCountContext>
  );
}
