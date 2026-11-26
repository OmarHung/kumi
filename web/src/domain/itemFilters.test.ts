import { describe, expect, it } from 'vitest';
import { cycleOf } from '../test/planning';
import {
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  hasActiveFilters,
  nextSort,
  toItemQuery,
} from './itemFilters';

const TODAY = '2026-11-18';
const SPRINT_ID = '0190a000-0000-7000-8000-0000000000d2';
const V1_ID = '0190a000-0000-7000-8000-0000000000e1';
const CYCLES = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_ID })];

describe('filtersFromSearchParams', () => {
  it('讀出類型、狀態、負責人、搜尋與排序', () => {
    // Arrange
    const params = new URLSearchParams('type=task&state=doing&assignee=me&q=首頁&sort=due_date:desc');

    // Act
    const filters = filtersFromSearchParams(params);

    // Assert
    expect(filters).toEqual({
      type: 'task',
      state: 'doing',
      assignee: 'me',
      createdBy: null,
      due: null,
      priority: null,
      cycle: null,
      milestone: null,
      fields: {},
      q: '首頁',
      sort: { field: 'due_date', direction: 'desc' },
    });
  });

  it('不認得的排序略過，後端才不會回查詢參數錯誤', () => {
    // Arrange / Act / Assert
    expect(filtersFromSearchParams(new URLSearchParams('sort=assignee:asc')).sort).toBeNull();
    expect(filtersFromSearchParams(new URLSearchParams('sort=due_date')).sort).toBeNull();
    expect(filtersFromSearchParams(new URLSearchParams('sort=due_date:up')).sort).toBeNull();
  });

  it('沒有條件時是空的', () => {
    // Arrange / Act / Assert
    expect(filtersFromSearchParams(new URLSearchParams())).toEqual(EMPTY_FILTERS);
  });
});

describe('週期與里程碑的網址參數', () => {
  it('讀出 cycle 與 milestone：current、none、編號；不認得的略過', () => {
    // Arrange / Act
    const some = filtersFromSearchParams(new URLSearchParams(`cycle=current&milestone=${V1_ID}`));
    const none = filtersFromSearchParams(new URLSearchParams('cycle=none&milestone=none'));
    const junk = filtersFromSearchParams(new URLSearchParams('cycle=sprint-1&milestone=current'));

    // Assert
    expect([some.cycle, some.milestone]).toEqual(['current', V1_ID]);
    expect([none.cycle, none.milestone]).toEqual(['none', 'none']);
    expect([junk.cycle, junk.milestone]).toEqual([null, null]);
  });

  it('寫回網址時帶 cycle 與 milestone，也算有篩選', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, cycle: SPRINT_ID, milestone: 'none' };

    // Act
    const text = filtersToSearchParams(filters).toString();

    // Assert
    expect(text).toBe(new URLSearchParams(`cycle=${SPRINT_ID}&milestone=none`).toString());
    expect(hasActiveFilters({ ...EMPTY_FILTERS, cycle: 'none' })).toBe(true);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, milestone: V1_ID })).toBe(true);
  });
});

describe('filtersToSearchParams', () => {
  it('只寫有設的條件，順序固定', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, q: ' 首頁 ', assignee: 'unassigned', sort: { field: 'title', direction: 'asc' } } as const;

    // Act
    const text = filtersToSearchParams(filters).toString();

    // Assert
    expect(text).toBe(new URLSearchParams('assignee=unassigned&q=首頁&sort=title:asc').toString());
  });
});

describe('建立者的條件', () => {
  it('網址 createdBy=me 或人員編號讀成條件，空白當成沒給；寫回網址時排在負責人後面', () => {
    // Arrange
    const params = new URLSearchParams('assignee=me&createdBy=p-2&q=x');

    // Act
    const filters = filtersFromSearchParams(params);

    // Assert
    expect(filters.createdBy).toBe('p-2');
    expect(filtersFromSearchParams(new URLSearchParams('createdBy=%20')).createdBy).toBeNull();
    expect(filtersToSearchParams(filters).toString()).toBe(new URLSearchParams('assignee=me&createdBy=p-2&q=x').toString());
  });

  it('送給 API 的條件帶 createdBy，也算有條件', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, createdBy: 'me' };

    // Assert
    expect(toItemQuery(filters, 50, TODAY, 'monday')).toEqual({ createdBy: 'me', limit: 50 });
    expect(hasActiveFilters(filters)).toBe(true);
  });
});

describe('toItemQuery', () => {
  it('轉成 API 的條件，帶上每頁的數量', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, type: 'task', state: 'doing', assignee: 'p-1', q: 'x' };

    // Act
    const query = toItemQuery(filters, 50, TODAY, 'monday');

    // Assert
    expect(query).toEqual({ type: ['task'], state: ['doing'], assignee: 'p-1', q: 'x', limit: 50 });
  });

  it('current 用使用者的今天從週期清單換成編號', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, cycle: 'current', milestone: V1_ID };

    // Act
    const query = toItemQuery(filters, 50, TODAY, 'monday', CYCLES);

    // Assert
    expect(query).toEqual({ cycleId: SPRINT_ID, milestoneId: V1_ID, limit: 50 });
  });

  it('沒有目前的週期時不產生週期條件（呼叫端不送請求，不送假編號）；none 是沒有週期與沒有里程碑', () => {
    // Arrange / Act / Assert
    expect(toItemQuery({ ...EMPTY_FILTERS, cycle: 'current' }, 50, TODAY, 'monday', [])).toEqual({ limit: 50 });
    expect(toItemQuery({ ...EMPTY_FILTERS, cycle: 'none', milestone: 'none' }, 50, TODAY, 'monday', CYCLES)).toEqual({
      noCycle: true,
      noMilestone: true,
      limit: 50,
    });
  });

  it('沒有條件時只有數量', () => {
    // Arrange / Act / Assert
    expect(toItemQuery(EMPTY_FILTERS, 50, TODAY, 'monday')).toEqual({ limit: 50 });
  });
});

describe('hasActiveFilters', () => {
  it('排序不算篩選', () => {
    // Arrange / Act / Assert
    expect(hasActiveFilters(EMPTY_FILTERS)).toBe(false);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, sort: { field: 'title', direction: 'asc' } })).toBe(false);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, q: '首頁' })).toBe(true);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, assignee: 'me' })).toBe(true);
  });
});

describe('nextSort', () => {
  it('同一欄依序是遞增、遞減、取消；換一欄從遞增開始', () => {
    // Arrange
    const ascending = nextSort(null, 'due_date');

    // Act
    const descending = nextSort(ascending, 'due_date');
    const cleared = nextSort(descending, 'due_date');
    const other = nextSort(descending, 'title');

    // Assert
    expect(ascending).toEqual({ field: 'due_date', direction: 'asc' });
    expect(descending).toEqual({ field: 'due_date', direction: 'desc' });
    expect(cleared).toBeNull();
    expect(other).toEqual({ field: 'title', direction: 'asc' });
  });
});
