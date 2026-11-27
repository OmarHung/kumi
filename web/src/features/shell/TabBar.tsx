import { useId, type ReactNode, type RefObject } from 'react';
import { Link, useLocation } from 'react-router';
import { useUnreadCount } from '../../api/notificationQueries';
import { CountDescription } from '../../components/CountBadge/CountBadge';
import { Icon, type IconName } from '../../components/Icon/Icon';
import { useIsCompact } from '../../lib/useMediaQuery';
import { ASSISTANT_TAB_ID, closeAssistantPanel, toggleAssistantPanel } from '../assistant-panel/panelControl';
import { TAB_BAR_ATTRIBUTE } from '../assistant-panel/PanelWindow';
import { badgeText } from './unreadBadge';
import { useNavState } from './useNavState';
import styles from './TabBar.module.css';

interface TabContentProps {
  readonly icon: IconName;
  readonly label: string;
  readonly children?: ReactNode;
}

/** 一格的內容：圖示是裝飾，名稱只有文字；數字之類的角標由呼叫的人放進 children。 */
function TabContent({ icon, label, children }: TabContentProps) {
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

interface LinkTabProps extends TabContentProps {
  readonly to: string;
  readonly isCurrent: boolean;
  readonly describedBy?: string;
}

/** 連結的一格：換頁之前先請浮動的 AI 面板收起來（全螢幕的面板會蓋住新的頁面），焦點跟著被點的分頁，不還給 AI 分頁。 */
function LinkTab({ to, isCurrent, describedBy, icon, label, children }: LinkTabProps) {
  return (
    <Link
      to={to}
      className={styles.tab}
      aria-current={isCurrent ? 'page' : undefined}
      aria-describedby={describedBy}
      onClick={() => {
        closeAssistantPanel({ restoreFocus: false });
      }}
    >
      <TabContent icon={icon} label={label}>
        {children}
      </TabContent>
    </Link>
  );
}

function InboxTab({ isCurrent }: { readonly isCurrent: boolean }) {
  const { data: count } = useUnreadCount();
  const descriptionId = useId();
  const hasUnread = count !== undefined && count > 0;
  return (
    <LinkTab to="/inbox" icon="notifications" label="收件匣" isCurrent={isCurrent} describedBy={descriptionId}>
      {hasUnread && (
        <span className={styles.badge} aria-hidden="true">
          {badgeText(count)}
        </span>
      )}
      <CountDescription id={descriptionId} labels={hasUnread ? [`${String(count)} 則未讀`] : []} />
    </LinkTab>
  );
}

/** 「AI 助理」：整頁版裡是目前所在的連結；其他頁是打開浮動面板的按鈕（面板開著時是目前所在）。 */
function AssistantTab({ isFullPage, isShown, onOpen }: { readonly isFullPage: boolean; readonly isShown: boolean; readonly onOpen: () => void }) {
  const { pathname, search } = useLocation();
  if (isFullPage) {
    return <LinkTab to={`${pathname}${search}`} icon="sparkles" label="AI 助理" isCurrent />;
  }
  return (
    <button
      type="button"
      id={ASSISTANT_TAB_ID}
      className={styles.tab}
      aria-haspopup="dialog"
      aria-expanded={isShown}
      onClick={() => {
        onOpen();
        toggleAssistantPanel();
      }}
    >
      <TabContent icon="sparkles" label="AI 助理" />
    </button>
  );
}

interface TabBarProps {
  readonly isDrawerOpen: boolean;
  readonly onToggleDrawer: () => void;
  /** 收起抽屜、不搬焦點（按「AI 助理」時：面板蓋上來，抽屜不能留在底下開著）。 */
  readonly onCloseDrawer: () => void;
  readonly moreButtonRef: RefObject<HTMLButtonElement | null>;
  /** 「更多」打開的側欄（抽屜）的 id。 */
  readonly sidebarId: string;
}

/**
 * 窄螢幕（48rem 以下）固定在畫面底部的主要分頁（docs/DESIGN.md 6.1）：我的工作、專案、收件匣、AI 助理（有入口才有）、更多。
 * 「更多」打開側欄的抽屜（知識庫、專案總覽、人員、設定與帳號選單都在裡面）。寬螢幕不渲染：導覽在側欄。
 * AI 面板開著時它是目前所在的那一格，其他格不標；面板是 modal，但分頁列不被 inert（PanelWindow 的 useModalBackground），
 * 所以點別的分頁照樣能換頁。這只對指標（觸控、滑鼠）成立：面板開著時 Tab 鎖在面板裡、報讀軟體照 aria-modal 只讀面板，
 * 鍵盤與報讀的人用面板的「關閉」或 Esc 回到頁面再換分頁——面板是要先處理完的全螢幕畫面，這樣的語意是對的。
 */
export function TabBar({ isDrawerOpen, onToggleDrawer, onCloseDrawer, moreButtonRef, sidebarId }: TabBarProps) {
  const isCompact = useIsCompact();
  const nav = useNavState();
  if (!isCompact) return null;

  const { noneActive } = nav;
  return (
    <nav className={styles.bar} aria-label="主要分頁" {...{ [TAB_BAR_ATTRIBUTE]: '' }}>
      <LinkTab to="/my-work" icon="myWork" label="我的工作" isCurrent={!noneActive && nav.isUnder('/my-work')} />
      <LinkTab to={nav.projectsTarget} icon="projects" label="專案" isCurrent={!noneActive && nav.isProjects} />
      <InboxTab isCurrent={!noneActive && nav.isUnder('/inbox')} />
      {nav.hasAssistant && <AssistantTab isFullPage={nav.isFullPageAssistant} isShown={nav.isPanelShown} onOpen={onCloseDrawer} />}
      <button
        ref={moreButtonRef}
        type="button"
        className={styles.tab}
        aria-expanded={isDrawerOpen}
        aria-controls={sidebarId}
        onClick={() => {
          closeAssistantPanel({ restoreFocus: false });
          onToggleDrawer();
        }}
      >
        <TabContent icon="more" label="更多" />
      </button>
    </nav>
  );
}
