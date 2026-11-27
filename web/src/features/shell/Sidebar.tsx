import { useId, type KeyboardEvent, type ReactNode, type Ref } from 'react';
import { Link, NavLink, useLocation } from 'react-router';
import { useProjects } from '../../api/queries';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Icon, type IconName } from '../../components/Icon/Icon';
import { PRODUCT_NAME } from '../../config/product';
import { cx } from '../../lib/cx';
import { projectInfoPath } from '../work-items/itemLinks';
import { InboxCount } from '../inbox/InboxCount';
import { JumpButton } from '../jump/JumpButton';
import { MyWorkCount } from '../my-work/MyWorkCount';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { isSettingsHubPath } from '../settings-hub/settingsHubPages';
import { AccountMenu } from './AccountMenu';
import styles from './Sidebar.module.css';

/** 側欄的標誌和瀏覽器分頁的圖示是同一個檔案（index.html 也指到它）：兩邊永遠一樣，改一份就好。 */
const BRAND_MARK_SRC = '/favicon.svg';

interface MainLinkProps {
  readonly to: string;
  readonly icon: IconName;
  readonly label: string;
  readonly end?: boolean;
  /** 名稱後面的東西（我的工作的數量）：畫面上的部分 aria-hidden，說法用 describedBy 連過來，名稱維持只有 label。 */
  readonly children?: ReactNode;
  readonly describedBy?: string;
  readonly className?: string;
}

/** 主要導覽的一項：圖示是裝飾，名稱只有文字。目前所在的那一項由 NavLink 標 aria-current，樣式照它選。 */
function MainLink({ to, icon, label, end = false, children, describedBy, className }: MainLinkProps) {
  return (
    <NavLink to={to} end={end} className={cx(styles.link, className)} aria-describedby={describedBy}>
      <Icon name={icon} className={styles.linkIcon} />
      <span className={styles.linkText}>{label}</span>
      {children}
    </NavLink>
  );
}

/**
 * 「設定」：設定中心的入口（我的帳號、通知…，管理員還有組織的設定，docs/DESIGN.md 6.1）。
 * 設定中心的網址不在同一個前綴底下（/account、/ai-settings、/email-settings…），NavLink 比對不到，自己判斷目前所在。
 */
function SettingsLink() {
  const { pathname } = useLocation();
  return (
    <Link to="/account" className={styles.link} aria-current={isSettingsHubPath(pathname) ? 'page' : undefined}>
      <Icon name="settings" className={styles.linkIcon} />
      <span className={styles.linkText}>設定</span>
    </Link>
  );
}

function RetryNote({ message, onRetry }: { readonly message: string; readonly onRetry: () => void }) {
  return (
    <p className={styles.note}>
      {message}
      <button type="button" className={styles.inlineButton} onClick={onRetry}>
        重試
      </button>
    </p>
  );
}

/**
 * 專案連結照這個專案上次用的檢視開（表格或看板，features/projects/projectViewPreference.ts）。
 * 目前所在的專案照 NavLink 標 aria-current（只比對路徑，所以 ?view=board 與 /info 都算）。
 * 右邊的「資訊」圖示是兄弟連結（連結裡不能再放連結），平常藏著、滑過或有焦點時才出現（Sidebar.module.css）：
 * 專案一多，每列都擺一個圖示太吵，名稱也已經被截斷。點了換頁，窄螢幕的抽屜跟著換頁一起關上（useDrawer）。
 */
function ProjectLink({ projectKey, name }: { readonly projectKey: string; readonly name: string }) {
  return (
    <li className={styles.projectItem}>
      <NavLink to={useProjectPageLink(projectKey)} className={styles.link}>
        <span className={styles.projectKey}>{projectKey}</span>
        <span className={styles.projectName}>{name}</span>
      </NavLink>
      <Link to={projectInfoPath(projectKey)} className={styles.infoLink} aria-label={`${projectKey} 的資訊`} title="資訊">
        <Icon name="info" />
      </Link>
    </li>
  );
}

function ProjectLinks() {
  const projects = useProjects();
  const retry = () => {
    void projects.refetch();
  };
  if (projects.isPending) {
    return <p className={styles.note}>載入中…</p>;
  }
  // 已經有清單時重抓失敗，連結照樣留著，只在下面說明。
  if (projects.isLoadingError) {
    return <RetryNote message="專案清單載入失敗。" onRetry={retry} />;
  }
  // 封存的專案只能看：側欄不列，要找得到的話在專案清單頁（「已封存的專案」）與快速跳轉。
  const active = projects.data.filter((project) => project.archivedAt === null);
  if (active.length === 0) {
    return <p className={styles.note}>{projects.data.length === 0 ? '還沒有專案' : '沒有進行中的專案'}</p>;
  }
  return (
    <>
      <ul className={styles.projectList}>
        {active.map((project) => (
          <ProjectLink key={project.id} projectKey={project.key} name={project.name} />
        ))}
      </ul>
      {projects.isRefetchError && <RetryNote message="專案清單無法更新。" onRetry={retry} />}
    </>
  );
}

interface SidebarProps {
  readonly ref?: Ref<HTMLElement>;
  readonly id: string;
  /** 只影響窄螢幕：抽屜打開了沒有。 */
  readonly isOpen: boolean;
  /** 只影響寬螢幕：使用者把側欄收起來了（窄螢幕一律是抽屜，外框不會給 true）。 */
  readonly isCollapsed: boolean;
  readonly onClose: () => void;
  /** 導覽的名稱：圖示列才是「主要導覽」，抽屜裡的叫別的，兩個地標才不同名。 */
  readonly navLabel?: string;
}

/**
 * 側欄是桌面（docs/DESIGN.md 6.1）：產品名稱、主要導覽、最下面的帳號選單。
 * 專案多了只有專案清單自己捲，「人員」與帳號選單一直看得到。收起來時用 hidden：裡面的連結不會被 Tab 走到，也不在無障礙樹裡。
 */
export function Sidebar({ ref, id, isOpen, isCollapsed, onClose, navLabel = '主要導覽' }: SidebarProps) {
  const user = useCurrentUser();
  const myWorkCountId = useId();
  const inboxCountId = useId();

  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (isOpen && event.key === 'Escape') {
      onClose();
    }
  }

  return (
    <aside ref={ref} id={id} className={styles.sidebar} data-open={isOpen} hidden={isCollapsed} onKeyDown={handleKeyDown}>
      <Link to="/" className={styles.brand}>
        {/* 標誌是裝飾：連結的名稱只有產品名稱。 */}
        <img className={styles.brandMark} src={BRAND_MARK_SRC} alt="" aria-hidden="true" />
        {PRODUCT_NAME}
      </Link>
      <JumpButton />
      <nav aria-label={navLabel} className={styles.nav}>
        <MainLink to="/my-work" icon="myWork" label="我的工作" describedBy={myWorkCountId}>
          <MyWorkCount id={myWorkCountId} />
        </MainLink>
        <MainLink to="/overview" icon="overview" label="專案總覽" />
        <MainLink to="/inbox" icon="notifications" label="收件匣" describedBy={inboxCountId}>
          <InboxCount id={inboxCountId} />
        </MainLink>
        <MainLink to="/knowledge" icon="knowledge" label="知識庫" />
        <MainLink to="/projects" icon="projects" label="專案" end className={styles.sectionLink} />
        <div className={styles.projects}>
          <ProjectLinks />
        </div>
        {isOrganizationAdmin(user) && <MainLink to="/people" icon="people" label="人員" />}
        <SettingsLink />
        <MainLink to="/help" icon="help" label="使用說明" />
      </nav>
      <AccountMenu />
    </aside>
  );
}
