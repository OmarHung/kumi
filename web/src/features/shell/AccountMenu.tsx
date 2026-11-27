import { useNavigate } from 'react-router';
import { errorMessage } from '../../api/http';
import { useCurrentUser } from '../../auth/CurrentUser';
import { useSignOut } from '../../auth/session';
import { Avatar } from '../../components/Avatar/Avatar';
import { Icon } from '../../components/Icon/Icon';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import { useThemeChoice, type ThemeChoice } from '../../lib/theme';
import { useLayoutTier } from '../../lib/useLayoutTier';
import { useLauncherPosition } from '../assistant-panel/launcherPosition';
import { resetLauncherPositionAndFocus, showLauncherAndFocus, useLauncherHidden } from '../assistant-panel/launcherPreference';
import { useHasAssistantEntry } from '../assistant-panel/useAssistantEntry';
import { setWeekStart, useWeekStart, WEEK_STARTS, type WeekStart } from '../../lib/weekStart';
import styles from './Sidebar.module.css';

const THEME_OPTIONS: readonly { readonly choice: ThemeChoice; readonly label: string }[] = [
  { choice: 'system', label: '主題跟著系統' },
  { choice: 'light', label: '淺色主題' },
  { choice: 'dark', label: '深色主題' },
];

const WEEK_START_LABELS: Readonly<Record<WeekStart, string>> = {
  monday: '一週從週一開始',
  sunday: '一週從週日開始',
};

/**
 * 主題三選一。用共用 Menu 的勾選型項目（menuitemradio）：目前的那一個帶 aria-checked 並畫打勾，
 * 報讀軟體唸得到、看得到也不只靠圖示或顏色。再選一次同一個不會怎樣。
 */
function themeItems(current: ThemeChoice, choose: (choice: ThemeChoice) => void): MenuItem[] {
  return THEME_OPTIONS.map((option, index) => ({
    key: `theme-${option.choice}`,
    label: option.label,
    isChecked: option.choice === current,
    startsGroup: index === 0,
    onSelect: () => {
      choose(option.choice);
    },
  }));
}

/** 一週從哪天開始二選一（個人偏好，lib/weekStart.ts）：行事曆、選日期的小月曆、時間軸與「本週」全都照它算。 */
function weekStartItems(current: WeekStart): MenuItem[] {
  return WEEK_STARTS.map((choice, index) => ({
    key: `week-start-${choice}`,
    label: WEEK_START_LABELS[choice],
    isChecked: choice === current,
    startsGroup: index === 0,
    onSelect: () => {
      setWeekStart(choice);
    },
  }));
}

/** 「我的帳號」：在主題那一組前面（internal/HANDOFF-security-local-ops.md 第 7 節第 1 點）。 */
function accountItem(open: () => void): MenuItem {
  return { key: 'account', label: '我的帳號', icon: 'account', onSelect: open };
}

/**
 * 右下角的 AI 助理按鈕被使用者藏起來之後在這裡叫回來、被拖開之後在這裡放回右下角（只在有助理可用時出現；
 * 藏起來又移動過的兩項都列）。點兩下按鈕不當作放回：點一下就會打開面板。
 */
function useAssistantLauncherItems(): MenuItem[] {
  const mode = useLayoutTier();
  const isLauncherHidden = useLauncherHidden();
  const isMoved = useLauncherPosition() !== null;
  const hasAssistant = useHasAssistantEntry();
  // 窄螢幕與圖示列模式沒有右下角的按鈕（入口在底部分頁列、圖示列），叫回來、放回右下角都沒有意義。
  if (!hasAssistant || mode !== 'wide') return [];
  const items: MenuItem[] = [];
  if (isLauncherHidden) items.push({ key: 'show-assistant', label: '顯示 AI 助理按鈕', icon: 'sparkles', onSelect: showLauncherAndFocus });
  if (isMoved) {
    items.push({ key: 'reset-assistant-position', label: '把 AI 助理按鈕放回右下角', icon: 'sparkles', onSelect: resetLauncherPositionAndFocus });
  }
  return items.map((item, index) => ({ ...item, startsGroup: index === 0 }));
}

/** 側欄與圖示列共用：選單的項目、按鈕的名稱與登出的狀態。 */
function useAccountMenu() {
  const user = useCurrentUser();
  const signOut = useSignOut();
  const [theme, chooseTheme] = useThemeChoice();
  const weekStart = useWeekStart();
  const navigate = useNavigate();
  const assistantItems = useAssistantLauncherItems();
  // 登出送出中：按鈕上「組織 · 角色」那一行換成「登出中…」，看得到也唸得到（選單這時打不開）。
  const meta = signOut.isPending ? '登出中…' : `${user.organizationName} · ${ORGANIZATION_ROLE_LABELS[user.role]}`;
  const items: MenuItem[] = [
    accountItem(() => {
      void navigate('/account');
    }),
    ...themeItems(theme, chooseTheme),
    ...weekStartItems(weekStart),
    ...assistantItems,
    {
      key: 'sign-out',
      label: '登出',
      icon: 'signOut',
      startsGroup: true,
      onSelect: () => {
        signOut.mutate();
      },
    },
  ];
  // 名稱與組織都看得到；無障礙名稱照看得到的文字排，中間加上括號才不會黏在一起。
  return { user, signOut, meta, items, label: `${user.displayName}（${meta}）` };
}

function SignOutError({ signOut }: { readonly signOut: ReturnType<typeof useSignOut> }) {
  if (!signOut.isError) return null;
  return (
    <p role="alert" className={styles.error}>
      登出失敗：{errorMessage(signOut.error)}
    </p>
  );
}

/**
 * 側欄最下面的帳號選單（docs/DESIGN.md 6.1）：按鈕上是名稱與組織，打開是「我的帳號」、主題、一週從哪天開始與登出。
 * 登出送出中選單打不開（按鈕仍然停得到，標 aria-disabled），按鈕上寫「登出中…」；失敗時說明留在按鈕下面，畫面不動。
 */
export function AccountMenu() {
  const { user, signOut, meta, items, label } = useAccountMenu();
  return (
    <div className={styles.account}>
      <Menu
        menuLabel="帳號"
        items={items}
        isDisabled={signOut.isPending}
        renderTrigger={(trigger) => (
          <button {...trigger} type="button" className={styles.accountButton} aria-label={label}>
            <Avatar name={user.displayName} decorative />
            <span className={styles.accountText}>
              <span className={styles.userName}>{user.displayName}</span>
              <span className={styles.userMeta}>{meta}</span>
            </span>
            <Icon name="more" className={styles.accountIcon} />
          </button>
        )}
      />
      <SignOutError signOut={signOut} />
    </div>
  );
}

/** 圖示列最下面的帳號：只有頭像，選單與側欄的帳號選單同一份。登出失敗的說明接在頭像下面（圖示列很窄，文字換行）。 */
export function RailAccountMenu({ buttonClassName }: { readonly buttonClassName: string | undefined }) {
  const { user, signOut, items, label } = useAccountMenu();
  return (
    <>
      <Menu
        menuLabel="帳號"
        items={items}
        align="start"
        isDisabled={signOut.isPending}
        renderTrigger={(trigger) => (
          <button {...trigger} type="button" className={buttonClassName} aria-label={label}>
            <Avatar name={user.displayName} decorative />
          </button>
        )}
      />
      <SignOutError signOut={signOut} />
    </>
  );
}
