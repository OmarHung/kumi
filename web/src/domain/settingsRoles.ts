import type { Permission, ProjectBlueprint, RoleDefinition } from '../api/blueprint';
import type { BuiltInProjectRole } from '../api/types';
import { BUILT_IN_PROJECT_ROLE_LABELS, BUILT_IN_PROJECT_ROLES, BUILT_IN_ROLE_PERMISSIONS, isBuiltInProjectRole, orderedPermissions } from './labels';
import { byKey, restoreInOrder, sameName, sameSet, stripTransitionReferences, type DraftStatus, type SettingsDraft } from './settingsDraft';
import { visibleTransitions } from './settingsTransitions';

/**
 * 設定頁的角色：自訂角色的新增、改名、權限勾選、刪除與復原。內建的四個角色寫死在後端，這裡只列出來、不能改。
 * 刪除角色時，轉換的 allowedRoles 與檢視的 visibleTo 裡的引用在組變更集時才拿掉（復原時引用自動回來）。
 */

export type RolePatch = Partial<Pick<RoleDefinition, 'name' | 'permissions'>>;

export interface BuiltInRole {
  readonly key: BuiltInProjectRole;
  readonly name: string;
  readonly permissions: readonly Permission[];
}

export const BUILT_IN_ROLES: readonly BuiltInRole[] = BUILT_IN_PROJECT_ROLES.map((key) => ({
  key,
  name: BUILT_IN_PROJECT_ROLE_LABELS[key],
  permissions: BUILT_IN_ROLE_PERMISSIONS[key],
}));

/** 新角色預設和「檢視者」一樣：只能看，要給更多再勾。 */
export const DEFAULT_ROLE_PERMISSIONS: readonly Permission[] = BUILT_IN_ROLE_PERMISSIONS.viewer;

function mapRole(draft: SettingsDraft, roleKey: string, update: (role: RoleDefinition) => RoleDefinition): SettingsDraft {
  return { ...draft, roles: draft.roles.map((role) => (role.key === roleKey ? update(role) : role)) };
}

export function addRole(draft: SettingsDraft, role: RoleDefinition): SettingsDraft {
  return { ...draft, roles: [...draft.roles, { ...role, permissions: orderedPermissions(role.permissions) }] };
}

/** 權限照 PERMISSION_ORDER 存：勾選的先後不同也是同一組。 */
export function updateRole(draft: SettingsDraft, roleKey: string, patch: RolePatch): SettingsDraft {
  const permissions = patch.permissions === undefined ? {} : { permissions: orderedPermissions(patch.permissions) };
  return mapRole(draft, roleKey, (role) => ({ ...role, ...patch, ...permissions }));
}

/** 目前藍圖裡就有的角色，引用留在草稿裡（送出時才拿掉，復原時跟著回來）；剛新增的連引用一起拿掉。 */
export function removeRole(base: ProjectBlueprint, draft: SettingsDraft, roleKey: string): SettingsDraft {
  const removed = { ...draft, roles: draft.roles.filter((role) => role.key !== roleKey) };
  if ((base.roles ?? []).some((role) => role.key === roleKey)) {
    return removed;
  }
  return stripTransitionReferences(removed, (transition) =>
    transition.allowedRoles?.includes(roleKey) === true
      ? { ...transition, allowedRoles: transition.allowedRoles.filter((role) => role !== roleKey) }
      : transition,
  );
}

/** 復原刪除的角色：名稱與權限回到目前藍圖裡的樣子，放回原本的位置。 */
export function restoreRole(base: ProjectBlueprint, draft: SettingsDraft, roleKey: string): SettingsDraft {
  const original = base.roles ?? [];
  const role = original.find((candidate) => candidate.key === roleKey);
  return role === undefined ? draft : { ...draft, roles: restoreInOrder(draft.roles, original, role, byKey) };
}

/** 目前藍圖裡有、草稿裡沒有的自訂角色，照目前藍圖的順序。 */
export function removedRoles(base: ProjectBlueprint, draft: SettingsDraft): readonly RoleDefinition[] {
  return (base.roles ?? []).filter((role) => !draft.roles.some((candidate) => candidate.key === role.key));
}

export function roleStatus(base: ProjectBlueprint, draft: SettingsDraft, roleKey: string): DraftStatus {
  const original = base.roles?.find((role) => role.key === roleKey);
  const role = draft.roles.find((candidate) => candidate.key === roleKey);
  if (original === undefined) {
    return 'added';
  }
  if (role === undefined) {
    return 'unchanged';
  }
  return sameName(role.name, original.name) && sameSet(role.permissions, original.permissions) ? 'unchanged' : 'modified';
}

/** 刪除角色時會一起拿掉的引用。becomesEmpty：拿掉要刪除的角色之後清單變空（轉換變成不限角色、檢視變成所有成員都看得到）。 */
export type RoleReference =
  | { readonly kind: 'transition'; readonly workflow: string; readonly from: string; readonly to: string; readonly becomesEmpty: boolean }
  | { readonly kind: 'view'; readonly view: string; readonly becomesEmpty: boolean };

/** 用到這個角色的轉換（草稿裡還在的）與檢視，照藍圖的順序。 */
export function roleReferences(base: ProjectBlueprint, draft: SettingsDraft, roleKey: string): readonly RoleReference[] {
  const removed = new Set([roleKey, ...removedRoles(base, draft).map((role) => role.key)]);
  const isEmptied = (roles: readonly string[]) => roles.every((role) => removed.has(role));
  const transitions = draft.workflows.flatMap((workflow) =>
    (visibleTransitions(workflow) ?? [])
      .filter((transition) => transition.allowedRoles?.includes(roleKey))
      .map((transition) => ({
        kind: 'transition' as const,
        workflow: workflow.key,
        from: transition.from,
        to: transition.to,
        becomesEmpty: isEmptied(transition.allowedRoles ?? []),
      })),
  );
  const views = (base.views ?? [])
    .filter((view) => view.visibleTo?.includes(roleKey))
    .map((view) => ({ kind: 'view' as const, view: view.key, becomesEmpty: isEmptied(view.visibleTo ?? []) }));
  return [...transitions, ...views];
}

/** 轉換的允許角色可以選哪些：內建的四個，加上草稿裡的自訂角色（照草稿的順序）。 */
export function assignableRoles(draft: SettingsDraft): readonly { readonly key: string; readonly name: string }[] {
  return [...BUILT_IN_ROLES, ...draft.roles];
}

/** 角色的權限：內建的照後端寫死的（labels.ts），自訂的照草稿；不在草稿裡（要刪除、送出時會拿掉）的是 undefined。 */
export function draftRolePermissions(draft: SettingsDraft, roleKey: string): readonly Permission[] | undefined {
  return isBuiltInProjectRole(roleKey) ? BUILT_IN_ROLE_PERMISSIONS[roleKey] : draft.roles.find((role) => role.key === roleKey)?.permissions;
}

/** 這個角色能不能改變狀態：伺服器改狀態先看 item.transition，再看轉換的 allowedRoles。 */
export function canTransitionItems(draft: SettingsDraft, roleKey: string): boolean {
  return draftRolePermissions(draft, roleKey)?.includes('item.transition') === true;
}

/**
 * 勾選之後的權限：「查看項目」與「只看自己的項目」擇一。伺服器同時有兩個時以「查看項目」為準，
 * 兩個都勾的角色看起來像是只看自己的、實際卻看得到全部；所以剛勾的那一個留下、另一個拿掉。
 */
export function exclusiveReadPermissions(previous: readonly Permission[], next: readonly Permission[]): readonly Permission[] {
  if (!next.includes('item.read') || !next.includes('item.read_own')) return next;
  const dropped: Permission = previous.includes('item.read_own') ? 'item.read_own' : 'item.read';
  return next.filter((permission) => permission !== dropped);
}
