import { describe, expect, it } from 'vitest';
import { dueQuery, matchesDue } from './dueFilter';
import { EMPTY_FILTERS, filtersFromSearchParams, filtersToSearchParams, hasActiveFilters, staleCutoff, toItemQuery } from './itemFilters';
import { DUE_SOON_DAYS, STALE_DAYS } from './overviewRules';
import { switchViewParams } from './projectView';

const TODAY = '2026-11-18';
const OPEN = ['backlog', 'not_started', 'in_progress'];
const NOW = new Date('2026-11-26T08:30:45.123Z');

describe('7 天內到期', () => {
  it('和總覽用同一個數字：今天到 6 天後，而且是還沒結束的', () => {
    // Arrange / Act
    const query = dueQuery('soon', TODAY, 'monday');

    // Assert
    expect(DUE_SOON_DAYS).toBe(7);
    expect(query).toEqual({ dueFrom: '2026-11-18', dueTo: '2026-11-24', stateCategory: OPEN });
  });

  it('從網址讀得到，標籤是「7 天內到期」', () => {
    // Arrange / Act
    const filters = filtersFromSearchParams(new URLSearchParams('due=soon'));

    // Assert
    expect(filters.due).toBe('soon');
  });

  it('比對單一項目：邊界是今天與 6 天後，完成的不算', () => {
    // Arrange / Act / Assert
    expect(matchesDue('soon', '2026-11-18', 'in_progress', TODAY, 'monday')).toBe(true);
    expect(matchesDue('soon', '2026-11-24', 'not_started', TODAY, 'monday')).toBe(true);
    expect(matchesDue('soon', '2026-11-25', 'not_started', TODAY, 'monday')).toBe(false);
    expect(matchesDue('soon', '2026-11-17', 'not_started', TODAY, 'monday')).toBe(false);
    expect(matchesDue('soon', '2026-11-20', 'done', TODAY, 'monday')).toBe(false);
    expect(matchesDue('soon', null, 'in_progress', TODAY, 'monday')).toBe(false);
  });
});

describe('只看還沒結束的（open）', () => {
  it('網址是 open=1：讀得到、寫得回，不是 1 的值當成沒開', () => {
    // Arrange / Act
    const on = filtersFromSearchParams(new URLSearchParams('open=1'));
    const off = filtersFromSearchParams(new URLSearchParams('open=true'));

    // Assert
    expect(on.open).toBe(true);
    expect(off.open).toBe(false);
    expect(filtersToSearchParams(on).toString()).toBe('open=1');
  });

  it('送三個還沒結束的狀態類別，算有篩選', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, open: true };

    // Act
    const query = toItemQuery(filters, 50, TODAY, 'monday', [], NOW);

    // Assert
    expect(query.stateCategory).toEqual(OPEN);
    expect(query.updatedBefore).toBeUndefined();
    expect(hasActiveFilters(filters)).toBe(true);
  });

  it('和狀態條件同時有時兩個都送（而且），不猜哪個優先', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, open: true, state: 'done' };

    // Act
    const query = toItemQuery(filters, 50, TODAY, 'monday', [], NOW);

    // Assert
    expect(query.state).toEqual(['done']);
    expect(query.stateCategory).toEqual(OPEN);
  });
});

describe('停滯（stale）', () => {
  it('網址是 stale=1，送現在減 14 天的時間加上還沒結束的類別', () => {
    // Arrange
    const filters = filtersFromSearchParams(new URLSearchParams('stale=1&open=1'));

    // Act
    const query = toItemQuery(filters, 50, TODAY, 'monday', [], NOW);

    // Assert
    expect(STALE_DAYS).toBe(14);
    expect(query.updatedBefore).toBe('2026-11-12T08:30:00.000Z');
    expect(query.stateCategory).toEqual(OPEN);
    expect(filtersToSearchParams(filters).toString()).toBe('open=1&stale=1');
  });

  it('只開 stale 也帶還沒結束的類別（停滯一定是還沒結束的）', () => {
    // Arrange / Act
    const query = toItemQuery({ ...EMPTY_FILTERS, stale: true }, 50, TODAY, 'monday', [], NOW);

    // Assert
    expect(query.stateCategory).toEqual(OPEN);
  });

  it('時間取到分鐘：同一分鐘裡兩次呼叫的結果一樣，查詢鍵才不會一直變', () => {
    // Arrange
    const a = new Date('2026-11-26T08:30:01.000Z');
    const b = new Date('2026-11-26T08:30:59.999Z');

    // Act / Assert
    expect(staleCutoff(a)).toBe(staleCutoff(b));
    expect(staleCutoff(new Date('2026-11-26T08:31:00.000Z'))).not.toBe(staleCutoff(a));
  });

  it('沒開的時候查詢裡什麼都沒有', () => {
    // Arrange / Act
    const query = toItemQuery(EMPTY_FILTERS, 50, TODAY, 'monday', [], NOW);

    // Assert
    expect(query).not.toHaveProperty('updatedBefore');
    expect(query).not.toHaveProperty('stateCategory');
  });
});

describe('切換版面', () => {
  it('open、stale 只有表格有：換到看板時不帶，到期的 soon 照帶', () => {
    // Arrange
    const params = new URLSearchParams('open=1&stale=1&due=soon');

    // Act
    const board = switchViewParams(params, 'board');

    // Assert
    expect(board.toString()).toBe(new URLSearchParams('view=board&due=soon').toString());
  });
});
