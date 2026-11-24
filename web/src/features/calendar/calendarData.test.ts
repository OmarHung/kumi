import { describe, expect, it } from 'vitest';
import { smkItem } from '../../test/fixtures';
import { cycleOf, milestoneOf } from '../../test/planning';
import { dateViewFiltersFromSearchParams } from '../../domain/projectView';
import { markersByDay, noDueQuery, itemsByDay, visibleItemsQuery, visibleSpanQuery } from './calendarData';
import { monthGrid } from './calendarMonth';

const TODAY = '2026-11-18';
const GRID = monthGrid('2026-11', 'monday');
const NO_FILTERS = dateViewFiltersFromSearchParams(new URLSearchParams('view=calendar'));

describe('itemsByDay', () => {
  it('項目排在到期日那一天，每一天依優先度、再依編號；沒有到期日的不排', () => {
    // Arrange
    const items = [
      smkItem({ number: 3, dueDate: '2026-11-18', priority: 'low' }),
      smkItem({ number: 5, dueDate: '2026-11-18', priority: 'urgent' }),
      smkItem({ number: 2, dueDate: '2026-11-18', priority: 'low' }),
      smkItem({ number: 4, dueDate: '2026-11-19', priority: 'none' }),
      smkItem({ number: 6, dueDate: null }),
    ];

    // Act
    const byDay = itemsByDay(items);

    // Assert
    expect(byDay.get('2026-11-18')?.map((item) => item.number)).toEqual([5, 2, 3]);
    expect(byDay.get('2026-11-19')?.map((item) => item.number)).toEqual([4]);
    expect(byDay.size).toBe(2);
  });

  it('優先度沒有設定的排在最後', () => {
    // Arrange
    const items = [
      smkItem({ number: 1, dueDate: '2026-11-18', priority: 'none' }),
      smkItem({ number: 2, dueDate: '2026-11-18', priority: 'medium' }),
    ];

    // Act / Assert
    expect(itemsByDay(items).get('2026-11-18')?.map((item) => item.number)).toEqual([2, 1]);
  });
});

describe('markersByDay', () => {
  it('里程碑在到期日，週期在開始日與結束日', () => {
    // Arrange
    const cycles = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29')];
    const milestones = [milestoneOf('v1.0 上線', '2026-11-20')];

    // Act
    const markers = markersByDay(cycles, milestones);

    // Assert
    expect(markers.get('2026-11-16')?.cycleStarts.map((cycle) => cycle.name)).toEqual(['衝刺 2']);
    expect(markers.get('2026-11-29')?.cycleEnds.map((cycle) => cycle.name)).toEqual(['衝刺 2']);
    expect(markers.get('2026-11-20')?.milestones.map((milestone) => milestone.name)).toEqual(['v1.0 上線']);
  });
});

describe('visibleItemsQuery', () => {
  it('讀畫面上看得到的那幾週，依到期日排，一頁 200 筆', () => {
    // Arrange / Act
    const query = visibleItemsQuery(NO_FILTERS, GRID, TODAY, 'monday', []);

    // Assert
    expect(query).toEqual({ dueFrom: '2026-10-26', dueTo: '2026-12-06', sort: { field: 'due_date', direction: 'asc' }, limit: 200 });
  });

  it('工具列的條件一起帶；到期條件和畫面的範圍取交集', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('view=calendar&assignee=me&priority=high&due=week&q=首頁'));

    // Act
    const query = visibleItemsQuery(filters, GRID, TODAY, 'monday', []);

    // Assert
    expect(query).toMatchObject({ assignee: 'me', priority: ['high'], q: '首頁', dueFrom: '2026-11-18', dueTo: '2026-11-22' });
  });

  it('一週從週日開始：讀的範圍跟著格子走，「本週到期」到這週的週六', () => {
    // Arrange
    const grid = monthGrid('2026-11', 'sunday');
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('view=calendar&due=week'));

    // Act
    const all = visibleItemsQuery(NO_FILTERS, grid, TODAY, 'sunday', []);
    const week = visibleItemsQuery(filters, grid, TODAY, 'sunday', []);

    // Assert
    expect(all).toMatchObject({ dueFrom: '2026-11-01', dueTo: '2026-12-05' });
    expect(week).toMatchObject({ dueFrom: '2026-11-18', dueTo: '2026-11-21' });
  });

  it('到期條件落在畫面之外時不用讀（null）；「沒有到期日」也不在月曆上', () => {
    // Arrange
    const future = monthGrid('2027-03', 'monday');

    // Act / Assert
    expect(visibleItemsQuery(dateViewFiltersFromSearchParams(new URLSearchParams('due=week')), future, TODAY, 'monday', [])).toBeNull();
    expect(visibleItemsQuery(dateViewFiltersFromSearchParams(new URLSearchParams('due=none')), GRID, TODAY, 'monday', [])).toBeNull();
  });

  it('「已過期」只看還沒完成的，到畫面最前面一天為止的取交集', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('due=overdue'));

    // Act
    const query = visibleItemsQuery(filters, GRID, TODAY, 'monday', []);

    // Assert
    expect(query).toMatchObject({ dueFrom: '2026-10-26', dueTo: '2026-11-17', stateCategory: ['backlog', 'not_started', 'in_progress'] });
  });

  it('週期是「目前的週期」時換成編號', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('cycle=current'));
    const cycles = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: 'c-2' })];

    // Act / Assert
    expect(visibleItemsQuery(filters, GRID, TODAY, 'monday', cycles)).toMatchObject({ cycleId: 'c-2' });
  });
});

describe('類型與狀態', () => {
  it('visibleItemsQuery 與 noDueQuery 都帶類型與狀態', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('view=calendar&type=task&state=doing'));

    // Act / Assert
    expect(visibleItemsQuery(filters, GRID, TODAY, 'monday', [])).toMatchObject({ type: ['task'], state: ['doing'] });
    expect(noDueQuery(filters, TODAY, [])).toEqual({ type: ['task'], state: ['doing'], noDue: true, limit: 200 });
  });
});

describe('noDueQuery', () => {
  it('同樣的條件，只看沒有到期日的；到期條件不是「沒有到期日」時沒有這回事（null）', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('assignee=me'));

    // Act / Assert
    expect(noDueQuery(filters, TODAY, [])).toEqual({ assignee: 'me', noDue: true, limit: 200 });
    expect(noDueQuery(dateViewFiltersFromSearchParams(new URLSearchParams('due=today')), TODAY, [])).toBeNull();
    expect(noDueQuery(dateViewFiltersFromSearchParams(new URLSearchParams('due=none')), TODAY, [])).toEqual({ noDue: true, limit: 200 });
  });
});

describe('visibleSpanQuery：期間排法', () => {
  it('讀和畫面那幾週期間重疊的（datesFrom、datesTo），依編號排，一頁 200 筆；不帶到期日的範圍', () => {
    // Arrange / Act
    const query = visibleSpanQuery(NO_FILTERS, GRID, TODAY, 'monday', []);

    // Assert
    expect(query).toEqual({ datesFrom: '2026-10-26', datesTo: '2026-12-06', sort: { field: 'number', direction: 'asc' }, limit: 200 });
  });

  it('工具列的到期條件照帶、不和畫面取交集（時間軸也是這樣）：期間跨進畫面的，到期日可以在畫面之外', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('view=calendar&assignee=me&type=task&due=overdue'));

    // Act
    const query = visibleSpanQuery(filters, GRID, TODAY, 'monday', []);

    // Assert
    expect(query).toMatchObject({
      assignee: 'me',
      type: ['task'],
      dueTo: '2026-11-17',
      stateCategory: ['backlog', 'not_started', 'in_progress'],
      datesFrom: '2026-10-26',
      datesTo: '2026-12-06',
    });
    expect(query).not.toHaveProperty('dueFrom');
  });

  it('「沒有到期日」後端不能和日期範圍一起用：不帶到期條件，讀回來再在畫面上把有到期日的拿掉', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('due=none'));

    // Act
    const query = visibleSpanQuery(filters, GRID, TODAY, 'monday', []);

    // Assert
    expect(query).toMatchObject({ datesFrom: '2026-10-26', datesTo: '2026-12-06' });
    expect(query).not.toHaveProperty('noDue');
  });

  it('「目前的週期」而現在沒有週期時，結果一定是空的（null）', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('cycle=current'));

    // Act / Assert
    expect(visibleSpanQuery(filters, GRID, TODAY, 'monday', [])).toBeNull();
  });
});
