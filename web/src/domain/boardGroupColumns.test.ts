import { describe, expect, it } from 'vitest';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { agyItem, exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin, smkItem } from '../test/fixtures';
import type { BoardGroup } from './boardGroup';
import {
  BOARD_GROUP_PAGE_SIZE,
  boardGroupQuery,
  columnKeyOf,
  dropBlockedReason,
  groupColumns,
  groupName,
  groupPatch,
  movedItem,
  NO_VALUE_KEY,
} from './boardGroupColumns';
import { boardFiltersFromSearchParams } from './projectView';

const byCategory: BoardGroup = { kind: 'field', key: 'category' };
const byImpact: BoardGroup = { kind: 'field', key: 'impact' };
const byAssignee: BoardGroup = { kind: 'assignee' };
const people = [ownerAsAdmin, memberAsEditor];

describe('boardGroupQuery', () => {
  it('只要沒結束的類別、不分類型與狀態，一次讀 200 個，帶著看板的條件', () => {
    // Arrange
    const filters = boardFiltersFromSearchParams(new URLSearchParams('view=board&group=assignee&priority=high&q=官網&field.category=bug'));

    // Act
    const query = boardGroupQuery(filters, '2026-11-19', 'monday');

    // Assert
    expect(query).toEqual({
      stateCategory: ['backlog', 'not_started', 'in_progress'],
      priority: ['high'],
      fields: { category: ['bug'] },
      q: '官網',
      sort: { field: 'number', direction: 'asc' },
      limit: BOARD_GROUP_PAGE_SIZE,
    });
    expect(query.type).toBeUndefined();
    expect(query.state).toBeUndefined();
  });
});

describe('groupColumns', () => {
  it('負責人：每位成員一欄，沒有項目也有，最後是「沒有負責人」', () => {
    // Arrange / Act
    const columns = groupColumns(byAssignee, agencyBlueprint, people, []);

    // Assert
    expect(columns.map((column) => column.name)).toEqual([exampleOwner.displayName, exampleMember.displayName, '沒有負責人']);
    expect(columns.at(-1)).toMatchObject({ key: NO_VALUE_KEY, isEmpty: true });
  });

  it('負責人：不是成員的負責人也有一欄，項目不會消失', () => {
    // Arrange
    const items = [agyItem({ assignee: { personId: 'gone', displayName: '離開的人' } })];

    // Act
    const columns = groupColumns(byAssignee, agencyBlueprint, people, items);

    // Assert
    expect(columns.map((column) => column.name)).toEqual([exampleOwner.displayName, exampleMember.displayName, '離開的人', '沒有負責人']);
  });

  it('單選欄位：只列有項目的選項，照選項的順序，最後是「沒有填」', () => {
    // Arrange
    const items = [
      smkItem({ number: 1, fields: { category: 'chore' } }),
      smkItem({ number: 2, fields: { category: 'feature' } }),
      smkItem({ number: 3 }),
    ];

    // Act
    const columns = groupColumns(byCategory, simpleTasksBlueprint, people, items);

    // Assert
    expect(columns.map((column) => [column.key, column.name])).toEqual([
      ['feature', '新功能'],
      ['chore', '雜務'],
      [NO_VALUE_KEY, '沒有填'],
    ]);
  });

  it('單選欄位：選項已經不在藍圖裡的項目照原本的值列出', () => {
    // Arrange
    const items = [smkItem({ fields: { category: 'removed' } })];

    // Act
    const columns = groupColumns(byCategory, simpleTasksBlueprint, people, items);

    // Assert
    expect(columns.map((column) => column.name)).toEqual(['removed', '沒有填']);
  });

  it('沒有項目的欄位只有「沒有填」', () => {
    expect(groupColumns(byCategory, simpleTasksBlueprint, people, []).map((column) => column.name)).toEqual(['沒有填']);
  });
});

describe('columnKeyOf', () => {
  it('依負責人、依欄位、沒有值', () => {
    expect(columnKeyOf(agyItem({ assignee: { personId: 'p1', displayName: 'A' } }), byAssignee)).toBe('p1');
    expect(columnKeyOf(agyItem({ assignee: null }), byAssignee)).toBe(NO_VALUE_KEY);
    expect(columnKeyOf(smkItem({ fields: { category: 'bug' } }), byCategory)).toBe('bug');
    expect(columnKeyOf(smkItem(), byCategory)).toBe(NO_VALUE_KEY);
    expect(columnKeyOf(smkItem({ fields: { category: ['bug'] } }), byCategory)).toBe(NO_VALUE_KEY);
  });
});

describe('dropBlockedReason', () => {
  it('類型沒有這個欄位的卡片不能放進選項的欄，寫出原因；放進「沒有填」可以', () => {
    // Arrange
    const requirement = agyItem({ type: 'requirement' });
    const change = agyItem({ type: 'change_request' });

    // Act / Assert
    expect(dropBlockedReason(requirement, byImpact, 'high', agencyBlueprint)).toBe('這個類型沒有「影響程度」');
    expect(dropBlockedReason(requirement, byImpact, NO_VALUE_KEY, agencyBlueprint)).toBeNull();
    expect(dropBlockedReason(change, byImpact, 'high', agencyBlueprint)).toBeNull();
    expect(dropBlockedReason(requirement, byAssignee, 'p1', agencyBlueprint)).toBeNull();
  });
});

describe('groupPatch 與 movedItem', () => {
  it('負責人：換人、清掉', () => {
    expect(groupPatch(byAssignee, 'p1')).toEqual({ assigneeId: 'p1' });
    expect(groupPatch(byAssignee, NO_VALUE_KEY)).toEqual({ assigneeId: null });
    expect(movedItem(agyItem(), byAssignee, exampleMember.personId, people).assignee).toEqual({
      personId: exampleMember.personId,
      displayName: exampleMember.displayName,
    });
    expect(movedItem(agyItem({ assignee: { personId: 'x', displayName: 'X' } }), byAssignee, NO_VALUE_KEY, people).assignee).toBeNull();
  });

  it('欄位：設成選項、清掉，不動別的欄位', () => {
    // Arrange
    const item = smkItem({ fields: { category: 'bug', summary: '摘要' } });

    // Act / Assert
    expect(groupPatch(byCategory, 'feature')).toEqual({ fields: { category: 'feature' } });
    expect(groupPatch(byCategory, NO_VALUE_KEY)).toEqual({ fields: { category: null } });
    expect(movedItem(item, byCategory, 'feature', people).fields).toEqual({ category: 'feature', summary: '摘要' });
    expect(movedItem(item, byCategory, NO_VALUE_KEY, people).fields).toEqual({ summary: '摘要' });
  });
});

describe('groupName', () => {
  it('負責人、欄位的名稱', () => {
    expect(groupName(byAssignee, agencyBlueprint)).toBe('負責人');
    expect(groupName(byCategory, simpleTasksBlueprint)).toBe('分類');
  });
});
