import { describe, expect, it } from 'vitest';
import type { SavedView } from '../api/types';
import { savedViewOf } from '../test/savedViews';
import { DEFAULT_MY_WORK_FILTERS, type MyWorkFilters } from './myWorkView';
import {
  defaultMyWorkViewName,
  hasMyWorkViewConditions,
  myWorkQueryOf,
  myWorkViewMatches,
  myWorkViewParams,
  type MyWorkNameContext,
  type MyWorkViewState,
} from './myWorkViews';
import { SAVED_VIEW_NAME_MAX_LENGTH } from './savedViews';

function state(
  overrides: Partial<MyWorkFilters> = {},
  layout: MyWorkViewState['layout'] = 'table',
  grouping: MyWorkViewState['grouping'] = 'urgency',
  sort: MyWorkViewState['sort'] = null,
): MyWorkViewState {
  return { layout, filters: { ...DEFAULT_MY_WORK_FILTERS, ...overrides }, grouping, sort };
}

function viewOf(query: Parameters<typeof savedViewOf>[1], layout: SavedView['layout'] = 'table'): SavedView {
  return savedViewOf('檢視', query, { layout });
}

const context: MyWorkNameContext = {
  types: [
    { key: 'bug', names: ['缺陷'], projectKeys: ['CM'] },
    { key: 'review', names: ['審稿', '設計審查'], projectKeys: ['CM', 'DS'] },
  ],
  projectStates: [{ key: 'waiting_client', name: '等客戶' }],
};

describe('條件轉成檢視的條件', () => {
  it('預設的條件全是 null：負責人存 null、狀態「未結束」不存類別', () => {
    expect(myWorkQueryOf(state())).toEqual({
      type: null, state: null, assignee: null, q: null, sort: null, due: null, priority: null,
      cycle: null, milestone: null, fields: null, category: null, project: null, group: null,
    });
  });

  it('狀態：所有狀態存 category=all，類別存類別，專案的某個狀態存 state 與 project', () => {
    expect(myWorkQueryOf(state({ status: { kind: 'all' } })).category).toBe('all');
    expect(myWorkQueryOf(state({ status: { kind: 'category', category: 'done' } })).category).toBe('done');
    const query = myWorkQueryOf(state({ status: { kind: 'state', state: 'waiting_client' }, project: 'CM' }));
    expect(query).toMatchObject({ state: 'waiting_client', project: 'CM', category: null });
  });

  it('沒有專案的狀態條件沒有意思，不存', () => {
    expect(myWorkQueryOf(state({ status: { kind: 'state', state: 'waiting_client' } })).state).toBeNull();
  });

  it('搜尋、類型、到期、優先度照存；週期、里程碑一律 null，沒有排序存 null', () => {
    const query = myWorkQueryOf(state({ q: '官網', type: 'bug', due: 'week', priority: 'high' }));
    expect(query).toMatchObject({ q: '官網', type: 'bug', due: 'week', priority: 'high', cycle: null, milestone: null, sort: null });
  });

  it('排序只有表格存「欄位:方向」，其他版面不存', () => {
    expect(myWorkQueryOf(state({}, 'table', 'urgency', { field: 'priority', direction: 'desc' })).sort).toBe('priority:desc');
    expect(myWorkQueryOf(state({}, 'table', 'urgency', { field: 'project', direction: 'asc' })).sort).toBe('project:asc');
    expect(myWorkQueryOf(state({}, 'board', 'urgency', { field: 'priority', direction: 'desc' })).sort).toBeNull();
  });

  it('group 只有表格依專案時是 project', () => {
    expect(myWorkQueryOf(state({}, 'table', 'project')).group).toBe('project');
    expect(myWorkQueryOf(state({}, 'table', 'urgency')).group).toBeNull();
    expect(myWorkQueryOf(state({}, 'board', 'project')).group).toBeNull();
  });
});

describe('套用檢視的網址', () => {
  it('預設條件的表格是空的網址', () => {
    expect(myWorkViewParams(viewOf({})).toString()).toBe('');
  });

  it('看板帶 view，條件照網址的寫法', () => {
    const params = myWorkViewParams(viewOf({ category: 'done', due: 'week', priority: 'high' }, 'board'));
    expect(params.toString()).toBe(new URLSearchParams('view=board&category=done&due=week&priority=high').toString());
  });

  it('舊檢視存的負責人不讀：套用出來沒有 assignee', () => {
    expect(myWorkViewParams(viewOf({ assignee: 'all', category: 'done' })).toString()).toBe('category=done');
  });

  it('表格依專案時帶 group=project，其他版面不帶', () => {
    expect(myWorkViewParams(viewOf({ group: 'project' })).get('group')).toBe('project');
    expect(myWorkViewParams(viewOf({ group: 'project' }, 'calendar')).has('group')).toBe(false);
  });

  it('表格帶 sort，其他版面不帶；預設（到期日遞增）、不認得的與網址一樣不寫', () => {
    expect(myWorkViewParams(viewOf({ sort: 'title:asc' })).get('sort')).toBe('title:asc');
    expect(myWorkViewParams(viewOf({ sort: 'title:asc' }, 'board')).has('sort')).toBe(false);
    expect(myWorkViewParams(viewOf({ sort: 'due_date:asc' })).has('sort')).toBe(false);
    expect(myWorkViewParams(viewOf({ sort: 'estimate:asc' })).has('sort')).toBe(false);
  });

  it('依專案分組時不帶依專案的排序（沒有專案欄，排了也看不出來）', () => {
    expect(myWorkViewParams(viewOf({ group: 'project', sort: 'project:asc' })).toString()).toBe('group=project');
    expect(myWorkQueryOf(state({}, 'table', 'project', { field: 'project', direction: 'asc' })).sort).toBeNull();
  });

  it('不認得的值與手打時一樣略過：沒有專案的狀態、壞的到期', () => {
    const params = myWorkViewParams(viewOf({ state: 'x', due: 'someday', category: 'weird' }));
    expect(params.toString()).toBe('');
  });

  it('有專案時狀態寫進網址，專案代號大寫', () => {
    const params = myWorkViewParams(viewOf({ project: 'cm', state: 'waiting_client' }));
    expect(params.toString()).toBe(new URLSearchParams('state=waiting_client&project=CM').toString());
  });
});

describe('目前是不是這個檢視', () => {
  it('版面與條件都一樣就是', () => {
    const view = viewOf({ category: 'done' }, 'board');
    expect(myWorkViewMatches(view, state({ status: { kind: 'category', category: 'done' } }, 'board'))).toBe(true);
  });

  it('版面不同、條件不同都不是', () => {
    const view = viewOf({ category: 'done' });
    expect(myWorkViewMatches(view, state({ status: { kind: 'category', category: 'done' } }, 'board'))).toBe(false);
    expect(myWorkViewMatches(view, state({ status: { kind: 'category', category: 'canceled' } }))).toBe(false);
  });

  it('只是存的值寫法不同、套用出來一樣的算一樣（略過的值）', () => {
    expect(myWorkViewMatches(viewOf({ due: 'someday' }), state())).toBe(true);
  });

  it('排序不同不是同一個檢視', () => {
    const view = viewOf({ sort: 'priority:desc' });
    expect(myWorkViewMatches(view, state())).toBe(false);
    expect(myWorkViewMatches(view, state({}, 'table', 'urgency', { field: 'priority', direction: 'desc' }))).toBe(true);
  });

  it('分組不同不是同一個檢視', () => {
    expect(myWorkViewMatches(viewOf({ group: 'project' }), state({}, 'table', 'urgency'))).toBe(false);
    expect(myWorkViewMatches(viewOf({ group: 'project' }), state({}, 'table', 'project'))).toBe(true);
  });
});

describe('目前有沒有條件', () => {
  it('預設的表格、看板都沒有條件', () => {
    expect(hasMyWorkViewConditions(state())).toBe(false);
    expect(hasMyWorkViewConditions(state({}, 'board'))).toBe(false);
  });

  it('有任何和預設不同的條件、或依專案分組就有', () => {
    expect(hasMyWorkViewConditions(state({ q: 'a' }))).toBe(true);
    expect(hasMyWorkViewConditions(state({}, 'table', 'project'))).toBe(true);
    expect(hasMyWorkViewConditions(state({}, 'table', 'urgency', { field: 'title', direction: 'asc' }))).toBe(true);
  });
});

describe('預設名稱', () => {
  it('沒有條件時是版面的名稱', () => {
    expect(defaultMyWorkViewName(state(), context)).toBe('表格');
    expect(defaultMyWorkViewName(state({}, 'timeline'), context)).toBe('時間軸');
  });

  it('看板有條件時前面加版面名稱', () => {
    expect(defaultMyWorkViewName(state({ due: 'week', priority: 'high' }, 'board'), context)).toBe('看板・本週到期・高');
  });

  it('表格有條件時不加版面名稱：完成', () => {
    expect(defaultMyWorkViewName(state({ status: { kind: 'category', category: 'done' } }), context)).toBe('完成');
  });

  it('專案與它的狀態名稱：CM・等客戶', () => {
    expect(defaultMyWorkViewName(state({ project: 'CM', status: { kind: 'state', state: 'waiting_client' } }), context)).toBe('CM・等客戶');
  });

  it('類型名稱用「／」接起來，找不到的用 key', () => {
    expect(defaultMyWorkViewName(state({ type: 'review' }), context)).toBe('審稿／設計審查');
    expect(defaultMyWorkViewName(state({ type: 'gone' }), context)).toBe('gone');
  });

  it('所有狀態、搜尋文字與依專案分組', () => {
    expect(defaultMyWorkViewName(state({ status: { kind: 'all' }, q: '官網' }), context)).toBe('所有狀態・「官網」');
    expect(defaultMyWorkViewName(state({}, 'table', 'project'), context)).toBe('依專案');
  });

  it('專案的狀態讀不到時用狀態 key', () => {
    const name = defaultMyWorkViewName(state({ project: 'CM', status: { kind: 'state', state: 'x' } }), { ...context, projectStates: null });
    expect(name).toBe('CM・x');
  });

  it('不超過名稱的長度上限', () => {
    const name = defaultMyWorkViewName(state({ q: '長'.repeat(80) }), context);
    expect(Array.from(name)).toHaveLength(SAVED_VIEW_NAME_MAX_LENGTH);
  });
});
