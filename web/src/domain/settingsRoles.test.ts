import { describe, expect, it } from 'vitest';
import type { Permission, ProjectBlueprint } from '../api/blueprint';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { buildChangeSet } from './settingsChangeSet';
import { draftFromBlueprint } from './settingsDraft';
import {
  addRole,
  assignableRoles,
  BUILT_IN_ROLES,
  exclusiveReadPermissions,
  removedRoles,
  removeRole,
  restoreRole,
  roleReferences,
  roleStatus,
  updateRole,
} from './settingsRoles';
import { pendingChangeCount } from './settingsStatus';
import { updateTransition } from './settingsTransitions';

/** 和本機 AGY 一樣：「給客戶看」只有客戶與專案經理看得到。 */
const agy: ProjectBlueprint = {
  ...agencyBlueprint,
  views: [
    { key: 'progress_board', name: '進度看板', type: 'board', groupBy: 'state', columns: [], sort: [] },
    { key: 'client_view', name: '給客戶看', type: 'table', columns: ['title'], sort: [], visibleTo: ['client', 'project_manager'] },
  ],
};

describe('設定的草稿：角色', () => {
  it('新增角色：權限照固定的順序存，送出的形狀和 API 一樣', () => {
    // Arrange & Act
    const draft = addRole(draftFromBlueprint(agy), { key: 'qa_lead', name: ' QA 負責人 ', permissions: ['blueprint.read', 'item.read'] });

    // Assert
    expect(buildChangeSet(agy, 1, draft, '').blueprint.roles?.at(-1)).toEqual({ key: 'qa_lead', name: 'QA 負責人', permissions: ['item.read', 'blueprint.read'] });
    expect(roleStatus(agy, draft, 'qa_lead')).toBe('added');
    expect(pendingChangeCount(agy, draft)).toBe(1);
  });

  it('改名與權限；改回同一組權限（勾選先後不同）不算修改，送出原本的物件', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const changed = updateRole(draft, 'client', { name: '業主', permissions: ['comment.create', 'item.read', 'item.create'] });
    const reverted = updateRole(changed, 'client', { name: '客戶 ', permissions: ['comment.create', 'item.read'] });

    // Assert
    expect(buildChangeSet(agy, 1, changed, '').blueprint.roles?.[1]).toEqual({ key: 'client', name: '業主', permissions: ['item.read', 'item.create', 'comment.create'] });
    expect(roleStatus(agy, changed, 'client')).toBe('modified');
    expect(roleStatus(agy, reverted, 'client')).toBe('unchanged');
    expect(buildChangeSet(agy, 1, reverted, '').blueprint.roles?.[1]).toBe(agy.roles?.[1]);
  });

  it('只改了名稱：權限和原本是同一組就送原本的陣列，勾過又取消不會讓權限被重新排序', () => {
    // Arrange
    const base: ProjectBlueprint = { ...agy, roles: [{ key: 'client', name: '客戶', permissions: ['comment.create', 'item.read'] }] };
    const toggled = updateRole(updateRole(draftFromBlueprint(base), 'client', { permissions: ['comment.create', 'item.read', 'item.create'] }), 'client', {
      permissions: ['comment.create', 'item.read'],
    });

    // Act
    const renamed = updateRole(toggled, 'client', { name: '業主' });

    // Assert
    const sent = buildChangeSet(base, 1, renamed, '').blueprint.roles?.[0];
    expect(sent).toEqual({ key: 'client', name: '業主', permissions: ['comment.create', 'item.read'] });
    expect(sent?.permissions).toBe(base.roles?.[0]?.permissions);
  });

  it('刪除角色：拿掉轉換的 allowedRoles 與檢視 visibleTo 裡的引用；復原後都回來', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const removed = removeRole(agy, draft, 'project_manager');
    const restored = restoreRole(agy, removed, 'project_manager');

    // Assert
    const blueprint = buildChangeSet(agy, 1, removed, '').blueprint;
    expect(blueprint.roles).toEqual([agy.roles?.[1]]);
    expect(blueprint.workflows[0]?.transitions?.slice(5, 6)).toEqual([{ from: 'internal_qa', to: 'client_qa', allowedRoles: [], requireFields: [] }]);
    expect(blueprint.workflows[0]?.transitions?.at(-1)).toEqual({ from: 'client_qa', to: 'closed', allowedRoles: [], requireFields: ['client_signoff_date'] });
    expect(blueprint.views?.[1]?.visibleTo).toEqual(['client']);
    expect(blueprint.views?.[0]).toBe(agy.views?.[0]);
    expect(removedRoles(agy, removed)).toEqual([agy.roles?.[0]]);
    expect(pendingChangeCount(agy, removed)).toBe(1);
    expect(buildChangeSet(agy, 1, restored, '').blueprint).toEqual(agy);
    expect(pendingChangeCount(agy, restored)).toBe(0);
  });

  it('visibleTo 拿掉之後變空就整個不送（所有成員都看得到）', () => {
    // Arrange & Act
    const draft = removeRole(agy, removeRole(agy, draftFromBlueprint(agy), 'client'), 'project_manager');

    // Assert
    const view = buildChangeSet(agy, 1, draft, '').blueprint.views?.[1];
    expect(view).toEqual({ key: 'client_view', name: '給客戶看', type: 'table', columns: ['title'], sort: [] });
    expect(view && 'visibleTo' in view).toBe(false);
  });

  it('刪除時會一起拿掉的引用，以及哪些拿掉之後清單變空', () => {
    // Arrange
    const draft = updateTransition(removeRole(agy, draftFromBlueprint(agy), 'client'), 'delivery', 'confirming', 'quoting', { allowedRoles: ['client', 'admin'] });

    // Act
    const references = roleReferences(agy, removeRole(agy, draft, 'project_manager'), 'project_manager');

    // Assert
    expect(references).toEqual([
      { kind: 'transition', workflow: 'delivery', from: 'internal_qa', to: 'client_qa', becomesEmpty: true },
      { kind: 'transition', workflow: 'delivery', from: 'client_qa', to: 'closed', becomesEmpty: true },
      { kind: 'view', view: 'client_view', becomesEmpty: true },
    ]);
    expect(roleReferences(agy, draft, 'client')).toEqual([
      { kind: 'transition', workflow: 'delivery', from: 'confirming', to: 'quoting', becomesEmpty: false },
      { kind: 'view', view: 'client_view', becomesEmpty: false },
    ]);
  });

  it('原本沒有 roles 屬性、也沒有自訂角色時不送 roles；API 回的空陣列照樣送', () => {
    // Arrange
    const { roles, ...withoutRoles } = simpleTasksBlueprint;

    // Act
    const blueprint = buildChangeSet(withoutRoles, 1, draftFromBlueprint(withoutRoles), '').blueprint;

    // Assert
    expect(roles).toEqual([]);
    expect('roles' in blueprint).toBe(false);
    expect(buildChangeSet(simpleTasksBlueprint, 1, draftFromBlueprint(simpleTasksBlueprint), '').blueprint.roles).toEqual([]);
  });

  it('轉換可以選的角色：內建的五個，再接草稿裡的自訂角色', () => {
    // Arrange
    const draft = addRole(draftFromBlueprint(agy), { key: 'qa_lead', name: 'QA', permissions: [] });

    // Act & Assert
    expect(assignableRoles(draft).map((role) => role.key)).toEqual(['admin', 'editor', 'contributor', 'commenter', 'viewer', 'project_manager', 'client', 'qa_lead']);
    expect(BUILT_IN_ROLES.find((role) => role.key === 'editor')?.permissions).toEqual([
      'item.read',
      'item.create',
      'item.update',
      'item.transition',
      'item.delete',
      'comment.create',
      'blueprint.read',
    ]);
  });
});

describe('內建的執行者與「只看自己的項目」', () => {
  it('執行者只有 item.read_own，沒有 item.read，也不能刪除、不能管專案', () => {
    // Arrange & Act
    const permissions = BUILT_IN_ROLES.find((role) => role.key === 'contributor')?.permissions;

    // Assert
    expect(permissions).toEqual(['item.read_own', 'item.create', 'item.update', 'item.transition', 'comment.create', 'blueprint.read']);
    expect(BUILT_IN_ROLES.find((role) => role.key === 'contributor')?.name).toBe('執行者');
  });

  it('剛勾「只看自己的項目」時拿掉「查看項目」，反過來也一樣；勾別的不受影響', () => {
    // Arrange
    const viewing: readonly Permission[] = ['item.read', 'blueprint.read'];
    const own: readonly Permission[] = ['item.read_own', 'blueprint.read'];

    // Act & Assert
    expect(exclusiveReadPermissions(viewing, ['item.read', 'item.read_own', 'blueprint.read'])).toEqual(['item.read_own', 'blueprint.read']);
    expect(exclusiveReadPermissions(own, ['item.read', 'item.read_own', 'blueprint.read'])).toEqual(['item.read', 'blueprint.read']);
    expect(exclusiveReadPermissions(viewing, ['item.read', 'item.create', 'blueprint.read'])).toEqual(['item.read', 'item.create', 'blueprint.read']);
  });
});
