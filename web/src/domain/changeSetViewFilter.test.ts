import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint, ViewFilter } from '../api/blueprint';
import type { Change } from '../api/types';
import { agencyBlueprint } from '../test/blueprints';
import { describeBlueprintChange } from './changeSetText';

const view: Change = { kind: 'modified', resource: 'view', key: 'client_view', parentKey: null, changedProperties: ['filter'] };

describe('檢視的篩選條件說出改了什麼', () => {
  const withFilter = (filter: ViewFilter | undefined): ProjectBlueprint => ({
    ...agencyBlueprint,
    views: [{ key: 'client_view', name: '給客戶看', type: 'table', ...(filter === undefined ? {} : { filter }) }],
  });

  it.each([
    [{ itemTypes: ['requirement', 'change_request'] }, { itemTypes: ['change_request'] }, '類型篩選拿掉「需求」'],
    [{ itemTypes: ['requirement'] }, { itemTypes: ['requirement', 'task'] }, '類型篩選多了「任務」'],
    [{ itemTypes: ['requirement'] }, { itemTypes: ['task'] }, '類型篩選多了「任務」，拿掉「需求」'],
    [{ itemTypes: [] }, { itemTypes: ['task'] }, '類型篩選改成「任務」'],
    [undefined, { itemTypes: ['task'] }, '類型篩選改成「任務」'],
    [{ itemTypes: ['requirement'] }, { itemTypes: [] }, '不再篩選類型（原本是「需求」）'],
    [{ itemTypes: ['requirement', 'task'] }, { itemTypes: ['task', 'requirement'] }, '調整了類型篩選的順序'],
    [{ states: ['developing'] }, { states: ['developing', 'internal_qa'] }, '狀態篩選多了「內部驗收」'],
    [{ states: [] }, { states: ['todo'] }, '狀態篩選改成「待辦」'],
    [{ stateCategories: ['in_progress'] }, { stateCategories: ['done'] }, '類別篩選多了「完成」，拿掉「進行中」'],
    [{}, { assignee: 'me' }, '負責人篩選改成「看的人自己」'],
    [{ assignee: 'me' }, { assignee: 'unassigned' }, '負責人篩選改成「沒有負責人」'],
    [{ assignee: 'me' }, {}, '不再篩選負責人'],
    [
      { itemTypes: ['requirement'], assignee: 'me' },
      { itemTypes: [], states: ['todo'] },
      '不再篩選類型（原本是「需求」）；狀態篩選改成「待辦」；不再篩選負責人',
    ],
    [{ itemTypes: ['task'] }, { itemTypes: ['task'] }, '篩選條件有調整'],
  ] satisfies readonly (readonly [ViewFilter | undefined, ViewFilter, string])[])('%#：%s', (before, after, expected) => {
    // Arrange
    const context = { before: withFilter(before), after: withFilter(after) };

    // Act
    const text = describeBlueprintChange(view, context);

    // Assert
    expect(text).toBe(`修改檢視「給客戶看」：${expected}`);
  });
});
