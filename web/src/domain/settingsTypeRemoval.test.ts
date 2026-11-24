import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { agencyBlueprint, agencyFullBlueprint } from '../test/blueprints';
import { buildChangeSet } from './settingsChangeSet';
import { itemTypeKeyError, requiredFieldIssues } from './settingsChecks';
import { draftFromBlueprint, type SettingsDraft } from './settingsDraft';
import { itemTypeAutomationReminder } from './settingsAutomations';
import { locateIssue } from './settingsIssues';
import { pendingChangeCount } from './settingsStatus';
import { itemTypeReferences, removedItemTypes, removeItemType, restoreItemType } from './settingsTypeRemoval';
import { addItemType, requiredTypeMoves, updateItemType } from './settingsTypes';
import { validateDraft } from './settingsValidation';

const agy = agencyFullBlueprint;
const [, changeRequest, task] = agencyBlueprint.itemTypes;

function removeAll(base: ProjectBlueprint, keys: readonly string[]): SettingsDraft {
  return keys.reduce((draft, key) => removeItemType(base, draft, key), draftFromBlueprint(base));
}

describe('設定的草稿：刪除類型', () => {
  it('刪除既有的類型：送出時拿掉它，也從其他類型的上層與檢視的類型篩選拿掉；算一項修改', () => {
    // Arrange & Act
    const draft = removeItemType(agy, draftFromBlueprint(agy), 'requirement');

    // Assert
    expect(removedItemTypes(agy, draft).map((type) => type.key)).toEqual(['requirement']);
    const body = buildChangeSet(agy, 1, draft, '');
    expect(body.blueprint.itemTypes).toEqual([changeRequest, { ...task, parents: ['change_request'] }]);
    expect(body.blueprint.itemTypes[0]).toBe(agy.itemTypes[1]);
    expect(body.blueprint.views?.[1]?.filter).toEqual({ itemTypes: ['change_request'], states: [], stateCategories: [] });
    expect(body.blueprint.views?.[0]).toBe(agy.views?.[0]);
    expect(body.blueprint.fields).toEqual(agy.fields);
    expect(body.blueprint.automations).toBe(agy.automations);
    expect(body.migrations).toEqual({ moveStates: [], dropFields: [] });
    expect(pendingChangeCount(agy, draft)).toBe(1);
  });

  it('復原：回到原本的位置，引用都回來，送出的和原本的一樣', () => {
    // Arrange
    const removed = removeItemType(agy, draftFromBlueprint(agy), 'change_request');

    // Act
    const restored = restoreItemType(agy, removed, 'change_request');

    // Assert
    expect(restored.itemTypes.map((type) => type.key)).toEqual(['requirement', 'change_request', 'task']);
    const body = buildChangeSet(agy, 1, restored, '');
    body.blueprint.itemTypes.forEach((type, index) => {
      expect(type).toBe(agy.itemTypes[index]);
    });
    expect(body.blueprint.views).toEqual(agy.views);
    expect(pendingChangeCount(agy, restored)).toBe(0);
  });

  it('剛新增、還沒送出的類型刪掉就直接不見，連其他類型的上層與欄位的套用一起拿掉', () => {
    // Arrange
    const added = addItemType(draftFromBlueprint(agy), { key: 'bug', name: '錯誤', workflow: 'task_flow', parents: [] }, ['impact']);
    const withParent = updateItemType(added, 'task', { parents: ['requirement', 'change_request', 'bug'] });

    // Act
    const draft = removeItemType(agy, withParent, 'bug');

    // Assert
    expect(draft.itemTypes.find((type) => type.key === 'task')?.parents).toEqual(['requirement', 'change_request']);
    expect(draft.fields.find((field) => field.key === 'impact')?.appliesTo).toEqual(['change_request']);
    expect(removedItemTypes(agy, draft)).toEqual([]);
    expect(pendingChangeCount(agy, draft)).toBe(0);
  });

  it('刪除的類型 key 也算用過', () => {
    // Arrange
    const draft = removeItemType(agy, draftFromBlueprint(agy), 'task');

    // Act & Assert
    expect(itemTypeKeyError(agy, draft, 'task')).toBe('已經有 key 是 task 的類型。');
  });

  it('會一起拿掉的引用；拿掉之後清單變空的另外標出來（不能有上層、檢視不再篩選類型）', () => {
    // Arrange
    const onlyRequirement = removeItemType(agy, draftFromBlueprint(agy), 'requirement');
    const both = removeAll(agy, ['requirement', 'change_request']);

    // Act & Assert
    expect(itemTypeReferences(agy, onlyRequirement, 'requirement')).toEqual([
      { kind: 'parent', itemType: 'task', becomesEmpty: false },
      { kind: 'view', view: 'client_view', becomesEmpty: false },
    ]);
    expect(itemTypeReferences(agy, both, 'change_request')).toEqual([
      { kind: 'parent', itemType: 'task', becomesEmpty: true },
      { kind: 'view', view: 'client_view', becomesEmpty: true },
    ]);
    const body = buildChangeSet(agy, 1, both, '');
    expect(body.blueprint.itemTypes).toEqual([{ ...task, parents: [] }]);
    expect(body.blueprint.views?.[1]?.filter?.itemTypes).toEqual([]);
  });

  it('換了流程又刪掉的類型不再要搬移；和它共用搬移的類型照舊', () => {
    // Arrange
    const switched = updateItemType(updateItemType(draftFromBlueprint(agy), 'requirement', { workflow: 'task_flow' }), 'change_request', {
      workflow: 'task_flow',
    });

    // Act
    const draft = removeItemType(agy, switched, 'requirement');
    const onlyRemoved = removeItemType(agy, updateItemType(draftFromBlueprint(agy), 'requirement', { workflow: 'task_flow' }), 'requirement');

    // Assert
    expect(requiredTypeMoves(agy, draft).every((move) => move.itemTypes.join() === 'change_request')).toBe(true);
    expect(buildChangeSet(agy, 1, draft, '').migrations?.moveStates).toHaveLength(7);
    expect(buildChangeSet(agy, 1, onlyRemoved, '').migrations?.moveStates).toEqual([]);
    // 復原回到目前藍圖裡的樣子（原本的流程），換流程的修改不會跟著回來。
    expect(restoreItemType(agy, onlyRemoved, 'requirement').itemTypes[0]).toEqual({ key: 'requirement', name: '需求', workflow: 'delivery', parents: [] });
  });

  it('自動化的條件用到這個類型時提醒（不擋送出，伺服器會擋）', () => {
    // Arrange
    const base: ProjectBlueprint = {
      ...agy,
      automations: [
        ...(agy.automations ?? []),
        {
          key: 'close_requirements',
          name: '需求合併後結案',
          trigger: { source: 'item', event: 'item.updated' },
          conditions: [{ field: 'item_type', in: ['requirement', 'change_request'] }],
          actions: [{ type: 'transition', to: 'closed' }],
        },
      ],
    };

    // Act & Assert
    expect(itemTypeAutomationReminder(base, 'requirement')).toBe(
      '自動化「需求合併後結案」用到這個類型：刪除之後，送出會被伺服器擋下。自動化目前只能用變更集文件修改。',
    );
    expect(itemTypeAutomationReminder(base, 'task')).toBe('');
  });

  it('伺服器說自動化用到不存在的類型：標在那個要刪除的類型', () => {
    // Arrange
    const base: ProjectBlueprint = {
      ...agy,
      automations: [
        {
          key: 'close_requirements',
          name: '需求合併後結案',
          trigger: { source: 'item', event: 'item.updated' },
          conditions: [
            { field: 'state', in: ['developing'] },
            { field: 'item_type', in: ['task', 'requirement'] },
          ],
          actions: [{ type: 'transition', to: 'closed' }],
        },
      ],
    };
    const draft = removeItemType(base, draftFromBlueprint(base), 'requirement');
    const request = buildChangeSet(base, 1, draft, '');

    // Act & Assert
    expect(locateIssue('/blueprint/automations/0/conditions/1/in/1', request, { base, draft })).toEqual({ kind: 'itemType', itemType: 'requirement' });
    expect(locateIssue('/blueprint/automations/0/conditions/0/in/0', request, { base, draft })).toBeNull();
    expect(locateIssue('/blueprint/views/1/filter/itemTypes/0', request, { base, draft })).toEqual({ kind: 'itemType', itemType: 'change_request' });
    expect(locateIssue('/blueprint/views/1/filter/states/0', request, { base, draft })).toBeNull();
  });

  it('至少要留一個類型', () => {
    // Arrange
    const draft = removeAll(agencyBlueprint, ['requirement', 'change_request', 'task']);

    // Act
    const issues = validateDraft(agencyBlueprint, draft);

    // Assert
    expect(issues).toContainEqual({ target: null, message: '至少要留一個類型。' });
  });

  it('轉換要求先填上層，而類型的上層只剩要刪除的：送出前擋下', () => {
    // Arrange
    const base: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: agencyBlueprint.workflows.map((workflow) =>
        workflow.key === 'task_flow'
          ? {
              ...workflow,
              transitions: [
                { from: 'todo', to: 'doing', allowedRoles: [], requireFields: ['parent'] },
                { from: 'doing', to: 'done', allowedRoles: [], requireFields: [] },
              ],
            }
          : workflow,
      ),
    };
    const draft = removeAll(base, ['requirement', 'change_request']);
    const workflow = draft.workflows.find((candidate) => candidate.key === 'task_flow');
    if (workflow === undefined) throw new Error('沒有任務流程。');

    // Act
    const issues = requiredFieldIssues(base, draft, workflow);

    // Assert
    expect(issues.map((issue) => issue.message)).toEqual([
      '「任務」不能掛在任何類型底下：這條轉換要求先填「上層項目」，「任務」永遠走不過去。',
    ]);
  });
});
