import { describe, expect, it } from 'vitest';
import type { Item } from '../api/types';
import {
  DEFAULT_MY_WORK_FILTERS,
  hasActiveMyWorkFilters,
  matchesMyWorkFilters,
  myWorkConditionCount,
  myWorkFilterEntries,
  myWorkConditions,
  myWorkFiltersFromParams,
  myWorkLayoutOf,
  myWorkSearchParams,
  statusCategories,
  switchMyWorkLayoutParams,
  type MyWorkFilters,
} from './myWorkView';

const ME = '0190a000-0000-7000-8000-000000000001';
const OTHER = '0190a000-0000-7000-8000-000000000002';
const TODAY = '2026-11-19';

function params(text: string): URLSearchParams {
  return new URLSearchParams(text);
}

function item(overrides: Partial<Item> = {}): Item {
  return {
    key: 'AGY-3',
    projectKey: 'AGY',
    number: 3,
    type: 'task',
    title: '首頁改版',
    state: 'doing',
    stateCategory: 'in_progress',
    assignee: { personId: ME, displayName: '我' },
    createdBy: { personId: ME, displayName: '我' },
    priority: 'high',
    startDate: null,
    dueDate: '2026-11-20',
    estimate: null,
    parentKey: null,
    cycleId: null,
    milestoneId: null,
    fields: {},
    createdAt: '2026-11-01T00:00:00Z',
    updatedAt: '2026-11-01T00:00:00Z',
    ...overrides,
  };
}

describe('myWorkFiltersFromParams', () => {
  it('沒有參數時是預設：我負責、還沒結束的', () => {
    // Act
    const filters = myWorkFiltersFromParams(params(''));

    // Assert
    expect(filters).toEqual(DEFAULT_MY_WORK_FILTERS);
    expect(filters.status).toEqual({ kind: 'open' });
  });

  it('讀得到所有條件，專案代號換成大寫', () => {
    // Act
    const filters = myWorkFiltersFromParams(params(`q=首頁&type=task&category=done&due=week&priority=high&project=agy`));

    // Assert
    expect(filters).toEqual({
      q: '首頁',
      type: 'task',
      status: { kind: 'category', category: 'done' },
      due: 'week',
      priority: 'high',
      project: 'AGY',
      mine: 'assigned',
    });
  });

  it('category=all 是所有狀態；不認得的類別當成預設', () => {
    // Assert
    expect(myWorkFiltersFromParams(params('category=all')).status).toEqual({ kind: 'all' });
    expect(myWorkFiltersFromParams(params('category=nope')).status).toEqual({ kind: 'open' });
  });

  it('狀態只有在選了專案時才算：各專案的流程不同，沒有專案時略過', () => {
    // Assert
    expect(myWorkFiltersFromParams(params('state=waiting_client')).status).toEqual({ kind: 'open' });
    expect(myWorkFiltersFromParams(params('state=waiting_client&project=CM')).status).toEqual({ kind: 'state', state: 'waiting_client' });
  });

  it('網址上的 assignee 一律忽略：我的工作沒有負責人的條件', () => {
    // Assert
    expect(myWorkFiltersFromParams(params('assignee=all'))).toEqual(DEFAULT_MY_WORK_FILTERS);
    expect(myWorkFiltersFromParams(params(`assignee=${OTHER}`))).toEqual(DEFAULT_MY_WORK_FILTERS);
    expect(myWorkFilterEntries(myWorkFiltersFromParams(params('assignee=unassigned')))).toEqual([]);
  });

  it('不認得的到期、優先度略過', () => {
    // Act
    const filters = myWorkFiltersFromParams(params('due=someday&priority=extreme'));

    // Assert
    expect(filters.due).toBeNull();
    expect(filters.priority).toBeNull();
  });
});

describe('我負責的／我建立的', () => {
  it('mine=created 讀成我建立的，沒給或不認得是我負責的', () => {
    // Assert
    expect(myWorkFiltersFromParams(params('mine=created')).mine).toBe('created');
    expect(myWorkFiltersFromParams(params('')).mine).toBe('assigned');
    expect(myWorkFiltersFromParams(params('mine=everyone')).mine).toBe('assigned');
  });

  it('只有我建立的才寫進網址；它不算條件（清除條件留著它）', () => {
    // Arrange
    const created: MyWorkFilters = { ...DEFAULT_MY_WORK_FILTERS, mine: 'created' };

    // Assert
    expect(myWorkSearchParams('table', created).toString()).toBe('mine=created');
    expect(myWorkSearchParams('table', DEFAULT_MY_WORK_FILTERS).toString()).toBe('');
    expect(hasActiveMyWorkFilters(created)).toBe(false);
    expect(hasActiveMyWorkFilters({ ...created, due: 'week' })).toBe(true);
    expect(myWorkConditionCount({ ...created, due: 'week' })).toBe(1);
  });

  it('我建立的送 createdBy=me、不送 assignee', () => {
    // Act
    const query = myWorkConditions({ ...DEFAULT_MY_WORK_FILTERS, mine: 'created' }, TODAY, 'monday');

    // Assert
    expect(query).toEqual({ createdBy: 'me', stateCategory: ['backlog', 'not_started', 'in_progress'] });
  });

  it('我建立的比對建立者，不看負責人', () => {
    // Arrange
    const created: MyWorkFilters = { ...DEFAULT_MY_WORK_FILTERS, mine: 'created' };
    const byMeForOther = item({ assignee: { personId: OTHER, displayName: '別人' } });
    const byOther = item({ createdBy: { personId: OTHER, displayName: '別人' } });

    // Assert
    expect(matchesMyWorkFilters(byMeForOther, created, ME, TODAY, 'monday')).toBe(true);
    expect(matchesMyWorkFilters(byOther, created, ME, TODAY, 'monday')).toBe(false);
    expect(matchesMyWorkFilters(byMeForOther, DEFAULT_MY_WORK_FILTERS, ME, TODAY, 'monday')).toBe(false);
  });
});

describe('myWorkSearchParams', () => {
  it('預設的條件不寫進網址，表格不寫 view', () => {
    // Assert
    expect(myWorkSearchParams('table', DEFAULT_MY_WORK_FILTERS).toString()).toBe('');
    expect(myWorkSearchParams('board', DEFAULT_MY_WORK_FILTERS).toString()).toBe('view=board');
  });

  it('讀回來和原本一樣', () => {
    // Arrange
    const filters: MyWorkFilters = {
      q: 'API',
      type: 'bug',
      status: { kind: 'state', state: 'waiting_client' },
      due: 'overdue',
      priority: 'urgent',
      project: 'CM',
      mine: 'assigned',
    };

    // Act
    const written = myWorkSearchParams('calendar', filters);

    // Assert
    expect(myWorkLayoutOf(written)).toBe('calendar');
    expect(myWorkFiltersFromParams(written)).toEqual(filters);
  });
});

describe('switchMyWorkLayoutParams', () => {
  it('換版面時條件、分組與排序留著，行事曆與時間軸自己的位置不帶', () => {
    // Act
    const next = switchMyWorkLayoutParams(params('view=calendar&month=2026-12&type=bug&group=project&sort=priority:desc'), 'timeline');

    // Assert
    expect(next.get('view')).toBe('timeline');
    expect(next.get('type')).toBe('bug');
    expect(next.get('group')).toBe('project');
    expect(next.get('sort')).toBe('priority:desc');
    expect(next.has('month')).toBe(false);
  });

  it('換回表格不寫 view', () => {
    // Assert
    expect(switchMyWorkLayoutParams(params('view=board&q=x'), 'table').toString()).toBe('q=x');
  });
});

describe('hasActiveMyWorkFilters', () => {
  it('預設不算有條件；改了任何一個就算', () => {
    // Assert
    expect(hasActiveMyWorkFilters(DEFAULT_MY_WORK_FILTERS)).toBe(false);
    expect(hasActiveMyWorkFilters({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'all' } })).toBe(true);
    expect(hasActiveMyWorkFilters({ ...DEFAULT_MY_WORK_FILTERS, project: 'AGY' })).toBe(true);
  });
});

describe('statusCategories', () => {
  it('還沒結束的是三個類別；所有狀態與單一狀態不限類別', () => {
    // Assert
    expect(statusCategories({ kind: 'open' })).toEqual(['backlog', 'not_started', 'in_progress']);
    expect(statusCategories({ kind: 'all' })).toBeNull();
    expect(statusCategories({ kind: 'category', category: 'done' })).toEqual(['done']);
    expect(statusCategories({ kind: 'state', state: 'doing' })).toBeNull();
  });
});

describe('myWorkConditions', () => {
  it('預設：我負責、還沒結束的', () => {
    // Act
    const query = myWorkConditions(DEFAULT_MY_WORK_FILTERS, TODAY, 'monday');

    // Assert
    expect(query).toEqual({ assignee: 'me', stateCategory: ['backlog', 'not_started', 'in_progress'] });
  });

  it('所有狀態時不帶類別，負責人仍是我', () => {
    // Act
    const query = myWorkConditions({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'all' } }, TODAY, 'monday');

    // Assert
    expect(query).toEqual({ assignee: 'me' });
  });

  it('帶上類型、狀態、優先度、搜尋與專案', () => {
    // Act
    const query = myWorkConditions(
      { q: 'API', type: 'bug', status: { kind: 'state', state: 'waiting_client' }, due: null, priority: 'high', project: 'CM', mine: 'assigned' },
      TODAY,
      'monday',
    );

    // Assert
    expect(query).toEqual({ type: ['bug'], state: ['waiting_client'], assignee: 'me', priority: ['high'], q: 'API', project: ['CM'] });
  });

  it('已過期本身只算還沒結束的：和狀態類別取交集', () => {
    // Act
    const query = myWorkConditions({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'all' }, due: 'overdue' }, TODAY, 'monday');

    // Assert
    expect(query).toEqual({ assignee: 'me', dueTo: '2026-11-18', stateCategory: ['backlog', 'not_started', 'in_progress'] });
  });

  it('已完成加上已過期一定是空的：回 null，不必送請求', () => {
    // Act
    const query = myWorkConditions({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'category', category: 'done' }, due: 'overdue' }, TODAY, 'monday');

    // Assert
    expect(query).toBeNull();
  });
});

describe('matchesMyWorkFilters', () => {
  it('預設：我負責、還沒結束的才符合', () => {
    // Assert
    expect(matchesMyWorkFilters(item(), DEFAULT_MY_WORK_FILTERS, ME, TODAY, 'monday')).toBe(true);
    expect(matchesMyWorkFilters(item({ stateCategory: 'done' }), DEFAULT_MY_WORK_FILTERS, ME, TODAY, 'monday')).toBe(false);
    expect(matchesMyWorkFilters(item({ assignee: { personId: OTHER, displayName: '別人' } }), DEFAULT_MY_WORK_FILTERS, ME, TODAY, 'monday')).toBe(false);
  });

  it('沒有負責人的不符合：永遠只比對自己', () => {
    // Assert
    expect(matchesMyWorkFilters(item({ assignee: null }), DEFAULT_MY_WORK_FILTERS, ME, TODAY, 'monday')).toBe(false);
  });

  it('類型、狀態、優先度、到期、專案、搜尋都要符合', () => {
    // Arrange
    const filters: MyWorkFilters = {
      q: 'agy-3',
      type: 'task',
      status: { kind: 'state', state: 'doing' },
      due: 'week',
      priority: 'high',
      project: 'AGY',
      mine: 'assigned',
    };

    // Assert
    expect(matchesMyWorkFilters(item(), filters, ME, TODAY, 'monday')).toBe(true);
    expect(matchesMyWorkFilters(item({ projectKey: 'CM' }), filters, ME, TODAY, 'monday')).toBe(false);
    expect(matchesMyWorkFilters(item({ state: 'todo' }), filters, ME, TODAY, 'monday')).toBe(false);
    expect(matchesMyWorkFilters(item({ dueDate: '2026-12-31' }), filters, ME, TODAY, 'monday')).toBe(false);
  });
});
