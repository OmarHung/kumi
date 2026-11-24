import { describe, expect, it } from 'vitest';
import { dateViewFiltersFromSearchParams } from '../../domain/projectView';
import { windowOf } from './timelineRange';
import { datedQuery, undatedQuery } from './useTimelineData';

const WINDOW = windowOf('week', '2026-11-02');

describe('時間軸的類型與狀態篩選', () => {
  it('期間的清單與「沒有日期」的清單都帶類型與狀態', () => {
    // Arrange
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('view=timeline&type=task&state=doing'));

    // Act / Assert
    expect(datedQuery(filters, WINDOW, '2026-11-18', 'monday', [])).toMatchObject({ type: ['task'], state: ['doing'], datesFrom: '2026-11-02' });
    expect(undatedQuery(filters, '2026-11-18', 'monday', [])).toMatchObject({ type: ['task'], state: ['doing'], noDue: true });
  });
});
