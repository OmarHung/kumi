import type { FieldType, Permission, RoleDefinition, StateCategory } from '../api/blueprint';
import type { BuiltInProjectRole, ChangeSetStatus, OrganizationRole, Priority } from '../api/types';

/** 狀態類別是固定的五個，前端自己對照；個別狀態的名稱用藍圖裡的。 */
export const STATE_CATEGORY_LABELS: Readonly<Record<StateCategory, string>> = {
  backlog: '待規劃',
  not_started: '未開始',
  in_progress: '進行中',
  done: '完成',
  canceled: '已取消',
};

export const FIELD_TYPE_LABELS: Readonly<Record<FieldType, string>> = {
  text: '文字',
  long_text: '長文字',
  number: '數字',
  currency: '金額',
  date: '日期',
  single_select: '單選',
  multi_select: '多選',
  person: '人員',
  checkbox: '勾選',
  url: '連結',
};

export const CHANGE_SET_STATUS_LABELS: Readonly<Record<ChangeSetStatus, string>> = {
  pending: '等待核准',
  applied: '已套用',
  rejected: '已放棄',
  expired: '已過期',
};

export const PRIORITY_LABELS: Readonly<Record<Priority, string>> = {
  none: '未設定',
  low: '低',
  medium: '中',
  high: '高',
  urgent: '緊急',
};

export const ORGANIZATION_ROLE_LABELS: Readonly<Record<OrganizationRole, string>> = {
  owner: '擁有者',
  admin: '管理員',
  member: '成員',
  guest: '訪客',
};

/** 組織角色各一句說明：變更角色的對話框裡，選到哪個就寫出它是什麼。 */
export const ORGANIZATION_ROLE_DESCRIPTIONS: Readonly<Record<OrganizationRole, string>> = {
  owner: '擁有者：管理整個組織，包含人員、角色與所有專案；組織至少要留著一位。',
  admin: '管理員：管理組織的人員與所有專案，在每個專案都有全部權限。',
  member: '一般成員：看得到被加入的專案，在裡面照專案的角色工作。',
  guest: '訪客：看不到組織的人員清單，只能在被加入的專案裡照專案的角色工作。',
};

export const BUILT_IN_PROJECT_ROLE_LABELS: Readonly<Record<BuiltInProjectRole, string>> = {
  admin: '管理員',
  editor: '編輯者',
  contributor: '執行者',
  commenter: '留言者',
  viewer: '檢視者',
};

/** 內建角色的順序：權限由多到少。 */
export const BUILT_IN_PROJECT_ROLES: readonly BuiltInProjectRole[] = ['admin', 'editor', 'contributor', 'commenter', 'viewer'];

/** 內建角色各一句說明：加入成員時選了哪個角色，就在選單下面寫出它能做什麼。 */
export const BUILT_IN_PROJECT_ROLE_DESCRIPTIONS: Readonly<Record<BuiltInProjectRole, string>> = {
  admin: '全部權限，包含管理成員與專案設定。',
  editor: '看得到所有項目；能新增、修改、改狀態、刪除、留言。',
  contributor: '只看得到自己負責或自己建立的項目；能新增、修改、改狀態、留言。',
  commenter: '看得到所有項目；只能留言。',
  viewer: '看得到所有項目，不能修改。',
};

/**
 * 自訂角色能勾的權限，順序照後端 ProjectRoles.AllPermissions；設定頁的勾選與比較都照這個順序。
 * 多了 item.read_own：後端的 AllPermissions 不含它（內建管理員看得到全部，不需要「只看自己的」），
 * 所以內建管理員的權限不能直接用這份。
 */
export const PERMISSION_ORDER: readonly Permission[] = [
  'item.read',
  'item.read_own',
  'item.create',
  'item.update',
  'item.transition',
  'item.delete',
  'comment.create',
  'blueprint.read',
  'blueprint.propose',
  'blueprint.apply',
  'project.manage',
];

export const PERMISSION_LABELS: Readonly<Record<Permission, string>> = {
  'item.read': '查看項目',
  'item.read_own': '只看自己的項目',
  'item.create': '新增項目',
  'item.update': '修改項目',
  'item.transition': '改變狀態',
  'item.delete': '刪除項目',
  'comment.create': '留言',
  'blueprint.read': '查看設定',
  'blueprint.propose': '提出設定修改',
  'blueprint.apply': '套用設定修改',
  'project.manage': '管理成員',
};

/** 內建角色的權限，和後端 ProjectRoles.cs 寫死的一樣；設定頁的唯讀列用它顯示。 */
export const BUILT_IN_ROLE_PERMISSIONS: Readonly<Record<BuiltInProjectRole, readonly Permission[]>> = {
  admin: PERMISSION_ORDER.filter((permission) => permission !== 'item.read_own'),
  editor: ['item.read', 'item.create', 'item.update', 'item.transition', 'item.delete', 'comment.create', 'blueprint.read'],
  contributor: ['item.read_own', 'item.create', 'item.update', 'item.transition', 'comment.create', 'blueprint.read'],
  commenter: ['item.read', 'comment.create', 'blueprint.read'],
  viewer: ['item.read', 'blueprint.read'],
};

/** 「只看自己的項目」的說明（設定頁勾選的旁邊）：和「查看項目」擇一，兩個都沒勾就看不到任何項目。 */
export const ITEM_READ_OWN_NOTE = '「查看項目」看得到所有項目；「只看自己的項目」只看得到自己負責或建立的項目。兩個擇一，勾其中一個會取消另一個；都不勾就看不到任何項目。';

/** 照 PERMISSION_ORDER 排（同一組權限勾選的先後不同，送出的也一樣）。 */
export function orderedPermissions(permissions: readonly Permission[]): readonly Permission[] {
  return PERMISSION_ORDER.filter((permission) => permissions.includes(permission));
}

export function isBuiltInProjectRole(role: string): role is BuiltInProjectRole {
  return Object.hasOwn(BUILT_IN_PROJECT_ROLE_LABELS, role);
}

/**
 * 專案角色的顯示名稱。自訂角色的名稱只有藍圖裡有；專案清單沒有藍圖，這時只能顯示 key。
 * null 是組織管理員看得到、但沒有被加進專案的情況。
 */
export function projectRoleLabel(
  role: string | null,
  customRoles: readonly RoleDefinition[] = [],
): string {
  if (role === null) {
    return '未加入';
  }
  if (isBuiltInProjectRole(role)) {
    return BUILT_IN_PROJECT_ROLE_LABELS[role];
  }
  return customRoles.find((definition) => definition.key === role)?.name ?? role;
}
