import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { buildChangeSet, detachConflicts, fieldReferences, removedFields } from './settingsChangeSet';
import { fieldStatus, optionStatus, pendingChangeCount, removedStates, stateStatus } from './settingsStatus';
import {
  addField,
  addOption,
  addState,
  draftFromBlueprint,
  moveState,
  removeField,
  removeOption,
  removeState,
  renameField,
  renameOption,
  restoreField,
  restoreState,
  setFieldTypes,
  setMoveTarget,
  updateState,
} from './settingsDraft';

const smk = simpleTasksBlueprint;
const agy = agencyBlueprint;

function statesOf(blueprint: ProjectBlueprint, workflow: string): string[] {
  return blueprint.workflows.find((candidate) => candidate.key === workflow)?.states.map((state) => state.key) ?? [];
}

describe('設定的草稿：狀態', () => {
  it('沒有修改時，送出的藍圖和目前的一樣、搬移與封存都是空的', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const request = buildChangeSet(agy, 4, draft, '  ');

    // Assert
    expect(request).toEqual({ baseVersion: 4, blueprint: agy, migrations: { moveStates: [], dropFields: [] } });
    expect(pendingChangeCount(agy, draft)).toBe(0);
  });

  it('新增狀態放在指定的位置，改名稱與類別不會動到原本的草稿', () => {
    // Arrange
    const draft = draftFromBlueprint(smk);

    // Act
    const added = addState(draft, 'task_flow', { key: 'review', name: '審查中', category: 'in_progress' }, 2);
    const renamed = updateState(added, 'task_flow', 'todo', { name: '待處理' });
    const recategorized = updateState(renamed, 'task_flow', 'review', { category: 'not_started' });

    // Assert
    expect(statesOf(buildChangeSet(smk, 1, draft, '').blueprint, 'task_flow')).toEqual(['todo', 'doing', 'done']);
    const states = buildChangeSet(smk, 1, recategorized, '').blueprint.workflows[0]?.states;
    expect(states).toEqual([
      { key: 'todo', name: '待處理', category: 'not_started' },
      { key: 'doing', name: '進行中', category: 'in_progress' },
      { key: 'review', name: '審查中', category: 'not_started' },
      { key: 'done', name: '完成', category: 'done' },
    ]);
    expect(stateStatus(smk, recategorized, 'task_flow', 'todo')).toBe('modified');
    expect(stateStatus(smk, recategorized, 'task_flow', 'review')).toBe('added');
    expect(stateStatus(smk, recategorized, 'task_flow', 'doing')).toBe('unchanged');
    expect(pendingChangeCount(smk, recategorized)).toBe(2);
  });

  it('上下移動順序；移回原位就不算修改', () => {
    // Arrange
    const draft = draftFromBlueprint(smk);

    // Act
    const moved = moveState(draft, 'task_flow', 'doing', -1);
    const back = moveState(moved, 'task_flow', 'doing', 1);
    const atEdge = moveState(draft, 'task_flow', 'todo', -1);

    // Assert
    expect(statesOf(buildChangeSet(smk, 1, moved, '').blueprint, 'task_flow')).toEqual(['doing', 'todo', 'done']);
    expect(stateStatus(smk, moved, 'task_flow', 'doing')).toBe('moved');
    expect(pendingChangeCount(smk, back)).toBe(0);
    expect(atEdge).toEqual(draft);
  });

  it('刪除狀態要指定搬移目標；有列轉換的流程一起拿掉進出這個狀態的轉換', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const removed = removeState(agy, draft, 'delivery', 'internal_qa');
    const chosen = setMoveTarget(removed, 'delivery', 'internal_qa', 'developing');
    const request = buildChangeSet(agy, 2, chosen, '拿掉內部驗收');

    // Assert
    expect(removedStates(agy, removed, 'delivery')).toEqual([
      { state: { key: 'internal_qa', name: '內部驗收', category: 'in_progress' }, to: '' },
    ]);
    expect(request.summary).toBe('拿掉內部驗收');
    expect(request.migrations).toEqual({
      moveStates: [{ workflow: 'delivery', from: 'internal_qa', to: 'developing' }],
      dropFields: [],
    });
    const delivery = request.blueprint.workflows.find((workflow) => workflow.key === 'delivery');
    expect(delivery?.transitions?.map((transition) => `${transition.from}->${transition.to}`)).toEqual([
      'confirming->quoting',
      'quoting->designing',
      'designing->developing',
      'client_qa->developing',
      'client_qa->closed',
    ]);
    expect(request.blueprint.workflows.find((workflow) => workflow.key === 'task_flow')).toBe(agy.workflows[1]);
  });

  it('沒有列轉換的流程刪除狀態後仍然沒有 transitions 這個屬性', () => {
    // Arrange & Act
    const draft = setMoveTarget(removeState(smk, draftFromBlueprint(smk), 'task_flow', 'doing'), 'task_flow', 'doing', 'todo');
    const workflow = buildChangeSet(smk, 1, draft, '').blueprint.workflows[0];

    // Assert
    expect(workflow).toEqual({ key: 'task_flow', name: '任務流程', states: [smk.workflows[0]?.states[0], smk.workflows[0]?.states[2]] });
    expect(workflow && 'transitions' in workflow).toBe(false);
  });

  it('被當成搬移目標的狀態也被刪除時，目標要重新選', () => {
    // Arrange
    const draft = setMoveTarget(removeState(smk, draftFromBlueprint(smk), 'task_flow', 'doing'), 'task_flow', 'doing', 'todo');

    // Act
    const next = removeState(smk, draft, 'task_flow', 'todo');

    // Assert
    expect(removedStates(smk, next, 'task_flow').map((entry) => [entry.state.key, entry.to])).toEqual([
      ['todo', ''],
      ['doing', ''],
    ]);
  });

  it('剛新增的狀態刪掉就沒了，不必搬移', () => {
    // Arrange
    const draft = addState(draftFromBlueprint(smk), 'task_flow', { key: 'review', name: '審查中', category: 'in_progress' }, 3);

    // Act
    const next = removeState(smk, draft, 'task_flow', 'review');

    // Assert
    expect(next).toEqual(draftFromBlueprint(smk));
  });

  it('復原刪除的狀態：回到原本的位置，轉換也回來', () => {
    // Arrange
    const draft = removeState(agy, draftFromBlueprint(agy), 'delivery', 'internal_qa');

    // Act
    const restored = restoreState(agy, draft, 'delivery', 'internal_qa');

    // Assert
    expect(buildChangeSet(agy, 1, restored, '').blueprint).toEqual(agy);
    expect(pendingChangeCount(agy, restored)).toBe(0);
  });
});

describe('設定的草稿：欄位', () => {
  it('新增欄位寫進類型的 fields；非選項型不帶 options、非金額不帶 currency', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const next = addField(draft, { key: 'site_url', name: '網站', type: 'url', appliesTo: ['task', 'requirement'] });
    const request = buildChangeSet(agy, 1, next, '');

    // Assert
    expect(request.blueprint.fields.at(-1)).toEqual({ key: 'site_url', name: '網站', type: 'url' });
    expect(request.blueprint.itemTypes.map((type) => [type.key, type.fields])).toEqual([
      ['requirement', ['quote_amount', 'client_signoff_date', 'site_url']],
      ['change_request', ['change_reason', 'impact', 'quote_amount', 'client_signoff_date']],
      ['task', ['site_url']],
    ]);
    expect(fieldStatus(agy, next, 'site_url')).toBe('added');
  });

  it('選項型的欄位帶選項，金額欄位帶幣別', () => {
    // Arrange
    const draft = draftFromBlueprint(smk);

    // Act
    const next = addField(
      addField(draft, {
        key: 'severity',
        name: '嚴重度',
        type: 'single_select',
        options: [{ key: 'minor', name: '輕微' }],
        appliesTo: [],
      }),
      { key: 'cost', name: '成本', type: 'currency', currency: 'USD', appliesTo: [] },
    );

    // Assert
    expect(buildChangeSet(smk, 1, next, '').blueprint.fields.slice(-2)).toEqual([
      { key: 'severity', name: '嚴重度', type: 'single_select', options: [{ key: 'minor', name: '輕微' }] },
      { key: 'cost', name: '成本', type: 'currency', currency: 'USD' },
    ]);
  });

  it('改名、改套用的類型、新增與改名選項；新選項可以拿掉', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const renamed = renameField(draft, 'impact', '影響');
    const retyped = setFieldTypes(renamed, 'quote_amount', ['change_request']);
    const withOption = addOption(retyped, 'impact', { key: 'critical', name: '嚴重' });
    const optionRenamed = renameOption(withOption, 'impact', 'low', '輕');
    const request = buildChangeSet(agy, 1, optionRenamed, '');

    // Assert
    expect(request.blueprint.fields.find((field) => field.key === 'impact')).toEqual({
      key: 'impact',
      name: '影響',
      type: 'single_select',
      options: [
        { key: 'low', name: '輕', color: 'gray' },
        { key: 'medium', name: '中', color: 'yellow' },
        { key: 'high', name: '高', color: 'red' },
        { key: 'critical', name: '嚴重' },
      ],
    });
    expect(request.blueprint.itemTypes.map((type) => type.fields)).toEqual([
      ['client_signoff_date'],
      ['change_reason', 'impact', 'quote_amount', 'client_signoff_date'],
      [],
    ]);
    expect(fieldStatus(agy, optionRenamed, 'impact')).toBe('modified');
    expect(fieldStatus(agy, optionRenamed, 'quote_amount')).toBe('modified');
    expect(fieldStatus(agy, optionRenamed, 'client_signoff_date')).toBe('unchanged');
    expect(removeOption(withOption, 'impact', 'critical')).toEqual(retyped);
  });

  it('刪除欄位：寫進 dropFields，也從類型、轉換的必填欄位與檢視拿掉', () => {
    // Arrange
    const withViews: ProjectBlueprint = {
      ...agy,
      views: [
        { key: 'board', name: '看板', type: 'board', groupBy: 'impact' },
        {
          key: 'table',
          name: '需求清單',
          type: 'table',
          columns: ['title', 'client_signoff_date', 'impact'],
          sort: [{ field: 'client_signoff_date', direction: 'asc' }],
        },
        { key: 'plain', name: '全部', type: 'table', columns: ['title'] },
      ],
    };
    const draft = draftFromBlueprint(withViews);

    // Act
    const next = removeField(withViews, removeField(withViews, draft, 'client_signoff_date'), 'impact');
    const request = buildChangeSet(withViews, 1, next, '');

    // Assert
    expect(removedFields(withViews, next).map((field) => field.key)).toEqual(['client_signoff_date', 'impact']);
    expect(request.migrations?.dropFields).toEqual(['client_signoff_date', 'impact']);
    expect(request.blueprint.fields.map((field) => field.key)).toEqual(['quote_amount', 'change_reason']);
    expect(request.blueprint.itemTypes.map((type) => type.fields)).toEqual([['quote_amount'], ['change_reason', 'quote_amount'], []]);
    const closing = request.blueprint.workflows[0]?.transitions?.at(-1);
    expect(closing).toEqual({ from: 'client_qa', to: 'closed', allowedRoles: ['project_manager'], requireFields: [] });
    expect(request.blueprint.views).toEqual([
      { key: 'board', name: '看板', type: 'board' },
      { key: 'table', name: '需求清單', type: 'table', columns: ['title'], sort: [] },
      withViews.views?.[2],
    ]);
  });

  it('刪除欄位時會一起拿掉的引用：轉換的必填欄位與用到它的檢視', () => {
    // Arrange
    const withViews: ProjectBlueprint = {
      ...agy,
      views: [
        { key: 'table', name: '需求清單', type: 'table', sort: [{ field: 'client_signoff_date', direction: 'asc' }] },
        { key: 'board', name: '看板', type: 'board', groupBy: 'state' },
      ],
    };

    // Act & Assert
    const draft = draftFromBlueprint(withViews);
    expect(fieldReferences(withViews, draft, 'client_signoff_date')).toEqual(['轉換的必填欄位', '檢視「需求清單」']);
    expect(fieldReferences(withViews, draft, 'impact')).toEqual([]);
  });

  it('檢視篩選的狀態被刪除時換成搬移的目標（去重），還在別的流程裡的留著', () => {
    // Arrange
    const withFilter: ProjectBlueprint = {
      ...agy,
      views: [
        { key: 'open', name: '進行中', type: 'table', filter: { states: ['doing', 'developing'] } },
        { key: 'only', name: '只有進行中', type: 'table', filter: { states: ['doing'] } },
        { key: 'both', name: '待辦與進行中', type: 'board', filter: { states: ['todo', 'doing'] } },
        { key: 'qa', name: '驗收', type: 'table', filter: { states: ['internal_qa', 'done'] } },
      ],
    };
    const removed = removeState(withFilter, draftFromBlueprint(withFilter), 'task_flow', 'doing');
    const draft = setMoveTarget(removed, 'task_flow', 'doing', 'todo');

    // Act
    const views = buildChangeSet(withFilter, 1, draft, '').blueprint.views;

    // Assert
    expect(views?.map((view) => view.filter?.states)).toEqual([['todo', 'developing'], ['todo'], ['todo'], ['internal_qa', 'done']]);
    expect(views?.[3]).toBe(withFilter.views?.[3]);
  });

  it('復原刪除的欄位：回到原本的位置與類型', () => {
    // Arrange
    const draft = removeField(agy, draftFromBlueprint(agy), 'client_signoff_date');

    // Act
    const restored = restoreField(agy, draft, 'client_signoff_date');

    // Assert
    expect(buildChangeSet(agy, 1, restored, '').blueprint).toEqual(agy);
  });

  it('剛新增的欄位刪掉就沒了，不必封存', () => {
    // Arrange
    const draft = addField(draftFromBlueprint(smk), { key: 'extra', name: '額外', type: 'text', appliesTo: ['task'] });

    // Act
    const next = removeField(smk, draft, 'extra');

    // Assert
    expect(next).toEqual(draftFromBlueprint(smk));
    expect(removedFields(smk, next)).toEqual([]);
  });
});

describe('設定的草稿：名稱的前後空白', () => {
  it('送出時去掉名稱前後的空白：狀態、欄位、選項', () => {
    // Arrange
    const renamedState = updateState(draftFromBlueprint(smk), 'task_flow', 'todo', { name: '  待處理  ' });
    const renamedField = renameField(renamedState, 'category', ' 類別 ');
    const draft = addOption(renameOption(renamedField, 'category', 'bug', '\t缺陷 '), 'category', { key: 'idea', name: ' 點子' });

    // Act
    const { blueprint } = buildChangeSet(smk, 1, draft, '');

    // Assert
    expect(blueprint.workflows[0]?.states[0]).toEqual({ key: 'todo', name: '待處理', category: 'not_started' });
    const category = blueprint.fields.find((field) => field.key === 'category');
    expect(category?.name).toBe('類別');
    expect(category?.options?.map((option) => option.name)).toEqual(['新功能', '缺陷', '雜務', '點子']);
  });

  it('只差前後空白不算修改：沒有標記、不計數，送出的物件和原本的一樣', () => {
    // Arrange
    const spacedState = updateState(draftFromBlueprint(smk), 'task_flow', 'todo', { name: '待辦  ' });
    const draft = renameOption(renameField(spacedState, 'category', ' 分類'), 'category', 'bug', '錯誤 ');

    // Act
    const request = buildChangeSet(smk, 1, draft, '');

    // Assert
    expect(stateStatus(smk, draft, 'task_flow', 'todo')).toBe('unchanged');
    expect(fieldStatus(smk, draft, 'category')).toBe('unchanged');
    expect(optionStatus(smk, 'category', 'bug', '錯誤 ')).toBe('unchanged');
    expect(pendingChangeCount(smk, draft)).toBe(0);
    expect(request.blueprint).toEqual(smk);
    expect(request.blueprint.workflows[0]).toBe(smk.workflows[0]);
  });
});

describe('設定的草稿：取消套用到類型', () => {
  it('類型用的流程有轉換要求先填這個欄位時，列出這些類型與流程', () => {
    // Arrange
    const draft = setFieldTypes(draftFromBlueprint(agy), 'client_signoff_date', ['requirement']);
    const field = draft.fields.find((candidate) => candidate.key === 'client_signoff_date');
    const quote = draft.fields.find((candidate) => candidate.key === 'quote_amount');
    if (field === undefined || quote === undefined) throw new Error('找不到欄位。');

    // Act & Assert
    expect(detachConflicts(draft, field)).toEqual([{ itemType: 'change_request', workflow: 'delivery' }]);
    expect(detachConflicts(draft, { ...field, appliesTo: [] })).toEqual([
      { itemType: 'requirement', workflow: 'delivery' },
      { itemType: 'change_request', workflow: 'delivery' },
    ]);
    expect(detachConflicts(draft, { ...quote, appliesTo: [] })).toEqual([]);
  });
});
