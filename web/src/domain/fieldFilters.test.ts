import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { boardGroupParam, knownBoardGroup, parseBoardGroup, STATE_GROUP } from './boardGroup';
import {
  fieldFilterEntries,
  fieldFiltersFromSearchParams,
  fieldFiltersQuery,
  fieldSort,
  filterableFields,
  knownFieldFilters,
  sortFieldKey,
} from './fieldFilters';
import { filtersFromSearchParams, filtersToSearchParams, toItemQuery } from './itemFilters';
import { boardFiltersFromSearchParams, boardFiltersToSearchParams, matchesFieldFilter } from './projectView';
import { smkItem } from '../test/fixtures';

const BLUEPRINT = {
  apiVersion: 'blueprint/v0',
  kind: 'ProjectBlueprint',
  metadata: { key: 'maint', name: '維運' },
  fields: [
    { key: 'client', name: '客戶', type: 'single_select', options: [{ key: 'client_a', name: 'ACME' }] },
    { key: 'tags', name: '標籤', type: 'multi_select', options: [{ key: 'web', name: '網站' }] },
    { key: 'reviewer', name: '審查', type: 'person' },
    { key: 'billable', name: '要收費', type: 'checkbox' },
    { key: 'actual_hours', name: '實際工時', type: 'number' },
  ],
  workflows: [],
  itemTypes: [],
} as unknown as ProjectBlueprint;

describe('自訂欄位的篩選在網址上', () => {
  it('讀出 field.<key>，略過空的值與不像 key 的名稱', () => {
    // Arrange
    const params = new URLSearchParams('field.client=client_a&field.tags=%20&field.Bad=x&field.reviewer=me');

    // Act
    const fields = fieldFiltersFromSearchParams(params);

    // Assert
    expect(fields).toEqual({ client: 'client_a', reviewer: 'me' });
  });

  it('寫回網址時依 key 排序，同樣的條件網址一樣', () => {
    // Arrange / Act
    const entries = fieldFilterEntries({ tags: 'web', client: 'client_a' });

    // Assert
    expect(entries).toEqual([
      ['field.client', 'client_a'],
      ['field.tags', 'web'],
    ]);
  });

  it('表格的條件來回一次不變，送給 API 的是每個欄位一個值的陣列', () => {
    // Arrange
    const params = new URLSearchParams('field.client=_none&sort=field.actual_hours:desc');

    // Act
    const filters = filtersFromSearchParams(params);
    const query = toItemQuery(filters, 50, '2026-11-19', 'monday');

    // Assert
    expect(filtersToSearchParams(filters).toString()).toBe('field.client=_none&sort=field.actual_hours%3Adesc');
    expect(query.fields).toEqual({ client: ['_none'] });
    expect(query.sort).toEqual({ field: 'field.actual_hours', direction: 'desc' });
  });

  it('沒有條件時不送 fields', () => {
    // Arrange / Act / Assert
    expect(fieldFiltersQuery({})).toBeUndefined();
    expect(toItemQuery(filtersFromSearchParams(new URLSearchParams('')), 50, '2026-11-19', 'monday')).not.toHaveProperty('fields');
  });

  it('只留藍圖裡還在、而且能篩的欄位', () => {
    // Arrange / Act
    const known = knownFieldFilters({ client: 'client_a', actual_hours: '3', gone: 'x' }, BLUEPRINT);

    // Assert
    expect(known).toEqual({ client: 'client_a' });
    expect(filterableFields(BLUEPRINT).map((field) => field.key)).toEqual(['client', 'tags', 'reviewer', 'billable']);
  });

  it('值不合欄位型別的略過：後端會回 422 整張清單擋掉', () => {
    // Arrange
    const guid = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
    const bad = { client: 'a,b', tags: 'Web Site', reviewer: 'someone', billable: '_none' };

    // Act / Assert
    expect(knownFieldFilters(bad, BLUEPRINT)).toEqual({});
    expect(knownFieldFilters({ client: 'x', tags: 'Bad' }, BLUEPRINT)).toEqual({});
    expect(knownFieldFilters({ billable: 'yes' }, BLUEPRINT)).toEqual({});
    expect(knownFieldFilters({ reviewer: `${guid},${guid}` }, BLUEPRINT)).toEqual({});
    expect(
      knownFieldFilters({ client: '_none', tags: 'web', reviewer: guid, billable: 'false' }, BLUEPRINT),
    ).toEqual({ client: '_none', tags: 'web', reviewer: guid, billable: 'false' });
    expect(knownFieldFilters({ reviewer: 'me' }, BLUEPRINT)).toEqual({ reviewer: 'me' });
    expect(knownFieldFilters({ reviewer: '_none', billable: 'true' }, BLUEPRINT)).toEqual({ reviewer: '_none', billable: 'true' });
    // 選項不在藍圖裡但格式對：照樣送，後端查不到而已。
    expect(knownFieldFilters({ client: 'client_gone' }, BLUEPRINT)).toEqual({ client: 'client_gone' });
  });

  it('排序欄位 field.<key> 拆得出 key；核心欄位不是', () => {
    // Arrange / Act / Assert
    expect(sortFieldKey(fieldSort('client'))).toBe('client');
    expect(sortFieldKey('due_date')).toBeNull();
    expect(sortFieldKey('field.Bad')).toBeNull();
  });
});

describe('看板的分組', () => {
  it('讀寫 group：負責人、單選欄位；依狀態是預設，不寫', () => {
    // Arrange / Act / Assert
    expect(parseBoardGroup('assignee')).toEqual({ kind: 'assignee' });
    expect(parseBoardGroup('field.client')).toEqual({ kind: 'field', key: 'client' });
    expect(parseBoardGroup('whatever')).toEqual(STATE_GROUP);
    expect(parseBoardGroup(null)).toEqual(STATE_GROUP);
    expect(boardGroupParam(STATE_GROUP)).toBeNull();
    expect(boardGroupParam({ kind: 'field', key: 'client' })).toBe('field.client');
  });

  it('欄位不在了或不是單選，回到依狀態', () => {
    // Arrange / Act / Assert
    expect(knownBoardGroup({ kind: 'field', key: 'client' }, BLUEPRINT)).toEqual({ kind: 'field', key: 'client' });
    expect(knownBoardGroup({ kind: 'field', key: 'tags' }, BLUEPRINT)).toEqual(STATE_GROUP);
    expect(knownBoardGroup({ kind: 'field', key: 'gone' }, BLUEPRINT)).toEqual(STATE_GROUP);
    expect(knownBoardGroup({ kind: 'assignee' }, BLUEPRINT)).toEqual({ kind: 'assignee' });
  });

  it('只有看板讀寫 group；行事曆的網址上有也不算', () => {
    // Arrange
    const board = boardFiltersFromSearchParams(new URLSearchParams('view=board&group=assignee&field.client=client_a'));
    const calendar = boardFiltersFromSearchParams(new URLSearchParams('view=calendar&group=assignee'));

    // Act / Assert
    expect(board.group).toEqual({ kind: 'assignee' });
    expect(board.fields).toEqual({ client: 'client_a' });
    expect(calendar.group).toEqual(STATE_GROUP);
    expect(boardFiltersToSearchParams(board, undefined).toString()).toBe('view=board&group=assignee&field.client=client_a');
    expect(boardFiltersToSearchParams(board, undefined, 'calendar').get('group')).toBeNull();
  });
});

describe('前端比對自訂欄位的條件（和後端一樣）', () => {
  const me = '0190a000-0000-7000-8000-000000000001';
  const item = smkItem({ number: 1, fields: { client: 'client_a', tags: ['web'], reviewer: me, billable: true } });
  const blank = smkItem({ number: 2, fields: { tags: [] } });

  it('單選、多選、人員（含 me）、勾選', () => {
    // Arrange / Act / Assert
    expect(matchesFieldFilter(item, 'client', 'client_a', me)).toBe(true);
    expect(matchesFieldFilter(item, 'client', 'client_b', me)).toBe(false);
    expect(matchesFieldFilter(item, 'tags', 'web', me)).toBe(true);
    expect(matchesFieldFilter(item, 'reviewer', 'me', me)).toBe(true);
    expect(matchesFieldFilter(item, 'billable', 'true', me)).toBe(true);
    expect(matchesFieldFilter(blank, 'billable', 'false', me)).toBe(true);
  });

  it('_none 是沒有填：沒有這個欄位、或多選是空的', () => {
    // Arrange / Act / Assert
    expect(matchesFieldFilter(blank, 'client', '_none', me)).toBe(true);
    expect(matchesFieldFilter(blank, 'tags', '_none', me)).toBe(true);
    expect(matchesFieldFilter(item, 'client', '_none', me)).toBe(false);
  });
});
