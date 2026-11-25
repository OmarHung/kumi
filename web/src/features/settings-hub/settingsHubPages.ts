import type { SessionUser } from '../../api/types';
import type { PageVariant } from '../../components/PageLayout/PageLayout';

/** 設定中心的兩組：自己的（每個人都有）與組織的（擁有者與管理員）。 */
export type SettingsHubGroup = 'personal' | 'organization';

export type SettingsHubPageKey =
  | 'account'
  | 'notifications'
  | 'ai-accounts'
  | 'integrations'
  | 'description-templates'
  | 'email-settings'
  | 'storage-usage'
  | 'ai-settings'
  | 'host-agents';

export type SettingsHubAccess = 'everyone' | 'admin' | 'owner';

export interface SettingsHubPage {
  readonly key: SettingsHubPageKey;
  readonly path: string;
  /** 次導覽的名稱，也是頁列的 h1 與瀏覽器分頁的標題。 */
  readonly label: string;
  readonly group: SettingsHubGroup;
  /** 誰在次導覽看得到這一項。只是隱藏入口：不夠格的人打了網址，頁面自己說沒有權限，真正的檢查在後端。 */
  readonly access: SettingsHubAccess;
  /** 閱讀頁（表單、說明）或清單頁（表格用滿寬度），docs/DESIGN.md 6.2。 */
  readonly variant: Extract<PageVariant, 'reading' | 'list'>;
}

export const SETTINGS_HUB_GROUP_LABELS: Readonly<Record<SettingsHubGroup, string>> = {
  personal: '個人',
  organization: '組織',
};

/**
 * 設定中心的每一頁（docs/design/08-interaction.md 8.3「設定中心」）：照這個順序排在次導覽。
 * 網址沿用改版前的（/account、/ai-settings…），舊的書籤、信裡與文件裡的連結照樣能用。
 */
export const SETTINGS_HUB_PAGES: readonly SettingsHubPage[] = [
  { key: 'account', path: '/account', label: '我的帳號', group: 'personal', access: 'everyone', variant: 'reading' },
  { key: 'notifications', path: '/account/notifications', label: '通知', group: 'personal', access: 'everyone', variant: 'reading' },
  { key: 'ai-accounts', path: '/account/ai', label: 'AI 帳號', group: 'personal', access: 'everyone', variant: 'reading' },
  { key: 'integrations', path: '/account/integrations', label: '命令列與整合', group: 'personal', access: 'everyone', variant: 'reading' },
  { key: 'description-templates', path: '/description-templates', label: '說明格式', group: 'organization', access: 'admin', variant: 'list' },
  { key: 'email-settings', path: '/email-settings', label: '寄信設定', group: 'organization', access: 'admin', variant: 'reading' },
  { key: 'storage-usage', path: '/storage-usage', label: '儲存空間', group: 'organization', access: 'admin', variant: 'reading' },
  { key: 'ai-settings', path: '/ai-settings', label: 'AI 設定', group: 'organization', access: 'admin', variant: 'reading' },
  { key: 'host-agents', path: '/ai-settings/host', label: '主機 AI 代理', group: 'organization', access: 'owner', variant: 'reading' },
];

/** 說明格式的新增與編輯頁不在設定中心的版面裡（填滿畫面的編輯器），但側欄的「設定」照樣是目前所在。 */
const DESCRIPTION_TEMPLATE_EDIT_PREFIX = '/description-templates/';

export function canSeeSettingsHubPage(page: SettingsHubPage, user: SessionUser): boolean {
  if (page.access === 'everyone') return true;
  // 主機 AI 代理只有組織擁有者管（管理員看不到入口；後端也一律 403）。
  if (page.access === 'owner') return user.role === 'owner';
  return user.role === 'owner' || user.role === 'admin';
}

export function visibleSettingsHubPages(user: SessionUser): readonly SettingsHubPage[] {
  return SETTINGS_HUB_PAGES.filter((page) => canSeeSettingsHubPage(page, user));
}

/** 比對用的路徑：去掉結尾的斜線、轉小寫（React Router 的路由不分大小寫，/Account/AI 也會掛上「AI 帳號」）。 */
function normalizedPath(pathname: string): string {
  const path = pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
  return path.toLowerCase();
}

/** 網址對到哪一頁（完全相同才算）；對不到是 undefined。 */
export function findSettingsHubPage(pathname: string): SettingsHubPage | undefined {
  const path = normalizedPath(pathname);
  return SETTINGS_HUB_PAGES.find((page) => page.path === path);
}

export function settingsHubPage(key: SettingsHubPageKey): SettingsHubPage {
  const page = SETTINGS_HUB_PAGES.find((candidate) => candidate.key === key);
  if (page === undefined) throw new Error(`沒有這一頁設定：${key}`);
  return page;
}

/** 側欄的「設定」是不是目前所在：設定中心的每一頁，加上說明格式的新增與編輯頁。 */
export function isSettingsHubPath(pathname: string): boolean {
  return findSettingsHubPage(pathname) !== undefined || normalizedPath(pathname).startsWith(DESCRIPTION_TEMPLATE_EDIT_PREFIX);
}
