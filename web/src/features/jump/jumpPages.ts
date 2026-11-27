import { matchPath } from 'react-router';
import type { ProjectDetail, SessionUser } from '../../api/types';
import { isOrganizationAdmin } from '../../auth/CurrentUser';
import type { IconName } from '../../components/Icon/Icon';
import { changeSetsPath, projectSettingsPath } from '../work-items/itemLinks';
import { SETTINGS_HUB_GROUP_LABELS, SETTINGS_HUB_PAGES, canSeeSettingsHubPage, type SettingsHubPageKey } from '../settings-hub/settingsHubPages';
import type { JumpOption } from './jumpGroups';

/** 網址在哪個專案底下（專案頁、項目頁、設定、變更集）；專案清單與其他頁是 null。 */
export function currentProjectKey(pathname: string): string | null {
  const match = matchPath({ path: '/projects/:projectKey/*' }, pathname);
  const segment = match?.params.projectKey;
  if (segment === undefined || segment === '') return null;
  try {
    return decodeURIComponent(segment);
  } catch {
    // 編碼壞掉的網址不是任何一個專案。
    return null;
  }
}

interface JumpPagesContext {
  /** 目前登入的人：組織的擁有者或管理員才有「人員」，設定中心的每一頁照頁面表的權限。 */
  readonly user: SessionUser;
  /** 目前所在的專案（已經載入的內容）；不在專案裡是 null。 */
  readonly project: ProjectDetail | null;
}

const GLOBAL_PAGES: readonly JumpOption[] = [
  { key: 'page:my-work', label: '我的工作', icon: 'myWork', action: { kind: 'navigate', to: '/my-work' } },
  { key: 'page:knowledge', label: '知識庫', icon: 'knowledge', action: { kind: 'navigate', to: '/knowledge' } },
  { key: 'page:projects', label: '專案', icon: 'projects', action: { kind: 'navigate', to: '/projects' } },
];

const HELP_PAGE: JumpOption = { key: 'page:help', label: '使用說明', icon: 'help', action: { kind: 'navigate', to: '/help' } };

const PEOPLE_PAGE: JumpOption = { key: 'page:people', label: '人員', icon: 'people', action: { kind: 'navigate', to: '/people' } };

const SETTINGS_HUB_ICONS: Readonly<Record<SettingsHubPageKey, IconName>> = {
  account: 'account',
  notifications: 'notifications',
  'ai-accounts': 'sparkles',
  integrations: 'link',
  'description-templates': 'fileDocument',
  'email-settings': 'mail',
  'storage-usage': 'settings',
  'ai-settings': 'sparkles',
  'host-agents': 'sparkles',
};

/**
 * 設定中心的頁面：名稱與路徑直接讀頁面表（不再寫一份）。旁邊寫「個人設定」「組織設定」：和專案的「設定」分得開，
 * 也看得出是哪一組（只寫「設定」的話「寄信設定」那一列讀起來是「寄信設定 設定」）。
 */
function settingsHubPages(user: SessionUser): readonly JumpOption[] {
  return SETTINGS_HUB_PAGES.filter((page) => canSeeSettingsHubPage(page, user)).map((page) => ({
    key: `page:settings-hub:${page.key}`,
    label: page.label,
    hint: `${SETTINGS_HUB_GROUP_LABELS[page.group]}設定`,
    icon: SETTINGS_HUB_ICONS[page.key],
    action: { kind: 'navigate', to: page.path },
  }));
}

/** 目前專案的頁面。入口照權限：和專案頁的「設定」一樣，沒有 blueprint.read 就沒有設定與變更集（真正的檢查在後端）。 */
function projectPages(detail: ProjectDetail): readonly JumpOption[] {
  const { key, name } = detail.project;
  const canReadSettings = detail.myPermissions.includes('blueprint.read');
  const settings: JumpOption = {
    key: 'page:settings',
    label: '設定',
    hint: name,
    icon: 'settings',
    action: { kind: 'navigate', to: projectSettingsPath(key) },
  };
  // 成員是對話框、沒有自己的網址：選了就在原地打開（和專案頁的「成員」同一個對話框）。
  const members: JumpOption = { key: 'page:members', label: '成員', hint: name, icon: 'members', action: { kind: 'members', projectKey: key } };
  const changeSets: JumpOption = {
    key: 'page:changesets',
    label: '變更集',
    hint: name,
    icon: 'edit',
    action: { kind: 'navigate', to: changeSetsPath(key) },
  };
  return canReadSettings ? [settings, members, changeSets] : [members];
}

/** 「頁面」分組：我的工作、專案、人員（組織管理員）、設定中心的各頁（照權限）、使用說明，以及目前專案的設定、成員、變更集。 */
export function jumpPages({ user, project }: JumpPagesContext): readonly JumpOption[] {
  return [
    ...GLOBAL_PAGES,
    ...(isOrganizationAdmin(user) ? [PEOPLE_PAGE] : []),
    ...settingsHubPages(user),
    HELP_PAGE,
    ...(project === null ? [] : projectPages(project)),
  ];
}
