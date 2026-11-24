import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import { agencyBlueprint, simpleTasksBlueprint } from '../../test/blueprints';
import { stateFilterChoices, stateKeyOfValue } from './stateChoices';

function groupsOf(choices: ReturnType<typeof stateFilterChoices>): (readonly [string | undefined, string])[] {
  return choices.options.filter((option) => option.groupLabel !== undefined).map((option) => [option.groupLabel, option.label] as const);
}

describe('篩選的狀態選項', () => {
  it('沒選類型、專案有兩個流程：依流程分組，組名是用它的類型，順序照類型', () => {
    // Arrange / Act
    const choices = stateFilterChoices(agencyBlueprint, null);

    // Assert
    expect(groupsOf(choices)).toEqual([
      ['需求、需求變更單', '需求確認'],
      ['任務', '待辦'],
    ]);
    expect(choices.options.map((option) => option.label)).toEqual([
      '需求確認', '報價', '設計', '開發', '內部驗收', '客戶驗收', '結案', '待辦', '進行中', '完成',
    ]);
  });

  it('選了類型：只有那個類型的流程，不分組', () => {
    // Arrange / Act
    const choices = stateFilterChoices(agencyBlueprint, 'task');

    // Assert
    expect(choices.options.map((option) => option.label)).toEqual(['待辦', '進行中', '完成']);
    expect(groupsOf(choices)).toEqual([]);
  });

  it('只有一個流程：不分組', () => {
    // Arrange / Act
    const choices = stateFilterChoices(simpleTasksBlueprint, null);

    // Assert
    expect(choices.options.length).toBeGreaterThan(0);
    expect(groupsOf(choices)).toEqual([]);
  });

  it('藍圖沒有的類型：沒有選項', () => {
    // Arrange / Act
    const choices = stateFilterChoices(agencyBlueprint, 'gone');

    // Assert
    expect(choices.options).toEqual([]);
  });

  it('沒有類型在用的流程接在最後，組名是流程的名稱（網址上的狀態還是選得到）', () => {
    // Arrange
    const blueprint: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: [
        { key: 'spare', name: '備用流程', states: [{ key: 'parked', name: '停放', category: 'backlog' }] },
        ...agencyBlueprint.workflows,
      ],
    };

    // Act
    const choices = stateFilterChoices(blueprint, null);

    // Assert
    expect(groupsOf(choices).map(([label]) => label)).toEqual(['需求、需求變更單', '任務', '備用流程']);
  });

  it('同 key 同名稱只列一次；同 key 不同名稱各一個選項，都對回同一個 key', () => {
    // Arrange
    const blueprint: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: [
        {
          key: 'content_flow',
          name: '內容流程',
          states: [
            { key: 'planning', name: '企劃', category: 'not_started' },
            { key: 'canceled', name: '取消', category: 'canceled' },
          ],
        },
        {
          key: 'campaign_flow',
          name: '活動流程',
          states: [
            { key: 'planning', name: '規劃中', category: 'not_started' },
            { key: 'canceled', name: '取消', category: 'canceled' },
          ],
        },
      ],
      itemTypes: [
        { key: 'content', name: '內容', workflow: 'content_flow', fields: [], parents: [] },
        { key: 'campaign', name: '活動', workflow: 'campaign_flow', fields: [], parents: [] },
      ],
    };

    // Act
    const choices = stateFilterChoices(blueprint, null);

    // Assert
    expect(choices.options.map((option) => option.label)).toEqual(['企劃', '取消', '規劃中']);
    const values = choices.options.map((option) => option.value);
    expect(new Set(values).size).toBe(3);
    expect(values.map((value) => choices.stateKeyOf(value))).toEqual(['planning', 'canceled', 'planning']);
    expect(values.map(stateKeyOfValue)).toEqual(['planning', 'canceled', 'planning']);
  });

  it('一個流程的狀態前面都列過了：那一組不留，只剩一組就不畫組名', () => {
    // Arrange
    const states = [{ key: 'todo', name: '待辦', category: 'not_started' as const }];
    const blueprint: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: [
        { key: 'first', name: '第一個流程', states },
        { key: 'copy', name: '一樣的流程', states },
      ],
      itemTypes: [
        { key: 'task', name: '任務', workflow: 'first', fields: [], parents: [] },
        { key: 'chore', name: '雜事', workflow: 'copy', fields: [], parents: [] },
      ],
    };

    // Act
    const choices = stateFilterChoices(blueprint, null);

    // Assert
    expect(choices.options.map((option) => option.label)).toEqual(['待辦']);
    expect(groupsOf(choices)).toEqual([]);
  });

  it('不是這份選項的值（「所有狀態」的空字串）對不到 key', () => {
    // Arrange / Act
    const choices = stateFilterChoices(agencyBlueprint, null);

    // Assert
    expect(choices.stateKeyOf('')).toBeUndefined();
  });
});
