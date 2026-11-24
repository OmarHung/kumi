import type { Permission, ProjectBlueprint } from '../api/blueprint';
import type { Change } from '../api/types';
import { nameIn, roleName, type ChangeSetBlueprints } from './changeSetNames';
import { fieldName, quotedList, transitionIn, workflowTransitionsText } from './changeSetTransitions';
import { viewFilterText } from './changeSetViewFilter';
import { orderedPermissions, PERMISSION_LABELS } from './labels';

/**
 * 轉換、類型、角色、檢視裡清單型的屬性（允許的角色、要先填的欄位、上層、權限、看得到的角色、篩選條件）與流程的轉換說成句子：
 * 改成了哪些，或多了哪些、少了哪些。名稱先看改之後的藍圖（新的角色、類型），再看改之前的。
 */

function allowedRolesText(change: Change, context: ChangeSetBlueprints): string {
  const roles = transitionIn(context.after, change)?.allowedRoles ?? [];
  return roles.length === 0 ? '改成不限角色' : `允許的角色改成${quotedList(roles.map((role) => roleName(context, role)))}`;
}

function requireFieldsText(change: Change, context: ChangeSetBlueprints): string {
  const fields = transitionIn(context.after, change)?.requireFields ?? [];
  return fields.length === 0 ? '改成不必先填任何欄位' : `要先填的欄位改成${quotedList(fields.map((field) => fieldName(context, field)))}`;
}

function typeWorkflowText(change: Change, { before, after }: ChangeSetBlueprints): string {
  const workflowOf = (blueprint: ProjectBlueprint) => blueprint.itemTypes.find((type) => type.key === change.key)?.workflow ?? '';
  const from = nameIn(before, after, 'workflow', workflowOf(before));
  const to = nameIn(after, before, 'workflow', workflowOf(after));
  return `流程從「${from}」改成「${to}」`;
}

function parentsText(change: Change, context: ChangeSetBlueprints): string {
  const parents = context.after.itemTypes.find((type) => type.key === change.key)?.parents ?? [];
  const names = parents.map((parent) => nameIn(context.after, context.before, 'item_type', parent));
  return names.length === 0 ? '不能掛在任何類型底下' : `可以掛的上層類型改成${quotedList(names)}`;
}

/** 權限多了哪些、少了哪些；同一組只是換了順序就說順序。 */
function permissionsText(change: Change, { before, after }: ChangeSetBlueprints): string {
  const permissionsOf = (blueprint: ProjectBlueprint): readonly Permission[] =>
    blueprint.roles?.find((role) => role.key === change.key)?.permissions ?? [];
  const old = permissionsOf(before);
  const next = permissionsOf(after);
  const labels = (permissions: readonly Permission[]) => quotedList(permissions.map((permission) => PERMISSION_LABELS[permission]));
  const added = next.filter((permission) => !old.includes(permission));
  const removed = old.filter((permission) => !next.includes(permission));
  const parts = [added.length > 0 ? `權限多了${labels(added)}` : '', removed.length > 0 ? `少了${labels(removed)}` : ''];
  const text = parts.filter((part) => part !== '').join('，');
  return text === '' ? '調整了權限的順序' : text;
}

/** 檢視沒寫 visibleTo 是所有成員都看得到；空陣列是一個角色都沒列（和沒寫不一樣）。 */
function visibleToText(change: Change, context: ChangeSetBlueprints): string {
  const roles = context.after.views?.find((view) => view.key === change.key)?.visibleTo;
  if (roles === undefined) return '改成所有成員都看得到';
  if (roles.length === 0) return '看得到的角色改成一個都沒列';
  return `看得到的角色改成${quotedList(roles.map((role) => roleName(context, role)))}`;
}

/** 新增的角色後面接權限（照固定的順序）：「，權限：查看項目、留言」。 */
export function addedRoleSuffix(change: Change, { after }: ChangeSetBlueprints): string {
  const permissions = orderedPermissions(after.roles?.find((role) => role.key === change.key)?.permissions ?? []);
  return permissions.length === 0 ? '，沒有任何權限' : `，權限：${permissions.map((permission) => PERMISSION_LABELS[permission]).join('、')}`;
}

/** 以「資源.屬性」為鍵。 */
export const LIST_PROPERTY_TEXTS: Readonly<Record<string, (change: Change, context: ChangeSetBlueprints) => string>> = {
  'transition.allowedRoles': allowedRolesText,
  'transition.requireFields': requireFieldsText,
  'item_type.workflow': typeWorkflowText,
  'item_type.parents': parentsText,
  'role.permissions': permissionsText,
  'view.visibleTo': visibleToText,
  'view.filter': viewFilterText,
  'workflow.transitions': workflowTransitionsText,
};
