import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint, Workflow } from '../api/blueprint';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { buildChangeSet } from './settingsChangeSet';
import { addField, addState, draftFromBlueprint, removeField, removeState, restoreState, setMoveTarget } from './settingsDraft';
import { addRole, removeRole } from './settingsRoles';
import { pendingChangeCount } from './settingsStatus';
import {
  addStateTransitions,
  allPairs,
  setRestricted,
  setTransition,
  transitionStatus,
  updateTransition,
} from './settingsTransitions';

const smk = simpleTasksBlueprint;
const agy = agencyBlueprint;

function workflowOf(blueprint: ProjectBlueprint, key: string): Workflow {
  const workflow = blueprint.workflows.find((candidate) => candidate.key === key);
  if (workflow === undefined) throw new Error(`沒有流程 ${key}。`);
  return workflow;
}

function pairs(workflow: Workflow): string[] {
  return (workflow.transitions ?? []).map((transition) => `${transition.from}->${transition.to}`);
}

describe('設定的草稿：轉換', () => {
  it('取消一條轉換再勾回來：回到原本的位置與限制，不算修改，送出的是原本的物件', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const unchecked = setTransition(agy, draft, 'delivery', 'client_qa', 'closed', false);
    const rechecked = setTransition(agy, unchecked, 'delivery', 'client_qa', 'closed', true);

    // Assert
    expect(transitionStatus(agy, unchecked, 'delivery', 'client_qa', 'closed')).toBe('removed');
    expect(pendingChangeCount(agy, unchecked)).toBe(1);
    expect(pairs(workflowOf(buildChangeSet(agy, 1, unchecked, '').blueprint, 'delivery'))).not.toContain('client_qa->closed');
    expect(transitionStatus(agy, rechecked, 'delivery', 'client_qa', 'closed')).toBe('unchanged');
    expect(pendingChangeCount(agy, rechecked)).toBe(0);
    expect(buildChangeSet(agy, 1, rechecked, '').blueprint.workflows[0]).toBe(agy.workflows[0]);
  });

  it('新的轉換接在最後，形狀和 API 一樣帶兩個陣列', () => {
    // Arrange & Act
    const draft = setTransition(agy, draftFromBlueprint(agy), 'delivery', 'developing', 'client_qa', true);
    const delivery = workflowOf(buildChangeSet(agy, 1, draft, '').blueprint, 'delivery');

    // Assert
    expect(delivery.transitions?.at(-1)).toEqual({ from: 'developing', to: 'client_qa', allowedRoles: [], requireFields: [] });
    expect(delivery.transitions?.slice(0, -1)).toEqual(agy.workflows[0]?.transitions);
    expect(transitionStatus(agy, draft, 'delivery', 'developing', 'client_qa')).toBe('added');
    expect(pendingChangeCount(agy, draft)).toBe(1);
  });

  it('改允許的角色與要先填的欄位；改回同一組（先後不同）不算修改', () => {
    // Arrange
    const draft = draftFromBlueprint(agy);

    // Act
    const changed = updateTransition(draft, 'delivery', 'client_qa', 'closed', { allowedRoles: ['admin', 'project_manager'], requireFields: ['description', 'client_signoff_date'] });
    const reverted = updateTransition(changed, 'delivery', 'client_qa', 'closed', { allowedRoles: ['project_manager'], requireFields: ['client_signoff_date'] });

    // Assert
    expect(transitionStatus(agy, changed, 'delivery', 'client_qa', 'closed')).toBe('modified');
    expect(workflowOf(buildChangeSet(agy, 1, changed, '').blueprint, 'delivery').transitions?.at(-1)).toEqual({
      from: 'client_qa',
      to: 'closed',
      allowedRoles: ['admin', 'project_manager'],
      requireFields: ['description', 'client_signoff_date'],
    });
    expect(transitionStatus(agy, reverted, 'delivery', 'client_qa', 'closed')).toBe('unchanged');
    expect(buildChangeSet(agy, 1, reverted, '').blueprint).toEqual(agy);
    expect(buildChangeSet(agy, 1, reverted, '').blueprint.workflows[0]).toBe(agy.workflows[0]);
  });

  it('只改了要先填的欄位：允許的角色和原本是同一組就送原本的陣列，不會被重新排序', () => {
    // Arrange
    const base: ProjectBlueprint = {
      ...agy,
      workflows: agy.workflows.map((workflow) => ({
        ...workflow,
        ...(workflow.transitions && {
          transitions: workflow.transitions.map((transition) =>
            transition.to === 'closed' ? { ...transition, allowedRoles: ['project_manager', 'admin'] } : transition,
          ),
        }),
      })),
    };
    const original = workflowOf(base, 'delivery').transitions?.at(-1);

    // Act
    const draft = updateTransition(draftFromBlueprint(base), 'delivery', 'client_qa', 'closed', {
      allowedRoles: ['admin', 'project_manager'],
      requireFields: ['description'],
    });

    // Assert
    const sent = workflowOf(buildChangeSet(base, 1, draft, '').blueprint, 'delivery').transitions?.at(-1);
    expect(sent).toEqual({ from: 'client_qa', to: 'closed', allowedRoles: ['project_manager', 'admin'], requireFields: ['description'] });
    expect(sent?.allowedRoles).toBe(original?.allowedRoles);
  });

  it('不限制切成限制：從每兩個狀態都可以互轉開始，不算個別的修改；取消一條算一項', () => {
    // Arrange
    const draft = setRestricted(smk, draftFromBlueprint(smk), 'task_flow', true);

    // Act
    const trimmed = setTransition(smk, draft, 'task_flow', 'done', 'todo', false);

    // Assert
    const flow = workflowOf(buildChangeSet(smk, 1, draft, '').blueprint, 'task_flow');
    expect(pairs(flow)).toEqual(['todo->doing', 'todo->done', 'doing->todo', 'doing->done', 'done->todo', 'done->doing']);
    expect(flow.transitions?.[0]).toEqual({ from: 'todo', to: 'doing', allowedRoles: [], requireFields: [] });
    expect(pendingChangeCount(smk, draft)).toBe(1);
    expect(transitionStatus(smk, draft, 'task_flow', 'todo', 'doing')).toBe('unchanged');
    expect(transitionStatus(smk, trimmed, 'task_flow', 'done', 'todo')).toBe('removed');
    expect(pendingChangeCount(smk, trimmed)).toBe(2);
    expect(allPairs(smk.workflows[0]?.states ?? [])).toHaveLength(6);
  });

  it('限制切回不限制：transitions 整個不送；再切回限制回到目前藍圖的轉換', () => {
    // Arrange
    const open = setRestricted(agy, draftFromBlueprint(agy), 'delivery', false);

    // Act
    const restricted = setRestricted(agy, open, 'delivery', true);

    // Assert
    const delivery = workflowOf(buildChangeSet(agy, 1, open, '').blueprint, 'delivery');
    expect('transitions' in delivery).toBe(false);
    expect(delivery.states).toEqual(agy.workflows[0]?.states);
    expect(pendingChangeCount(agy, open)).toBe(1);
    expect(buildChangeSet(agy, 1, restricted, '').blueprint).toEqual(agy);
    expect(pendingChangeCount(agy, restricted)).toBe(0);
  });

  it('新增狀態時一起加進出的轉換；不限制的流程不加', () => {
    // Arrange
    const state = { key: 'review', name: '審查', category: 'in_progress' } as const;

    // Act
    const draft = addStateTransitions(addState(draftFromBlueprint(agy), 'delivery', state, 6), 'delivery', 'review', ['client_qa'], ['closed']);
    const open = addStateTransitions(addState(draftFromBlueprint(smk), 'task_flow', state, 2), 'task_flow', 'review', ['doing'], ['done']);

    // Assert
    expect(pairs(workflowOf(buildChangeSet(agy, 1, draft, '').blueprint, 'delivery')).slice(-2)).toEqual(['client_qa->review', 'review->closed']);
    expect(pendingChangeCount(agy, draft)).toBe(1);
    expect('transitions' in workflowOf(buildChangeSet(smk, 1, open, '').blueprint, 'task_flow')).toBe(false);
  });

  it('刪除狀態時草稿留著轉換、送出時才拿掉；復原狀態時轉換跟著回來，兩端被刪除的不另外計數', () => {
    // Arrange
    const removed = setMoveTarget(removeState(agy, draftFromBlueprint(agy), 'delivery', 'internal_qa'), 'delivery', 'internal_qa', 'developing');

    // Act
    const restored = restoreState(agy, removed, 'delivery', 'internal_qa');

    // Assert
    expect(removed.workflows[0]?.transitions).toBe(agy.workflows[0]?.transitions);
    expect(pendingChangeCount(agy, removed)).toBe(1);
    expect(buildChangeSet(agy, 1, restored, '').blueprint.workflows[0]).toBe(agy.workflows[0]);
  });

  it('一端是新狀態的轉換不另外計數：先新增再切成限制、和先切成限制再新增，計數一樣', () => {
    // Arrange
    const state = { key: 'review', name: '審查', category: 'in_progress' } as const;
    const addReview = (draft: ReturnType<typeof draftFromBlueprint>) =>
      addStateTransitions(addState(draft, 'task_flow', state, 2), 'task_flow', 'review', ['doing'], ['done']);

    // Act
    const addedFirst = setRestricted(smk, addReview(draftFromBlueprint(smk)), 'task_flow', true);
    const restrictedFirst = addReview(setRestricted(smk, draftFromBlueprint(smk), 'task_flow', true));

    // Assert
    expect(pairs(workflowOf(buildChangeSet(smk, 1, addedFirst, '').blueprint, 'task_flow'))).toHaveLength(12);
    expect(pendingChangeCount(smk, addedFirst)).toBe(2);
    expect(pendingChangeCount(smk, restrictedFirst)).toBe(2);
    expect(transitionStatus(smk, addedFirst, 'task_flow', 'review', 'todo')).toBe('added');
  });

  it('不限制的流程勾選轉換沒有作用', () => {
    // Arrange & Act
    const draft = setTransition(smk, draftFromBlueprint(smk), 'task_flow', 'todo', 'done', true);

    // Assert
    expect(draft.workflows[0]?.transitions).toBeUndefined();
    expect(transitionStatus(smk, draft, 'task_flow', 'todo', 'done')).toBe('unchanged');
  });

  it('剛新增的狀態、欄位、角色刪掉時一起拿掉轉換裡的引用：之後再用同一個 key 新增，舊的轉換與限制不會跑回來', () => {
    // Arrange
    const state = { key: 'review', name: '審查', category: 'in_progress' } as const;
    const added = addStateTransitions(addState(draftFromBlueprint(agy), 'delivery', state, 6), 'delivery', 'review', ['client_qa'], ['closed']);
    const withExtras = addRole(addField(added, { key: 'extra', name: '補充', type: 'text', appliesTo: ['requirement', 'change_request'] }), { key: 'qa', name: 'QA', permissions: [] });
    const limited = updateTransition(withExtras, 'delivery', 'confirming', 'quoting', { allowedRoles: ['qa'], requireFields: ['extra'] });

    // Act
    const removed = removeRole(agy, removeField(agy, removeState(agy, limited, 'delivery', 'review'), 'extra'), 'qa');
    const readded = addRole(addField(addState(removed, 'delivery', state, 6), { key: 'extra', name: '補充', type: 'text', appliesTo: [] }), { key: 'qa', name: 'QA', permissions: [] });

    // Assert
    expect(removed.workflows[0]?.transitions).toEqual(agy.workflows[0]?.transitions?.map((transition, index) => (index === 0 ? { ...transition, allowedRoles: [], requireFields: [] } : transition)));
    expect(transitionStatus(agy, removed, 'delivery', 'confirming', 'quoting')).toBe('unchanged');
    expect(transitionStatus(agy, readded, 'delivery', 'client_qa', 'review')).toBe('unchanged');
    expect(pendingChangeCount(agy, removed)).toBe(0);
  });
});
