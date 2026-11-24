import { describe, expect, it } from 'vitest';
import { agencyBlueprint } from '../../test/blueprints';
import { addState, draftFromBlueprint, removeField, renameField, updateState, type SettingsDraft } from '../../domain/settingsDraft';
import type { SettingsIssue } from '../../domain/settingsIssues';
import { removeRole } from '../../domain/settingsRoles';
import { pendingChangeCount } from '../../domain/settingsStatus';
import { setTransition } from '../../domain/settingsTransitions';
import { updateItemType } from '../../domain/settingsTypes';
import { issueBreakdown, issueSection, sectionChangeCounts, sectionIssueCounts } from './settingsSections';

const base = agencyBlueprint;

function edited(...changes: readonly ((draft: SettingsDraft) => SettingsDraft)[]): SettingsDraft {
  return changes.reduce((draft, change) => change(draft), draftFromBlueprint(base));
}

describe('每個區塊還沒送出的修改', () => {
  it('沒有修改時每個區塊都是 0', () => {
    // Act
    const counts = sectionChangeCounts(base, draftFromBlueprint(base));

    // Assert
    expect(counts).toEqual({ workflows: 0, types: 0, fields: 0, roles: 0 });
  });

  it('狀態與轉換算在流程，類型、欄位、角色各算各的；加起來就是送出列的項數', () => {
    // Arrange
    const draft = edited(
      (current) => addState(current, 'delivery', { key: 'review', name: '審查', category: 'in_progress' }, 2),
      (current) => updateState(current, 'task_flow', 'todo', { name: '待處理' }),
      (current) => setTransition(base, current, 'delivery', 'confirming', 'closed', true),
      (current) => updateItemType(current, 'task', { name: '工作' }),
      (current) => renameField(current, 'impact', '影響'),
      (current) => removeField(base, current, 'change_reason'),
      (current) => removeRole(base, current, 'client'),
    );

    // Act
    const counts = sectionChangeCounts(base, draft);

    // Assert
    expect(counts).toEqual({ workflows: 3, types: 1, fields: 2, roles: 1 });
    expect(counts.workflows + counts.types + counts.fields + counts.roles).toBe(pendingChangeCount(base, draft));
  });
});

describe('問題落在哪個區塊', () => {
  it.each([
    [{ kind: 'workflow', workflow: 'delivery' }, 'workflows'],
    [{ kind: 'state', workflow: 'delivery', state: 'quoting' }, 'workflows'],
    [{ kind: 'transition', workflow: 'delivery', from: 'quoting', to: 'closed' }, 'workflows'],
    [{ kind: 'itemType', itemType: 'task' }, 'types'],
    [{ kind: 'typeMove', workflow: 'task_flow', from: 'quoting' }, 'types'],
    [{ kind: 'field', field: 'impact' }, 'fields'],
    [{ kind: 'option', field: 'impact', option: 'low' }, 'fields'],
    [{ kind: 'role', role: 'client' }, 'roles'],
  ] as const)('%o 在 %s', (target, section) => {
    // Act & Assert
    expect(issueSection(target)).toBe(section);
  });

  it('數每個區塊的問題；對不到任何一列的（送出列的「其他問題」）不算在分頁上', () => {
    // Arrange
    const issues: readonly SettingsIssue[] = [
      { target: { kind: 'state', workflow: 'delivery', state: 'quoting' }, message: 'a' },
      { target: { kind: 'transition', workflow: 'delivery', from: 'quoting', to: 'closed' }, message: 'b' },
      { target: { kind: 'option', field: 'impact', option: 'low' }, message: 'c' },
      { target: null, message: 'd' },
    ];

    // Act & Assert
    expect(sectionIssueCounts(issues)).toEqual({ workflows: 2, types: 0, fields: 1, roles: 0 });
  });

  it('說明裡的分布：照分頁的順序，再接摘要與其他問題；是 0 的不說', () => {
    // Arrange
    const issues: readonly SettingsIssue[] = [
      { target: { kind: 'role', role: 'client' }, message: 'a' },
      { target: { kind: 'state', workflow: 'delivery', state: 'quoting' }, message: 'b' },
      { target: { kind: 'transition', workflow: 'delivery', from: 'quoting', to: 'closed' }, message: 'c' },
      { target: null, message: 'd' },
    ];

    // Act & Assert
    expect(issueBreakdown(issues, true)).toBe('流程 2 個、角色 1 個、摘要 1 個、其他問題 1 個');
    expect(issueBreakdown([], true)).toBe('摘要 1 個');
  });
});
