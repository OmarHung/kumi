import { STATE_GROUP } from './boardGroup';
import { describe, expect, it } from 'vitest';
import type { SavedView, SavedViewQuery } from '../api/types';
import { agyDetail, exampleMember, exampleOwner } from '../test/fixtures';
import { cycleOf, milestoneOf } from '../test/planning';
import { EMPTY_FILTERS } from './itemFilters';
import {
  defaultViewName,
  hasViewConditions,
  nameLength,
  queryFromBoardFilters,
  queryFromTableFilters,
  savedViewParams,
  viewMatches,
} from './savedViews';

const BLUEPRINT = agyDetail().blueprint;
const SPRINT_ID = '0190a000-0000-7000-8000-0000000000d1';
const V1_ID = '0190a000-0000-7000-8000-0000000000e1';
const NOTHING: SavedViewQuery = { type: null, state: null, assignee: null, q: null, sort: null, due: null, priority: null, cycle: null, milestone: null, fields: null, group: null, category: null, project: null, createdBy: null, mine: null };

function savedView(layout: SavedView['layout'], query: Partial<SavedViewQuery>): SavedView {
  return {
    id: 'v-1',
    name: '我本週要交的',
    layout,
    query: { ...NOTHING, ...query },
    createdAt: '2026-11-18T01:00:00Z',
    updatedAt: '2026-11-18T01:00:00Z',
  };
}

describe('從目前的條件取出要存的內容', () => {
  it('表格也存週期與里程碑：目前的週期存 current，不存編號', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, cycle: 'current', milestone: 'none' } as const;

    // Act
    const query = queryFromTableFilters(filters);

    // Assert
    expect(query).toEqual({ ...NOTHING, cycle: 'current', milestone: 'none' });
  });

  it('表格存類型、狀態、負責人、搜尋、排序、到期與優先度，空的搜尋存成 null', () => {
    // Arrange
    const filters = {
      ...EMPTY_FILTERS,
      type: 'task',
      assignee: 'me',
      createdBy: null,
      due: 'week',
      priority: 'high',
      sort: { field: 'due_date', direction: 'asc' },
    } as const;

    // Act
    const query = queryFromTableFilters(filters);

    // Assert
    expect(query).toEqual({ ...NOTHING, type: 'task', assignee: 'me', sort: 'due_date:asc', due: 'week', priority: 'high' });
  });

  it('看板只存看板有的條件', () => {
    // Arrange
    const filters = { workflow: 'task_flow', assignee: 'unassigned', createdBy: null, due: null, priority: 'low', q: '首頁', cycle: 'current', milestone: SPRINT_ID , fields: {}, group: STATE_GROUP} as const;

    // Act
    const query = queryFromBoardFilters(filters);

    // Assert
    expect(query).toEqual({ ...NOTHING, assignee: 'unassigned', q: '首頁', priority: 'low', cycle: 'current', milestone: SPRINT_ID });
  });
});

describe('建立者存進檢視', () => {
  it('表格與看板存 createdBy，mine 一律 null', () => {
    // Arrange
    const board = { workflow: null, assignee: null, createdBy: 'me', due: null, priority: null, q: '', cycle: null, milestone: null, fields: {}, group: STATE_GROUP } as const;

    // Act / Assert
    expect(queryFromTableFilters({ ...EMPTY_FILTERS, createdBy: 'p-2' })).toEqual({ ...NOTHING, createdBy: 'p-2' });
    expect(queryFromBoardFilters(board)).toEqual({ ...NOTHING, createdBy: 'me' });
  });

  it('套用時寫回網址（四種版面都帶）；舊的檢視沒有就是不限', () => {
    // Assert
    expect(savedViewParams(savedView('table', { createdBy: 'me' }), BLUEPRINT).toString()).toBe('createdBy=me');
    expect(savedViewParams(savedView('timeline', { createdBy: 'me' }), BLUEPRINT).toString()).toBe('view=timeline&createdBy=me');
    expect(savedViewParams(savedView('board', {}), BLUEPRINT).has('createdBy')).toBe(false);
  });

  it('舊的檢視在伺服器上根本沒有 createdBy 欄位（undefined）：套用的網址不會冒出 createdBy=undefined', () => {
    // Arrange：舊的 JSON 沒有這個鍵，型別上看不出來，所以拿掉鍵再斷言成目前的型別。
    const legacy = Object.fromEntries(Object.entries(NOTHING).filter(([name]) => name !== 'createdBy'));
    const view = { ...savedView('table', {}), query: legacy as SavedViewQuery };

    // Act
    const params = savedViewParams(view, BLUEPRINT);

    // Assert
    expect(params.toString()).toBe('');
    expect(params.toString()).not.toContain('undefined');
  });

  it('預設名稱：我建立的', () => {
    // Arrange
    const context = { blueprint: BLUEPRINT, cycles: [], milestones: [], members: [exampleMember], me: exampleOwner.personId };

    // Assert
    expect(defaultViewName('table', { ...NOTHING, createdBy: 'me' }, context)).toBe('我建立的');
  });
});

describe('行事曆與時間軸存類型與狀態', () => {
  it('queryFromBoardFilters：有給類型與狀態時存起來，沒給（看板）存 null', () => {
    // Arrange
    const filters = { workflow: null, assignee: null, createdBy: null, due: null, priority: null, q: '', cycle: null, milestone: null , fields: {}, group: STATE_GROUP} as const;

    // Act / Assert
    expect(queryFromBoardFilters({ ...filters, type: 'task', state: 'doing' })).toEqual({ ...NOTHING, type: 'task', state: 'doing' });
    expect(queryFromBoardFilters(filters)).toEqual(NOTHING);
  });

  it('套用時寫回網址：行事曆與時間軸帶類型與狀態，看板不帶；藍圖沒有的略過', () => {
    // Arrange
    const query = { type: 'task', state: 'doing' };

    // Act / Assert
    expect(savedViewParams(savedView('calendar', query), BLUEPRINT).toString()).toBe('view=calendar&type=task&state=doing');
    expect(savedViewParams(savedView('timeline', query), BLUEPRINT).toString()).toBe('view=timeline&type=task&state=doing');
    expect(savedViewParams(savedView('board', query), BLUEPRINT).toString()).toBe('view=board');
    expect(savedViewParams(savedView('calendar', { type: 'gone', state: 'gone' }), BLUEPRINT).toString()).toBe('view=calendar');
  });
});

describe('savedViewParams', () => {
  it('表格的檢視換成網址上的條件', () => {
    // Arrange
    const view = savedView('table', { type: 'task', assignee: 'me', createdBy: null, due: 'week', sort: 'title:desc' });

    // Act
    const params = savedViewParams(view, BLUEPRINT);

    // Assert
    expect(params.toString()).toBe(new URLSearchParams('type=task&assignee=me&due=week&sort=title:desc').toString());
  });

  it('看板的檢視帶 view=board', () => {
    // Arrange
    const view = savedView('board', { assignee: 'me', priority: 'urgent' });

    // Act
    const params = savedViewParams(view, BLUEPRINT);

    // Assert
    expect(params.toString()).toBe(new URLSearchParams('view=board&assignee=me&priority=urgent').toString());
  });

  it('行事曆與時間軸的檢視帶各自的 view', () => {
    // Arrange
    const query = { assignee: 'me', priority: 'urgent' };

    // Act / Assert
    expect(savedViewParams(savedView('calendar', query), BLUEPRINT).toString()).toBe(new URLSearchParams('view=calendar&assignee=me&priority=urgent').toString());
    expect(savedViewParams(savedView('timeline', query), BLUEPRINT).toString()).toBe(new URLSearchParams('view=timeline&assignee=me&priority=urgent').toString());
  });

  it('藍圖現在沒有的類型與狀態略過，不認得的到期與排序也略過', () => {
    // Arrange
    const view = savedView('table', { type: 'gone', state: 'gone', due: 'someday', sort: 'nope:asc', priority: 'high' });

    // Act
    const params = savedViewParams(view, BLUEPRINT);

    // Assert
    expect(params.toString()).toBe('priority=high');
  });

  it('狀態不在這個類型的流程裡也略過', () => {
    // Arrange
    const view = savedView('table', { type: 'task', state: 'developing' });

    // Act / Assert
    expect(savedViewParams(view, BLUEPRINT).toString()).toBe('type=task');
  });
});

describe('週期與里程碑的檢視', () => {
  it('換成網址上的 cycle 與 milestone，表格與看板都一樣', () => {
    // Arrange
    const query = { cycle: 'current', milestone: V1_ID };

    // Act / Assert
    expect(savedViewParams(savedView('table', query), BLUEPRINT).toString()).toBe(new URLSearchParams(`cycle=current&milestone=${V1_ID}`).toString());
    expect(savedViewParams(savedView('board', query), BLUEPRINT).toString()).toBe(
      new URLSearchParams(`view=board&cycle=current&milestone=${V1_ID}`).toString(),
    );
  });

  it('不認得的週期值略過', () => {
    // Arrange
    const view = savedView('table', { cycle: 'sprint-1', milestone: 'current' });

    // Act / Assert
    expect(savedViewParams(view, BLUEPRINT).toString()).toBe('');
  });

  it('週期不同就不是同一個檢視，有週期就算有條件', () => {
    // Arrange
    const view = savedView('table', { cycle: 'current' });

    // Act / Assert
    expect(viewMatches(view, { layout: 'table', query: { ...NOTHING, cycle: 'current' } }, BLUEPRINT)).toBe(true);
    expect(viewMatches(view, { layout: 'table', query: { ...NOTHING, cycle: 'none' } }, BLUEPRINT)).toBe(false);
    expect(hasViewConditions({ layout: 'table', query: { ...NOTHING, milestone: 'none' } }, BLUEPRINT)).toBe(true);
  });
});

describe('viewMatches', () => {
  it('版面與條件都一樣才算同一個檢視', () => {
    // Arrange
    const view = savedView('table', { due: 'week' });
    const same = { layout: 'table', query: { ...NOTHING, due: 'week' } } as const;

    // Act / Assert
    expect(viewMatches(view, same, BLUEPRINT)).toBe(true);
    expect(viewMatches(view, { ...same, layout: 'board' }, BLUEPRINT)).toBe(false);
    expect(viewMatches(view, { ...same, query: { ...NOTHING, due: 'today' } }, BLUEPRINT)).toBe(false);
  });

  it('排序「編號遞增」是預設，視同沒有排序', () => {
    // Arrange
    const view = savedView('table', { sort: 'number:asc', priority: 'high' });
    const current = { layout: 'table', query: { ...NOTHING, priority: 'high' } } as const;

    // Act / Assert
    expect(viewMatches(view, current, BLUEPRINT)).toBe(true);
    expect(viewMatches(savedView('table', { sort: 'number:desc' }), { layout: 'table', query: NOTHING }, BLUEPRINT)).toBe(false);
  });

  it('檢視裡藍圖沒有的類型不影響比對：套用之後的網址就是目前的條件', () => {
    // Arrange
    const view = savedView('table', { type: 'gone', priority: 'high' });
    const current = { layout: 'table', query: { ...NOTHING, priority: 'high' } } as const;

    // Act / Assert
    expect(viewMatches(view, current, BLUEPRINT)).toBe(true);
  });
});

describe('defaultViewName', () => {
  const context = {
    blueprint: BLUEPRINT,
    cycles: [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_ID })],
    milestones: [milestoneOf('v1.0 上線', '2026-12-20', { id: V1_ID })],
    members: [{ personId: exampleMember.personId, displayName: '範例成員', role: 'editor' }], me: exampleOwner.personId,
  } as const;

  it('用條件的簡短說法，中間用・隔開', () => {
    // Arrange
    const query = { ...NOTHING, due: 'week', priority: 'high' };

    // Act / Assert
    expect(defaultViewName('table', query, context)).toBe('本週到期・高');
  });

  it('類型、狀態、負責人與搜尋也寫進去', () => {
    // Arrange
    const query = { ...NOTHING, type: 'task', state: 'doing', assignee: 'me', q: '首頁' };

    // Act / Assert
    expect(defaultViewName('table', query, context)).toBe('任務・進行中・我・「首頁」');
    expect(defaultViewName('table', { ...NOTHING, assignee: exampleMember.personId }, context)).toBe('範例成員');
    expect(defaultViewName('table', { ...NOTHING, assignee: 'unassigned' }, context)).toBe('沒有負責人');
  });

  it('週期寫「目前的週期」或名稱，里程碑寫名稱；沒有的寫「沒有週期」「沒有里程碑」；刪掉的寫已刪除', () => {
    // Arrange / Act / Assert
    expect(defaultViewName('table', { ...NOTHING, cycle: 'current' }, context)).toBe('目前的週期');
    expect(defaultViewName('table', { ...NOTHING, cycle: SPRINT_ID, milestone: V1_ID }, context)).toBe('衝刺 2・v1.0 上線');
    expect(defaultViewName('table', { ...NOTHING, cycle: 'none', milestone: 'none' }, context)).toBe('沒有週期・沒有里程碑');
    expect(defaultViewName('table', { ...NOTHING, cycle: V1_ID }, context)).toBe('已刪除的週期');
  });

  it('沒有條件時表格是「表格」、看板是「看板」', () => {
    // Arrange / Act / Assert
    expect(defaultViewName('table', NOTHING, context)).toBe('表格');
    expect(defaultViewName('board', NOTHING, context)).toBe('看板');
  });

  it('行事曆與時間軸和看板一樣：沒有條件是版面的名稱，有條件前面加版面的名稱', () => {
    // Arrange / Act / Assert
    expect(defaultViewName('calendar', NOTHING, context)).toBe('行事曆');
    expect(defaultViewName('timeline', { ...NOTHING, due: 'week' }, context)).toBe('時間軸・本週到期');
  });

  it('優先度沒有設定時寫「優先度未設定」，和下拉、項目上的「未設定」一致', () => {
    // Arrange / Act / Assert
    expect(defaultViewName('table', { ...NOTHING, priority: 'none' }, context)).toBe('優先度未設定');
  });

  it('看板的條件前面加上「看板」，名稱最多 50 個字', () => {
    // Arrange
    const long = { ...NOTHING, q: 'x'.repeat(80) };

    // Act
    const name = defaultViewName('board', { ...NOTHING, due: 'today' }, context);

    // Assert
    expect(name).toBe('看板・今天到期');
    expect(nameLength(defaultViewName('table', long, context))).toBe(50);
  });
});

describe('hasViewConditions', () => {
  it('沒篩選、沒搜尋、預設排序（含編號遞增）時沒有條件；任何一項都算', () => {
    // Arrange / Act / Assert
    expect(hasViewConditions({ layout: 'table', query: NOTHING }, BLUEPRINT)).toBe(false);
    expect(hasViewConditions({ layout: 'table', query: { ...NOTHING, sort: 'number:asc' } }, BLUEPRINT)).toBe(false);
    expect(hasViewConditions({ layout: 'board', query: NOTHING }, BLUEPRINT)).toBe(false);
    expect(hasViewConditions({ layout: 'table', query: { ...NOTHING, q: '首頁' } }, BLUEPRINT)).toBe(true);
    expect(hasViewConditions({ layout: 'table', query: { ...NOTHING, sort: 'title:asc' } }, BLUEPRINT)).toBe(true);
    expect(hasViewConditions({ layout: 'board', query: { ...NOTHING, priority: 'none' } }, BLUEPRINT)).toBe(true);
  });
});
