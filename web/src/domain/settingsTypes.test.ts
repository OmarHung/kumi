import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { agencyBlueprint } from '../test/blueprints';
import { buildChangeSet } from './settingsChangeSet';
import { addField, addState, draftFromBlueprint, removeState, setMoveTarget } from './settingsDraft';
import { pendingChangeCount } from './settingsStatus';
import {
  addItemType,
  coveredTypeMoves,
  fieldsRequiredByWorkflow,
  itemTypeStatus,
  requiredTypeMoves,
  setTypeMoveTarget,
  updateItemType,
} from './settingsTypes';
import { validateDraft } from './settingsValidation';

const agy = agencyBlueprint;

/** 任務改用交付流程、需求改用任務流程時要搬的狀態，寫成「流程/舊狀態→新狀態（類型…）」。 */
function movesText(base: ProjectBlueprint, draft: ReturnType<typeof draftFromBlueprint>): string[] {
  return requiredTypeMoves(base, draft).map((move) => `${move.workflow}/${move.from.key}→${move.to}（${move.itemTypes.join('、')}）`);
}

describe('設定的草稿：類型', () => {
  it('改名、換流程、改上層：只送改了的屬性，其他屬性原樣保留；改回原樣不算修改', () => {
    // Arrange
    const withIcon: ProjectBlueprint = { ...agy, itemTypes: agy.itemTypes.map((type) => (type.key === 'task' ? { ...type, icon: 'check' } : type)) };
    const draft = draftFromBlueprint(withIcon);

    // Act
    const renamed = updateItemType(draft, 'task', { name: ' 工作 ', parents: ['change_request', 'requirement'] });
    const reverted = updateItemType(renamed, 'task', { name: '任務' });

    // Assert
    expect(itemTypeStatus(withIcon, updateItemType(draft, 'task', { parents: ['change_request', 'requirement'] }), 'task')).toBe('unchanged');
    expect(itemTypeStatus(withIcon, renamed, 'task')).toBe('modified');
    const task = buildChangeSet(withIcon, 1, renamed, '').blueprint.itemTypes[2];
    expect(task).toEqual({ key: 'task', name: '工作', workflow: 'task_flow', fields: [], parents: ['requirement', 'change_request'], icon: 'check' });
    expect(buildChangeSet(withIcon, 1, reverted, '').blueprint.itemTypes[2]).toBe(withIcon.itemTypes[2]);
    expect(pendingChangeCount(withIcon, reverted)).toBe(0);
  });

  it('上層清空時送空陣列', () => {
    // Arrange & Act
    const draft = updateItemType(draftFromBlueprint(agy), 'task', { parents: [] });

    // Assert
    expect(buildChangeSet(agy, 1, draft, '').blueprint.itemTypes[2]?.parents).toEqual([]);
    expect(pendingChangeCount(agy, draft)).toBe(1);
  });

  it('新增類型：接在最後，fields 照欄位的順序，形狀和 API 一樣', () => {
    // Arrange
    const draft = addField(draftFromBlueprint(agy), { key: 'severity', name: '嚴重度', type: 'text', appliesTo: [] });

    // Act
    const next = addItemType(draft, { key: 'bug', name: ' 錯誤 ', workflow: 'delivery', parents: ['requirement'] }, ['severity', 'client_signoff_date']);

    // Assert
    expect(buildChangeSet(agy, 1, next, '').blueprint.itemTypes.at(-1)).toEqual({
      key: 'bug',
      name: '錯誤',
      workflow: 'delivery',
      fields: ['client_signoff_date', 'severity'],
      parents: ['requirement'],
    });
    expect(buildChangeSet(agy, 1, next, '').blueprint.itemTypes.slice(0, 3)).toEqual(agy.itemTypes);
    expect(itemTypeStatus(agy, next, 'bug')).toBe('added');
    expect(pendingChangeCount(agy, next)).toBe(2);
  });

  it('新類型預設套用的欄位：選定流程的轉換要求先填的自訂欄位', () => {
    // Arrange & Act & Assert
    expect(fieldsRequiredByWorkflow(draftFromBlueprint(agy), 'delivery')).toEqual(['client_signoff_date']);
    expect(fieldsRequiredByWorkflow(draftFromBlueprint(agy), 'task_flow')).toEqual([]);
  });
});

describe('設定的草稿：類型換流程的搬移', () => {
  it('舊流程有、新流程沒有的狀態要搬，預設是新流程第一個同類別的狀態，沒有就第一個狀態', () => {
    // Arrange & Act
    const draft = updateItemType(draftFromBlueprint(agy), 'requirement', { workflow: 'task_flow' });

    // Assert
    expect(movesText(agy, draft)).toEqual([
      'task_flow/confirming→todo（requirement）',
      'task_flow/quoting→todo（requirement）',
      'task_flow/designing→doing（requirement）',
      'task_flow/developing→doing（requirement）',
      'task_flow/internal_qa→doing（requirement）',
      'task_flow/client_qa→doing（requirement）',
      'task_flow/closed→done（requirement）',
    ]);
    expect(buildChangeSet(agy, 1, draft, '').migrations?.moveStates).toHaveLength(7);
    expect(buildChangeSet(agy, 1, draft, '').migrations?.moveStates?.[0]).toEqual({ workflow: 'task_flow', from: 'confirming', to: 'todo' });
  });

  it('兩個類型換到同一個流程時共用搬移：改一個另一個跟著變，只送一條', () => {
    // Arrange
    const both = updateItemType(updateItemType(draftFromBlueprint(agy), 'requirement', { workflow: 'task_flow' }), 'change_request', { workflow: 'task_flow' });

    // Act
    const chosen = setTypeMoveTarget(both, 'task_flow', 'closed', 'doing');

    // Assert
    const closed = requiredTypeMoves(agy, chosen).find((move) => move.from.key === 'closed');
    expect(closed).toEqual({ workflow: 'task_flow', from: agy.workflows[0]?.states[6], to: 'doing', itemTypes: ['requirement', 'change_request'] });
    const moves = buildChangeSet(agy, 1, chosen, '').migrations?.moveStates ?? [];
    expect(moves.filter((move) => move.from === 'closed')).toEqual([{ workflow: 'task_flow', from: 'closed', to: 'doing' }]);
    expect(pendingChangeCount(agy, chosen)).toBe(2);
  });

  it('換回原本的流程就不用搬，留下的選擇不送', () => {
    // Arrange
    const moved = setTypeMoveTarget(updateItemType(draftFromBlueprint(agy), 'task', { workflow: 'delivery' }), 'delivery', 'done', 'client_qa');

    // Act
    const back = updateItemType(moved, 'task', { workflow: 'task_flow' });

    // Assert
    expect(movesText(agy, moved)).toEqual(['delivery/todo→confirming（task）', 'delivery/doing→designing（task）', 'delivery/done→client_qa（task）']);
    expect(requiredTypeMoves(agy, back)).toEqual([]);
    expect(buildChangeSet(agy, 1, back, '').migrations).toEqual({ moveStates: [], dropFields: [] });
    expect(pendingChangeCount(agy, back)).toBe(0);
  });

  it('新流程有同 key 的狀態不用搬；在新流程被刪除的狀態由刪除的搬移處理，不重複送', () => {
    // Arrange
    const [delivery, taskFlow] = agy.workflows;
    if (delivery === undefined || taskFlow === undefined) throw new Error('沒有流程。');
    const developing = { key: 'developing', name: '開發中', category: 'in_progress' } as const;
    const base: ProjectBlueprint = { ...agy, workflows: [delivery, { ...taskFlow, states: [...taskFlow.states.slice(0, 2), developing, ...taskFlow.states.slice(2)] }] };
    const added = addState(draftFromBlueprint(base), 'task_flow', { key: 'closed', name: '結案', category: 'done' }, 4);
    const withDeleted = setMoveTarget(removeState(base, added, 'task_flow', 'developing'), 'task_flow', 'developing', 'doing');

    // Act
    const draft = updateItemType(withDeleted, 'requirement', { workflow: 'task_flow' });

    // Assert
    expect(requiredTypeMoves(base, draft).map((move) => move.from.key)).toEqual(['confirming', 'quoting', 'designing', 'internal_qa', 'client_qa']);
    const moves = buildChangeSet(base, 1, draft, '').migrations?.moveStates ?? [];
    expect(moves[0]).toEqual({ workflow: 'task_flow', from: 'developing', to: 'doing' });
    expect(moves.filter((move) => move.from === 'developing')).toHaveLength(1);
    expect(moves).toHaveLength(6);
  });

  it('在新流程被刪除的狀態：類型那一列看得到它照「刪除狀態」的搬移搬到哪裡', () => {
    // Arrange
    const [delivery, taskFlow] = agy.workflows;
    if (delivery === undefined || taskFlow === undefined) throw new Error('沒有流程。');
    const developing = { key: 'developing', name: '開發中', category: 'in_progress' } as const;
    const base: ProjectBlueprint = { ...agy, workflows: [delivery, { ...taskFlow, states: [...taskFlow.states.slice(0, 2), developing, ...taskFlow.states.slice(2)] }] };
    const removed = removeState(base, draftFromBlueprint(base), 'task_flow', 'developing');

    // Act
    const draft = updateItemType(removed, 'requirement', { workflow: 'task_flow' });
    const chosen = setMoveTarget(draft, 'task_flow', 'developing', 'doing');

    // Assert
    expect(coveredTypeMoves(base, draft, 'requirement')).toEqual([{ workflow: 'task_flow', from: delivery.states[3], deleted: developing, to: '' }]);
    expect(coveredTypeMoves(base, chosen, 'requirement')).toEqual([{ workflow: 'task_flow', from: delivery.states[3], deleted: developing, to: 'doing' }]);
    expect(coveredTypeMoves(base, chosen, 'change_request')).toEqual([]);
  });

  it('兩個類型共用一條搬移、舊狀態在兩邊的類別不同：不預選，要使用者自己選', () => {
    // Arrange
    const flow = (key: string, name: string, open: 'not_started' | 'in_progress') => ({
      key,
      name,
      states: [
        { key: 'open', name: open === 'not_started' ? '開放' : '處理中', category: open },
        { key: 'end', name: '結束', category: 'done' },
      ],
    }) as const;
    const target = {
      key: 'gamma',
      name: '新流程',
      states: [
        { key: 'new', name: '新的', category: 'not_started' },
        { key: 'working', name: '進行中', category: 'in_progress' },
        { key: 'finish', name: '完成', category: 'done' },
      ],
    } as const;
    const base: ProjectBlueprint = {
      ...agy,
      workflows: [flow('alpha', '甲流程', 'not_started'), flow('beta', '乙流程', 'in_progress'), target],
      itemTypes: [
        { key: 'first', name: '第一種', workflow: 'alpha', fields: [], parents: [] },
        { key: 'second', name: '第二種', workflow: 'beta', fields: [], parents: [] },
      ],
      fields: [],
    };

    // Act
    const draft = updateItemType(updateItemType(draftFromBlueprint(base), 'first', { workflow: 'gamma' }), 'second', { workflow: 'gamma' });
    const chosen = setTypeMoveTarget(draft, 'gamma', 'open', 'working');

    // Assert
    expect(movesText(base, draft)).toEqual(['gamma/open→（first、second）', 'gamma/end→finish（first、second）']);
    expect(requiredTypeMoves(base, draft)[0]?.from.name).toBe('開放');
    expect(validateDraft(base, draft)).toEqual([
      { target: { kind: 'typeMove', workflow: 'gamma', from: 'open' }, message: '請選擇「第一種」停在「開放」的項目要搬到哪個狀態。' },
    ]);
    expect(movesText(base, chosen)[0]).toBe('gamma/open→working（first、second）');
    expect(validateDraft(base, chosen)).toEqual([]);
  });

  it('選過的目標後來被刪掉就回到預設', () => {
    // Arrange
    const chosen = setTypeMoveTarget(updateItemType(draftFromBlueprint(agy), 'task', { workflow: 'delivery' }), 'delivery', 'done', 'client_qa');

    // Act
    const draft = removeState(agy, chosen, 'delivery', 'client_qa');

    // Assert
    expect(requiredTypeMoves(agy, draft).find((move) => move.from.key === 'done')?.to).toBe('closed');
  });
});
