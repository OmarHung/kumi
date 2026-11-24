import { STATE_GROUP } from './boardGroup';
import { describe, expect, it } from 'vitest';
import { cycleOf } from '../test/planning';
import { exampleOwner, smkItem } from '../test/fixtures';
import {
  boardColumnQuery,
  boardFiltersFromSearchParams,
  boardFiltersToSearchParams,
  hasActiveBoardFilters,
  matchesBoardFilters,
  switchViewParams,
} from './projectView';

const TODAY = '2026-11-18';
const NO_FILTERS = { workflow: null, assignee: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' } as const;

describe('看板的到期與優先度', () => {
  it('切換表格與看板時帶著到期與優先度，類型與狀態不帶', () => {
    // Arrange
    const params = new URLSearchParams('type=task&due=week&priority=high&q=首頁');

    // Act
    const board = switchViewParams(params, 'board');

    // Assert
    expect(board.toString()).toBe(new URLSearchParams('view=board&q=首頁&due=week&priority=high').toString());
  });

  it('從網址讀出、寫回網址，順序是流程、負責人、到期、優先度、搜尋', () => {
    // Arrange
    const params = new URLSearchParams('view=board&q=首頁&priority=low&due=overdue&assignee=me');

    // Act
    const filters = boardFiltersFromSearchParams(params);
    const written = boardFiltersToSearchParams(filters, undefined);

    // Assert
    expect(filters).toEqual({ workflow: null, assignee: 'me', due: 'overdue', priority: 'low', cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '首頁' });
    expect(written.toString()).toBe(new URLSearchParams('view=board&assignee=me&due=overdue&priority=low&q=首頁').toString());
  });

  it('到期或優先度也算篩選，清除條件按鈕才會出現', () => {
    // Arrange / Act / Assert
    expect(hasActiveBoardFilters({ ...NO_FILTERS, due: 'none' })).toBe(true);
    expect(hasActiveBoardFilters({ ...NO_FILTERS, priority: 'none' })).toBe(true);
    expect(hasActiveBoardFilters(NO_FILTERS)).toBe(false);
  });

  it('每一欄的條件帶上優先度與到期的參數', () => {
    // Arrange
    const filters = { ...NO_FILTERS, due: 'overdue', priority: 'urgent' } as const;

    // Act
    const query = boardColumnQuery(filters, ['task'], 'doing', TODAY, 'monday');

    // Assert
    expect(query).toMatchObject({
      state: ['doing'],
      priority: ['urgent'],
      dueTo: '2026-11-17',
      stateCategory: ['backlog', 'not_started', 'in_progress'],
    });
  });

  it('送出中的卡片也照優先度與到期比對', () => {
    // Arrange
    const item = smkItem({ priority: 'high', dueDate: TODAY, stateCategory: 'in_progress' });
    const me = exampleOwner.personId;

    // Act / Assert
    expect(matchesBoardFilters(item, { ...NO_FILTERS, priority: 'high', due: 'today' }, me, TODAY, 'monday')).toBe(true);
    expect(matchesBoardFilters(item, { ...NO_FILTERS, priority: 'low' }, me, TODAY, 'monday')).toBe(false);
    expect(matchesBoardFilters(item, { ...NO_FILTERS, due: 'overdue' }, me, TODAY, 'monday')).toBe(false);
  });
});

describe('看板的週期與里程碑', () => {
  const SPRINT = '0190a000-0000-7000-8000-0000000000d2';
  const V1 = '0190a000-0000-7000-8000-0000000000e1';
  const cycles = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT })];

  it('從網址讀出、寫回網址，也算篩選', () => {
    // Arrange
    const params = new URLSearchParams(`view=board&cycle=current&milestone=${V1}`);

    // Act
    const filters = boardFiltersFromSearchParams(params);

    // Assert
    expect(filters).toEqual({ ...NO_FILTERS, cycle: 'current', milestone: V1 });
    expect(boardFiltersToSearchParams(filters, undefined).toString()).toBe(params.toString());
    expect(hasActiveBoardFilters({ ...NO_FILTERS, cycle: 'none' })).toBe(true);
  });

  it('一欄的條件帶上換成編號的週期與里程碑', () => {
    // Arrange
    const filters = { ...NO_FILTERS, cycle: 'current', milestone: 'none' } as const;

    // Act
    const query = boardColumnQuery(filters, ['task'], 'todo', TODAY, 'monday', cycles);

    // Assert
    expect(query).toMatchObject({ cycleId: SPRINT, noMilestone: true });
  });

  it('送出中的卡片也看週期與里程碑', () => {
    // Arrange
    const item = smkItem({ cycleId: SPRINT, milestoneId: null });
    const me = 'someone';

    // Act / Assert
    expect(matchesBoardFilters(item, { ...NO_FILTERS, cycle: 'current' }, me, TODAY, 'monday', cycles)).toBe(true);
    expect(matchesBoardFilters(item, { ...NO_FILTERS, cycle: 'none' }, me, TODAY, 'monday', cycles)).toBe(false);
    expect(matchesBoardFilters(item, { ...NO_FILTERS, milestone: V1 }, me, TODAY, 'monday', cycles)).toBe(false);
  });

  it('切換檢視時週期與里程碑兩邊都帶', () => {
    // Arrange
    const params = new URLSearchParams(`type=task&cycle=none&milestone=${V1}`);

    // Act
    const board = switchViewParams(params, 'board');

    // Assert
    expect(board.toString()).toBe(new URLSearchParams(`view=board&cycle=none&milestone=${V1}`).toString());
  });
});
