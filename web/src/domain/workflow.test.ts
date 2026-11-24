import { describe, expect, it } from 'vitest';
import type { Workflow } from '../api/blueprint';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import {
  boardWorkflows,
  filterableStates,
  findTransition,
  moveAvailability,
  moveTargets,
  reachableStates,
  workflowTypes,
} from './workflow';

function workflow(blueprintWorkflows: readonly Workflow[], key: string): Workflow {
  const found = blueprintWorkflows.find((candidate) => candidate.key === key);
  if (found === undefined) {
    throw new Error(`測試資料少了流程 ${key}`);
  }
  return found;
}

const delivery = workflow(agencyBlueprint.workflows, 'delivery');
const taskFlow = workflow(simpleTasksBlueprint.workflows, 'task_flow');

describe('reachableStates', () => {
  it('流程有列轉換時，只列從目前狀態走得到的', () => {
    // Arrange / Act
    const states = reachableStates(delivery, 'internal_qa');

    // Assert
    expect(states.map((state) => state.key)).toEqual(['developing', 'client_qa']);
  });

  it('流程沒有列轉換時，其他每個狀態都走得到', () => {
    // Arrange / Act
    const states = reachableStates(taskFlow, 'doing');

    // Assert
    expect(states.map((state) => state.key)).toEqual(['todo', 'done']);
  });

  it('轉換是空陣列時一條路都沒有（和後端一樣：只有沒有這個屬性才是哪裡都可以）', () => {
    // Arrange
    const closed: Workflow = { ...taskFlow, transitions: [] };

    // Act
    const states = reachableStates(closed, 'todo');

    // Assert
    expect(states).toEqual([]);
  });
});

describe('findTransition', () => {
  it('找得到列出的轉換與它的限制', () => {
    // Arrange / Act
    const found = findTransition(delivery, 'client_qa', 'closed');

    // Assert
    expect(found?.allowedRoles).toEqual(['project_manager']);
    expect(found?.requireFields).toEqual(['client_signoff_date']);
  });

  it('沒有列的轉換回 undefined', () => {
    // Arrange / Act / Assert
    expect(findTransition(delivery, 'confirming', 'closed')).toBeUndefined();
    expect(findTransition(taskFlow, 'todo', 'done')).toBeUndefined();
  });
});

describe('filterableStates', () => {
  it('選了類型時只列那個類型的流程的狀態', () => {
    // Arrange / Act
    const states = filterableStates(agencyBlueprint, 'task');

    // Assert
    expect(states.map((state) => state.key)).toEqual(['todo', 'doing', 'done']);
  });

  it('沒選類型時列出所有流程的狀態，同一個 key 只列一次', () => {
    // Arrange
    const blueprint = {
      ...agencyBlueprint,
      workflows: [...agencyBlueprint.workflows, { ...taskFlow, key: 'another_flow' }],
    };

    // Act
    const keys = filterableStates(blueprint, null).map((state) => state.key);

    // Assert
    expect(keys).toEqual([
      'confirming',
      'quoting',
      'designing',
      'developing',
      'internal_qa',
      'client_qa',
      'closed',
      'todo',
      'doing',
      'done',
    ]);
  });

  it('類型不存在時是空的', () => {
    // Arrange / Act / Assert
    expect(filterableStates(agencyBlueprint, 'nope')).toEqual([]);
  });
});

describe('boardWorkflows', () => {
  it('只列有類型在用的流程，照第一個用到它的類型排；同一個流程只列一次', () => {
    // Arrange
    const blueprint = {
      ...agencyBlueprint,
      workflows: [...agencyBlueprint.workflows, { ...taskFlow, key: 'unused_flow' }],
    };

    // Act
    const keys = boardWorkflows(blueprint).map((candidate) => candidate.key);

    // Assert
    expect(keys).toEqual(['delivery', 'task_flow']);
  });
});

describe('workflowTypes', () => {
  it('列出用這個流程的類型，照藍圖的順序', () => {
    // Arrange / Act / Assert
    expect(workflowTypes(agencyBlueprint, 'delivery').map((itemType) => itemType.key)).toEqual([
      'requirement',
      'change_request',
    ]);
    expect(workflowTypes(agencyBlueprint, 'nope')).toEqual([]);
  });
});

describe('moveAvailability', () => {
  it('流程沒有列轉換時，到哪裡都可以', () => {
    // Arrange / Act / Assert
    expect(moveAvailability(taskFlow, 'todo', 'done', 'viewer')).toEqual({ kind: 'open' });
  });

  it('轉換是空陣列時，到哪裡都是 no_path', () => {
    // Arrange
    const closed: Workflow = { ...taskFlow, transitions: [] };

    // Act / Assert
    expect(moveAvailability(closed, 'todo', 'done', 'viewer')).toEqual({ kind: 'no_path' });
    expect(moveTargets(closed, 'todo', 'viewer').map((target) => target.availability.kind)).toEqual(['no_path', 'no_path']);
  });

  it('沒有列的路是 no_path；有限制角色而我不是，回需要的角色', () => {
    // Arrange / Act / Assert
    expect(moveAvailability(delivery, 'confirming', 'closed', 'admin')).toEqual({ kind: 'no_path' });
    expect(moveAvailability(delivery, 'internal_qa', 'client_qa', 'admin')).toEqual({
      kind: 'role',
      roles: ['project_manager'],
    });
    expect(moveAvailability(delivery, 'internal_qa', 'client_qa', 'project_manager')).toEqual({ kind: 'open' });
  });

  it('不是專案成員（組織管理員）時，有角色限制的轉換也走不到', () => {
    // Arrange / Act / Assert
    expect(moveAvailability(delivery, 'internal_qa', 'client_qa', null)).toEqual({
      kind: 'role',
      roles: ['project_manager'],
    });
    expect(moveAvailability(delivery, 'internal_qa', 'developing', null)).toEqual({ kind: 'open' });
  });
});

describe('moveTargets', () => {
  it('列出流程裡其他的狀態：走得到的在前，各自照流程的順序', () => {
    // Arrange / Act
    const targets = moveTargets(delivery, 'internal_qa', 'admin');

    // Assert
    expect(targets.map((target) => [target.state.key, target.availability.kind])).toEqual([
      ['developing', 'open'],
      ['confirming', 'no_path'],
      ['quoting', 'no_path'],
      ['designing', 'no_path'],
      ['client_qa', 'role'],
      ['closed', 'no_path'],
    ]);
  });
});
