import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import { agencyBlueprint } from '../../test/blueprints';
import { agyItem, smkItem } from '../../test/fixtures';
import { stateChoicesFor } from './stateChoices';

function labels(choices: ReturnType<typeof stateChoicesFor>): (string | undefined)[] {
  return choices.options.map((option) => option.label);
}

describe('批次改狀態的選項', () => {
  it('選取的項目用同一個流程：就是那個流程的狀態，不分組', () => {
    // Arrange
    const items = [agyItem({ number: 1 }), agyItem({ number: 2 })];

    // Act
    const choices = stateChoicesFor(agencyBlueprint, items);

    // Assert
    expect(labels(choices)).toEqual(['需求確認', '報價', '設計', '開發', '內部驗收', '客戶驗收', '結案']);
    expect(choices.options.some((option) => option.groupLabel !== undefined)).toBe(false);
  });

  it('同一個流程的兩種類型（需求與需求變更單）只算一個流程', () => {
    // Arrange
    const items = [agyItem({ number: 1 }), agyItem({ number: 2, type: 'change_request' })];

    // Act
    const choices = stateChoicesFor(agencyBlueprint, items);

    // Assert
    expect(choices.options).toHaveLength(7);
  });

  it('分屬不同流程：依流程分組，每組第一項帶流程的名稱', () => {
    // Arrange
    const items = [agyItem({ number: 1 }), agyItem({ number: 2, type: 'task', state: 'todo' })];

    // Act
    const choices = stateChoicesFor(agencyBlueprint, items);

    // Assert
    const groups = choices.options.filter((option) => option.groupLabel !== undefined);
    expect(groups.map((option) => [option.groupLabel, option.label])).toEqual([
      ['交付流程', '需求確認'],
      ['任務流程', '待辦'],
    ]);
  });

  it('不同流程裡同 key 同名稱的狀態只列一次；同 key 不同名稱的分成兩個選項，各自對回自己的 key', () => {
    // Arrange
    const blueprint: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: [
        {
          key: 'delivery',
          name: '交付流程',
          states: [
            { key: 'todo', name: '待辦', category: 'not_started' },
            { key: 'done', name: '完成', category: 'done' },
          ],
        },
        {
          key: 'task_flow',
          name: '任務流程',
          states: [
            { key: 'todo', name: '待辦', category: 'not_started' },
            { key: 'done', name: '做完了', category: 'done' },
          ],
        },
      ],
      itemTypes: [
        { key: 'requirement', name: '需求', workflow: 'delivery', fields: [], parents: [] },
        { key: 'task', name: '任務', workflow: 'task_flow', fields: [], parents: [] },
      ],
    };
    const items = [agyItem({ number: 1 }), agyItem({ number: 2, type: 'task' })];

    // Act
    const choices = stateChoicesFor(blueprint, items);

    // Assert
    expect(labels(choices)).toEqual(['待辦', '完成', '做完了']);
    const values = choices.options.map((option) => option.value);
    expect(new Set(values).size).toBe(3);
    expect(values.map((value) => choices.stateKeyOf(value))).toEqual(['todo', 'done', 'done']);
  });

  it('藍圖沒有的類型（已經被拿掉）不貢獻選項', () => {
    // Arrange
    const items = [smkItem({ number: 1, type: 'gone' })];

    // Act
    const choices = stateChoicesFor(agencyBlueprint, items);

    // Assert
    expect(choices.options).toEqual([]);
  });
});
