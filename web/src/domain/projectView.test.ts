import { STATE_GROUP } from './boardGroup';
import { describe, expect, it } from 'vitest';
import { exampleOwner, smkItem } from '../test/fixtures';
import {
  matchesBoardFilters,
  boardColumnQuery,
  boardFiltersFromSearchParams,
  boardFiltersToSearchParams,
  dateViewFiltersFromSearchParams,
  hasActiveBoardFilters,
  switchViewParams,
  viewFromSearchParams,
} from './projectView';

const TODAY = '2026-11-18';

describe('viewFromSearchParams', () => {
  it('view=board 是看板，其他（沒給、打錯）都是表格', () => {
    // Arrange / Act / Assert
    expect(viewFromSearchParams(new URLSearchParams('view=board'))).toBe('board');
    expect(viewFromSearchParams(new URLSearchParams(''))).toBe('table');
    expect(viewFromSearchParams(new URLSearchParams('view=kanban'))).toBe('table');
  });
});

describe('四種版面', () => {
  it('view=calendar、view=timeline 各是行事曆與時間軸', () => {
    // Arrange / Act / Assert
    expect(viewFromSearchParams(new URLSearchParams('view=calendar'))).toBe('calendar');
    expect(viewFromSearchParams(new URLSearchParams('view=timeline'))).toBe('timeline');
  });

  it('切到行事曆或時間軸：帶類型與狀態（表格有的篩選），月份與刻度不帶', () => {
    // Arrange
    const params = new URLSearchParams('view=calendar&month=2026-12&type=task&state=doing&assignee=me&priority=high');

    // Act
    const calendar = switchViewParams(params, 'calendar');
    const timeline = switchViewParams(params, 'timeline');
    const table = switchViewParams(params, 'table');

    // Assert
    expect(calendar.toString()).toBe(new URLSearchParams('view=calendar&type=task&state=doing&assignee=me&priority=high').toString());
    expect(timeline.toString()).toBe(new URLSearchParams('view=timeline&type=task&state=doing&assignee=me&priority=high').toString());
    expect(table.toString()).toBe(new URLSearchParams('type=task&state=doing&assignee=me&priority=high').toString());
  });

  it('去看板時才丟掉類型與狀態（看板依狀態分欄）', () => {
    // Arrange
    const params = new URLSearchParams('view=timeline&type=task&state=doing&assignee=me');

    // Act
    const board = switchViewParams(params, 'board');

    // Assert
    expect(board.toString()).toBe(new URLSearchParams('view=board&assignee=me').toString());
  });

  it('行事曆與時間軸的網址讀得出類型與狀態、寫得回去；看板的網址上有也不讀', () => {
    // Arrange
    const calendar = new URLSearchParams('view=calendar&type=task&state=doing');
    const board = new URLSearchParams('view=board&type=task&state=doing');

    // Act
    const filters = dateViewFiltersFromSearchParams(calendar);

    // Assert
    expect(filters).toMatchObject({ type: 'task', state: 'doing' });
    expect(boardFiltersToSearchParams(filters, undefined, 'timeline').toString()).toBe('view=timeline&type=task&state=doing');
    expect(boardFiltersToSearchParams(filters, undefined, 'board').toString()).toBe('view=board');
    expect(dateViewFiltersFromSearchParams(board)).toMatchObject({ type: null, state: null });
  });

  it('類型與狀態也算條件（行事曆與時間軸的「清除條件」）', () => {
    // Arrange
    const base = dateViewFiltersFromSearchParams(new URLSearchParams('view=calendar'));

    // Act / Assert
    expect(hasActiveBoardFilters(base)).toBe(false);
    expect(hasActiveBoardFilters({ ...base, type: 'task' })).toBe(true);
    expect(hasActiveBoardFilters({ ...base, state: 'doing' })).toBe(true);
  });

  it('條件寫回網址時帶著目前的版面，行事曆不寫流程', () => {
    // Arrange
    const filters = { workflow: 'task_flow', assignee: 'me', createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' };

    // Act / Assert
    expect(boardFiltersToSearchParams(filters, undefined, 'calendar').toString()).toBe('view=calendar&assignee=me');
    expect(boardFiltersToSearchParams(filters, undefined, 'board').toString()).toBe('view=board&workflow=task_flow&assignee=me');
  });
});

describe('switchViewParams', () => {
  it('從表格切到看板：留下搜尋與負責人，類型、狀態、排序是表格自己的', () => {
    // Arrange
    const params = new URLSearchParams('type=task&state=doing&assignee=me&q=首頁&sort=due_date:desc');

    // Act
    const next = switchViewParams(params, 'board');

    // Assert
    expect(next.toString()).toBe(new URLSearchParams('view=board&q=首頁&assignee=me').toString());
  });

  it('從看板切回表格：拿掉 view 與流程，留下搜尋與負責人', () => {
    // Arrange
    const params = new URLSearchParams('view=board&workflow=task_flow&assignee=unassigned');

    // Act
    const next = switchViewParams(params, 'table');

    // Assert
    expect(next.toString()).toBe('assignee=unassigned');
  });

  it('自訂欄位的條件在每一種版面之間都帶著', () => {
    // Arrange
    const params = new URLSearchParams('field.client=client_a&field.billable=true&q=首頁');

    // Act / Assert
    for (const view of ['board', 'calendar', 'timeline', 'table'] as const) {
      const next = switchViewParams(params, view);
      expect(next.get('field.client')).toBe('client_a');
      expect(next.get('field.billable')).toBe('true');
    }
  });

  it('group 只屬於看板，切到其他版面不帶', () => {
    // Arrange
    const params = new URLSearchParams('view=board&group=assignee&field.client=client_a');

    // Act
    const next = switchViewParams(params, 'table');

    // Assert
    expect(next.toString()).toBe('field.client=client_a');
  });
});

describe('看板的條件', () => {
  it('從網址讀出流程、負責人與搜尋，空白的當成沒給', () => {
    // Arrange
    const params = new URLSearchParams('view=board&workflow=delivery&assignee=me&q=%20%20官網%20');

    // Act
    const filters = boardFiltersFromSearchParams(params);

    // Assert
    expect(filters).toEqual({ workflow: 'delivery', assignee: 'me', createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '官網' });
    expect(boardFiltersFromSearchParams(new URLSearchParams('workflow=%20'))).toEqual({ workflow: null, assignee: null, createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' });
  });

  it('寫回網址時一定帶 view=board；選的是預設的流程就不寫', () => {
    // Arrange
    const filters = { workflow: 'delivery', assignee: 'me', createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '官網' };

    // Act
    const atDefault = boardFiltersToSearchParams(filters, 'delivery');
    const other = boardFiltersToSearchParams(filters, 'task_flow');

    // Assert
    expect(atDefault.toString()).toBe(new URLSearchParams('view=board&assignee=me&q=官網').toString());
    expect(other.toString()).toBe(new URLSearchParams('view=board&workflow=delivery&assignee=me&q=官網').toString());
  });

  it('只有搜尋與負責人算篩選；換流程只是換一個看板', () => {
    // Arrange / Act / Assert
    expect(hasActiveBoardFilters({ workflow: 'task_flow', assignee: null, createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' })).toBe(false);
    expect(hasActiveBoardFilters({ workflow: null, assignee: 'me', createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' })).toBe(true);
    expect(hasActiveBoardFilters({ workflow: null, assignee: null, createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '首頁' })).toBe(true);
  });

  it('每一欄的條件：這個狀態、這個流程的類型、照編號遞增、一頁 50 個，再加上搜尋與負責人', () => {
    // Arrange
    const filters = { workflow: null, assignee: 'unassigned', createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '官網' };

    // Act
    const query = boardColumnQuery(filters, ['requirement', 'change_request'], 'quoting', TODAY, 'monday');

    // Assert
    expect(query).toEqual({
      type: ['requirement', 'change_request'],
      state: ['quoting'],
      assignee: 'unassigned',
      q: '官網',
      sort: { field: 'number', direction: 'asc' },
      limit: 50,
    });
    expect(boardColumnQuery({ workflow: null, assignee: null, createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' }, ['task'], 'todo', TODAY, 'monday')).toEqual({
      type: ['task'],
      state: ['todo'],
      sort: { field: 'number', direction: 'asc' },
      limit: 50,
    });
  });
});

describe('建立者在四種版面的網址上', () => {
  it('看板、行事曆與時間軸讀得到，寫回網址時帶上，也算篩選', () => {
    // Arrange
    const params = new URLSearchParams('view=calendar&createdBy=me');

    // Act
    const filters = boardFiltersFromSearchParams(params);

    // Assert
    expect(filters.createdBy).toBe('me');
    expect(boardFiltersToSearchParams(filters, undefined, 'calendar').toString()).toBe(new URLSearchParams('view=calendar&createdBy=me').toString());
    expect(hasActiveBoardFilters(filters)).toBe(true);
  });

  it('每一欄的條件帶 createdBy', () => {
    // Arrange
    const filters = { ...boardFiltersFromSearchParams(new URLSearchParams('createdBy=p-9')) };

    // Act
    const query = boardColumnQuery(filters, ['task'], 'todo', TODAY, 'monday');

    // Assert
    expect(query.createdBy).toBe('p-9');
  });

  it('切換版面時建立者跟著帶過去', () => {
    // Act
    const next = switchViewParams(new URLSearchParams('createdBy=me&assignee=me'), 'board');

    // Assert
    expect(next.get('createdBy')).toBe('me');
  });
});

describe('matchesBoardFilters', () => {
  const me = exampleOwner.personId;
  const filters = (assignee: string | null, q = '', createdBy: string | null = null) => ({ workflow: null, assignee, createdBy, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q });

  it('負責人：「我」、「沒有負責人」、指定的人；沒選就是所有人', () => {
    // Arrange
    const mine = smkItem({ number: 1 });
    const nobody = smkItem({ number: 2, assignee: null });

    // Act / Assert
    expect(matchesBoardFilters(mine, filters(null), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(mine, filters('me'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(nobody, filters('me'), me, TODAY, 'monday')).toBe(false);
    expect(matchesBoardFilters(nobody, filters('unassigned'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(mine, filters('unassigned'), me, TODAY, 'monday')).toBe(false);
    expect(matchesBoardFilters(mine, filters(me), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(mine, filters('someone-else'), me, TODAY, 'monday')).toBe(false);
  });

  it('建立者：「我」、指定的人；沒選就是所有人，和負責人可以一起用', () => {
    // Arrange
    const mine = smkItem({ number: 1 });
    const byOther = smkItem({ number: 2, createdBy: { personId: 'someone-else', displayName: '另一位' } });

    // Act / Assert
    expect(matchesBoardFilters(byOther, filters(null), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(mine, filters(null, '', 'me'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(byOther, filters(null, '', 'me'), me, TODAY, 'monday')).toBe(false);
    expect(matchesBoardFilters(byOther, filters(null, '', 'someone-else'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(mine, filters('me', '', 'someone-else'), me, TODAY, 'monday')).toBe(false);
  });

  it('搜尋和後端一樣：標題包含這段文字（不分大小寫），或就是這個項目的編號', () => {
    // Arrange
    const item = smkItem({ number: 7, title: 'Landing 首頁設計稿' });

    // Act / Assert
    expect(matchesBoardFilters(item, filters(null, '首頁'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(item, filters(null, 'landing'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(item, filters(null, 'SMK-7'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(item, filters(null, 'smk-07'), me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(item, filters(null, 'SMK-8'), me, TODAY, 'monday')).toBe(false);
    expect(matchesBoardFilters(item, filters(null, 'AGY-7'), me, TODAY, 'monday')).toBe(false);
    expect(matchesBoardFilters(item, filters(null, '報價'), me, TODAY, 'monday')).toBe(false);
  });
});
