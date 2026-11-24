import { describe, expect, it } from 'vitest';
import { EMPTY_FILTERS, filtersFromSearchParams, filtersToSearchParams, hasActiveFilters, toItemQuery } from './itemFilters';
import { dueQuery, matchesDue } from './dueFilter';

// 2026-11-18 是週三，這一週的最後一天是 2026-11-22（週日）。
const TODAY = '2026-11-18';

describe('網址上的到期與優先度', () => {
  it('讀出到期與優先度，不認得的值略過', () => {
    // Arrange
    const good = new URLSearchParams('due=week&priority=high');
    const bad = new URLSearchParams('due=tomorrow&priority=critical');

    // Act
    const parsed = filtersFromSearchParams(good);
    const ignored = filtersFromSearchParams(bad);

    // Assert
    expect(parsed).toMatchObject({ due: 'week', priority: 'high' });
    expect(ignored).toMatchObject({ due: null, priority: null });
  });

  it('寫回網址時排在負責人後面、搜尋前面，順序固定', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, assignee: 'me', due: 'overdue', priority: 'none', q: '首頁' } as const;

    // Act
    const text = filtersToSearchParams(filters).toString();

    // Assert
    expect(text).toBe(new URLSearchParams('assignee=me&due=overdue&priority=none&q=首頁').toString());
  });

  it('有到期或優先度的條件就算有篩選', () => {
    // Arrange / Act / Assert
    expect(hasActiveFilters({ ...EMPTY_FILTERS, due: 'today' })).toBe(true);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, priority: 'low' })).toBe(true);
  });
});

describe('dueQuery', () => {
  it('已過期是昨天以前、而且還沒完成的狀態類別', () => {
    // Arrange / Act
    const query = dueQuery('overdue', TODAY, 'monday');

    // Assert
    expect(query).toEqual({ dueTo: '2026-11-17', stateCategory: ['backlog', 'not_started', 'in_progress'] });
  });

  it('今天到期是今天到今天', () => {
    // Arrange / Act / Assert
    expect(dueQuery('today', TODAY, 'monday')).toEqual({ dueFrom: TODAY, dueTo: TODAY });
  });

  it('本週到期是今天到這週的週日', () => {
    // Arrange / Act / Assert
    expect(dueQuery('week', TODAY, 'monday')).toEqual({ dueFrom: TODAY, dueTo: '2026-11-22' });
  });

  it('一週從週日開始時，本週到期是今天到這週的週六', () => {
    // Arrange / Act / Assert
    expect(dueQuery('week', TODAY, 'sunday')).toEqual({ dueFrom: TODAY, dueTo: '2026-11-21' });
    expect(dueQuery('week', '2026-11-22', 'sunday')).toEqual({ dueFrom: '2026-11-22', dueTo: '2026-11-28' });
  });

  it('今天是週日時，本週到期只有今天', () => {
    // Arrange / Act / Assert
    expect(dueQuery('week', '2026-11-22', 'monday')).toEqual({ dueFrom: '2026-11-22', dueTo: '2026-11-22' });
  });

  it('月初的昨天是上個月底', () => {
    // Arrange / Act / Assert
    expect(dueQuery('overdue', '2026-12-01', 'monday').dueTo).toBe('2026-11-30');
  });

  it('沒有到期日是 noDue', () => {
    // Arrange / Act / Assert
    expect(dueQuery('none', TODAY, 'monday')).toEqual({ noDue: true });
    expect(dueQuery(null, TODAY, 'monday')).toEqual({});
  });
});

describe('toItemQuery 的到期與優先度', () => {
  it('帶上優先度與到期的參數', () => {
    // Arrange
    const filters = { ...EMPTY_FILTERS, due: 'today', priority: 'urgent' } as const;

    // Act
    const query = toItemQuery(filters, 50, TODAY, 'monday');

    // Assert
    expect(query).toEqual({ priority: ['urgent'], dueFrom: TODAY, dueTo: TODAY, limit: 50 });
  });
});

describe('matchesDue 照偏好', () => {
  it('本週到期：週日開始時週日不算這一週，週一開始時算', () => {
    // Arrange / Act / Assert
    expect(matchesDue('week', '2026-11-22', 'backlog', TODAY, 'sunday')).toBe(false);
    expect(matchesDue('week', '2026-11-21', 'backlog', TODAY, 'sunday')).toBe(true);
    expect(matchesDue('week', '2026-11-22', 'backlog', TODAY, 'monday')).toBe(true);
  });
});

describe('matchesDue', () => {
  it('依到期日與狀態類別比對，和後端的條件同一套', () => {
    // Arrange / Act / Assert
    expect(matchesDue('overdue', '2026-11-17', 'in_progress', TODAY, 'monday')).toBe(true);
    expect(matchesDue('overdue', '2026-11-17', 'done', TODAY, 'monday')).toBe(false);
    expect(matchesDue('today', TODAY, 'done', TODAY, 'monday')).toBe(true);
    expect(matchesDue('week', '2026-11-23', 'backlog', TODAY, 'monday')).toBe(false);
    expect(matchesDue('week', '2026-11-22', 'backlog', TODAY, 'monday')).toBe(true);
    expect(matchesDue('none', null, 'backlog', TODAY, 'monday')).toBe(true);
    expect(matchesDue('none', TODAY, 'backlog', TODAY, 'monday')).toBe(false);
    expect(matchesDue(null, null, 'backlog', TODAY, 'monday')).toBe(true);
  });
});
