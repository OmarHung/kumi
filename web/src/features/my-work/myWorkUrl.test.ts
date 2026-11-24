import { describe, expect, it } from 'vitest';
import { DEFAULT_MY_WORK_FILTERS } from '../../domain/myWorkView';
import { withMyWorkFilters } from './myWorkUrl';

describe('withMyWorkFilters', () => {
  it('換掉條件的參數，版面、分組、排序與開著的面板留著', () => {
    // Arrange
    const params = new URLSearchParams('view=calendar&q=舊的&assignee=all&group=project&sort=title:asc&item=WEB-1&month=2026-11');

    // Act
    const next = withMyWorkFilters(params, { ...DEFAULT_MY_WORK_FILTERS, priority: 'high', project: 'WEB' });

    // Assert
    expect(Object.fromEntries(next)).toEqual({
      view: 'calendar',
      group: 'project',
      sort: 'title:asc',
      item: 'WEB-1',
      month: '2026-11',
      priority: 'high',
      project: 'WEB',
    });
  });

  it('預設的條件不寫進網址', () => {
    // Arrange
    const params = new URLSearchParams('category=all&due=today');

    // Act
    const next = withMyWorkFilters(params, DEFAULT_MY_WORK_FILTERS);

    // Assert
    expect(next.toString()).toBe('');
  });
});
