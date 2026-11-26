import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { queryKeys } from '../api/queryKeys';
import { conditionsOf, touchesConditions } from '../features/item-table/listConditions';
import { agyDetail, smkItem } from '../test/fixtures';
import { STATE_GROUP } from './boardGroup';
import { EMPTY_FILTERS } from './itemFilters';
import { paramsOfViewState, queryFromBoardFilters, queryFromTableFilters } from './savedViews';

const AGY = agyDetail().blueprint;
const WITH_CLIENT: ProjectBlueprint = {
  ...AGY,
  fields: [...AGY.fields, { key: 'client', name: '客戶', type: 'single_select', options: [{ key: 'client_a', name: 'ACME' }] }],
};

describe('清單的條件看得到自訂欄位', () => {
  it('改了條件裡的欄位或排序的欄位，算動到條件；改別的欄位不算', () => {
    // Arrange
    const key = queryKeys.items('SMK', { fields: { client: ['client_a'] }, sort: { field: 'field.hours', direction: 'asc' } });
    const conditions = conditionsOf(key);
    const before = smkItem({ number: 1, fields: { client: 'client_a', hours: 2, note: 'a' } });

    // Act / Assert
    expect(conditions.fields).toEqual({ client: ['client_a'] });
    expect(conditions.sortField).toBe('field.hours');
    expect(touchesConditions(conditions, before, { ...before, fields: { ...before.fields, client: 'client_b' } })).toBe(true);
    expect(touchesConditions(conditions, before, { ...before, fields: { ...before.fields, hours: 3 } })).toBe(true);
    expect(touchesConditions(conditions, before, { ...before, fields: { ...before.fields, note: 'b' } })).toBe(false);
  });
});

describe('檢視存得下自訂欄位的條件與看板的分組', () => {
  it('表格：存 fields，沒有時是 null；分組一律 null', () => {
    // Arrange / Act
    const saved = queryFromTableFilters({ ...EMPTY_FILTERS, fields: { client: 'client_a' } });
    const empty = queryFromTableFilters(EMPTY_FILTERS);

    // Assert
    expect(saved.fields).toEqual({ client: 'client_a' });
    expect(saved.group).toBeNull();
    expect(empty.fields).toBeNull();
  });

  it('看板：存分組；套用時欄位還在才帶回網址', () => {
    // Arrange
    const filters = {
      workflow: null,
      assignee: null,
      createdBy: null,
      due: null,
      priority: null,
      cycle: null,
      milestone: null,
      fields: { client: 'client_a' },
      group: { kind: 'field', key: 'client' } as const,
      q: '',
    };

    // Act
    const query = queryFromBoardFilters(filters);
    const known = paramsOfViewState({ layout: 'board', query }, WITH_CLIENT).toString();
    const gone = paramsOfViewState({ layout: 'board', query }, AGY).toString();

    // Assert
    expect(query.group).toBe('field.client');
    expect(known).toBe('view=board&group=field.client&field.client=client_a');
    expect(gone).toBe('view=board');
  });

  it('依狀態分組不存（是預設）', () => {
    // Arrange / Act
    const query = queryFromBoardFilters({
      workflow: null,
      assignee: null,
      createdBy: null,
      due: null,
      priority: null,
      cycle: null,
      milestone: null,
      fields: {},
      group: STATE_GROUP,
      q: '',
    });

    // Assert
    expect(query.group).toBeNull();
    expect(query.fields).toBeNull();
  });
});
