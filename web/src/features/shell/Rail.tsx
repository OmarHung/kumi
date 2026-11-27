import { useId, type ReactNode, type RefObject } from 'react';
import { Link, useLocation } from 'react-router';
import { useUnreadCount } from '../../api/notificationQueries';
import { CountDescription } from '../../components/CountBadge/CountBadge';
import { Icon, type IconName } from '../../components/Icon/Icon';
import { PRODUCT_NAME } from '../../config/product';
import { ariaKeyShortcuts, SHORTCUTS } from '../../lib/shortcuts';
import { ASSISTANT_TAB_ID, closeAssistantPanel, toggleAssistantPanel } from '../assistant-panel/panelControl';
import { useJump } from '../jump/JumpProvider';
import { isSettingsHubPath } from '../settings-hub/settingsHubPages';
import { RailAccountMenu } from './AccountMenu';
import { badgeText } from './unreadBadge';
import { useNavState } from './useNavState';
import styles from './Rail.module.css';

/** 產品標誌和側欄、瀏覽器分頁的圖示是同一個檔案。 */
const BRAND_MARK_SRC = '/favicon.svg';

interface CellContentProps {
  readonly icon: IconName;
  readonly label: string;
  readonly children?: ReactNode;
}

/** 一格的內容：圖示是裝飾，名稱就是下面那行字；角標由呼叫的人放進 children。 */
function CellContent({ icon, label, children }: CellContentProps) {
  return (
    <>
      <span className={styles.iconSlot}>
        <Icon name={icon} className={styles.icon} />
        {children}
      </span>
      <span className={styles.label}>{label}</span>
    </>
  );
}

interface LinkCellProps extends CellContentProps {
  readonly to: string;
  readonly isCurrent: boolean;
  /** 畫面上的字比較短時，完整的名稱（名稱要包含看得到的字）。 */
  readonly name?: string;
  readonly describedBy?: string;
}

/** 連結的一格：換頁之前先請浮動的 AI 面板收起來，焦點跟著被點的那一格。 */
function LinkCell({ to, isCurrent, name, describedBy, icon, label, children }: LinkCellProps) {
  return (
    <Link
      to={to}
      className={styles.cell}
      aria-label={name}
      aria-current={isCurrent ? 'page' : undefined}
      aria-describedby={describedBy}
      onClick={() => {
        closeAssistantPanel({ restoreFocus: false });
      }}
    >
      <CellContent icon={icon} label={label}>
        {children}
      </CellContent>
    </Link>
  );
}

function InboxCell({ isCurrent }: { readonly isCurrent: boolean }) {
  const { data: count } = useUnreadCount();
  const descriptionId = useId();
  const hasUnread = count !== undefined && count > 0;
  return (
    <LinkCell to="/inbox" icon="notifications" label="收件匣" isCurrent={isCurrent} describedBy={descriptionId}>
      {hasUnread && (
        <span className={styles.badge} aria-hidden="true">
          {badgeText(count)}
        </span>
      )}
      <CountDescription id={descriptionId} labels={hasUnread ? [`${String(count)} 則未讀`] : []} />
    </LinkCell>
  );
}

function SearchCell() {
  const jump = useJump();
  return (
    <button
      type="button"
      className={styles.cell}
      aria-keyshortcuts={ariaKeyShortcuts(SHORTCUTS.openJump)}
      onClick={(event) => {
        // Safari 點了按鈕不會把焦點給它：先給，對話框關上時焦點才回得來。
        event.currentTarget.focus();
        jump.open();
      }}
    >
      <CellContent icon="search" label="搜尋" />
    </button>
  );
}

function SettingsCell({ isCurrent }: { readonly isCurrent: boolean }) {
  return <LinkCell to="/account" icon="settings" label="設定" isCurrent={isCurrent} />;
}

interface AssistantCellProps {
  readonly isFullPage: boolean;
  readonly isShown: boolean;
  readonly onOpen: () => void;
}

/** 「AI 助理」：整頁版裡是目前所在的連結；其他頁是打開浮動面板的按鈕（面板開著時是目前所在）。 */
function AssistantCell({ isFullPage, isShown, onOpen }: AssistantCellProps) {
  const { pathname, search } = useLocation();
  if (isFullPage) {
    return <LinkCell to={`${pathname}${search}`} icon="sparkles" label="AI 助理" isCurrent />;
  }
  return (
    <button
      type="button"
      id={ASSISTANT_TAB_ID}
      className={styles.cell}
      aria-haspopup="dialog"
      aria-expanded={isShown}
      onClick={() => {
        onOpen();
        toggleAssistantPanel();
      }}
    >
      <CellContent icon="sparkles" label="AI 助理" />
    </button>
  );
}

interface RailProps {
  readonly isDrawerOpen: boolean;
  readonly onToggleDrawer: () => void;
  /** 收起抽屜、不搬焦點（按「AI 助理」時：面板蓋上來，抽屜不能留在底下開著）。 */
  readonly onCloseDrawer: () => void;
  /** 「專案」那一格：抽屜關上時焦點回到這裡。 */
  readonly projectsButtonRef: RefObject<HTMLButtonElement | null>;
  /** 「專案」打開的側欄（抽屜）的 id。 */
  readonly sidebarId: string;
}

/**
 * 48～64rem（平板直向）的圖示列（docs/DESIGN.md 6.1）：側欄換成一條窄的直欄，每一格圖示加一行字。
 * 「專案」打開完整側欄的抽屜（專案清單、人員、使用說明都在裡面）；AI 助理的入口在這裡，不再浮一顆按鈕。
 * 不渲染的時機由外框決定（只在 rail 模式掛上）。
 */
export function Rail({ isDrawerOpen, onToggleDrawer, onCloseDrawer, projectsButtonRef, sidebarId }: RailProps) {
  const nav = useNavState();
  const { pathname, noneActive } = nav;
  return (
    <nav className={styles.rail} aria-label="主要導覽">
      <Link to="/" className={styles.brand} aria-label={PRODUCT_NAME}>
        <img className={styles.brandMark} src={BRAND_MARK_SRC} alt="" aria-hidden="true" />
      </Link>
      <SearchCell />
      <LinkCell to="/my-work" icon="myWork" label="我的工作" isCurrent={!noneActive && nav.isUnder('/my-work')} />
      <LinkCell to="/overview" icon="overview" label="總覽" name="專案總覽" isCurrent={!noneActive && nav.isUnder('/overview')} />
      <InboxCell isCurrent={!noneActive && nav.isUnder('/inbox')} />
      <LinkCell to="/knowledge" icon="knowledge" label="知識庫" isCurrent={!noneActive && nav.isUnder('/knowledge')} />
      <button
        ref={projectsButtonRef}
        type="button"
        className={styles.cell}
        aria-current={!noneActive && nav.isProjects ? 'page' : undefined}
        aria-expanded={isDrawerOpen}
        aria-controls={sidebarId}
        onClick={() => {
          closeAssistantPanel({ restoreFocus: false });
          onToggleDrawer();
        }}
      >
        <CellContent icon="projects" label="專案" />
      </button>
      {nav.hasAssistant && <AssistantCell isFullPage={nav.isFullPageAssistant} isShown={nav.isPanelShown} onOpen={onCloseDrawer} />}
      <span className={styles.spacer} />
      <SettingsCell isCurrent={!noneActive && isSettingsHubPath(pathname)} />
      <RailAccountMenu buttonClassName={styles.cell} />
    </nav>
  );
}
