import { describe, expect, it } from 'vitest';
import { agyItem } from '../../test/fixtures';
import { dayButtonName, dotsOfDay, moveSelectedDay, selectedDayOf } from './compactDay';

const TODAY = '2026-11-18';

describe('selectedDayOf', () => {
  it('網址的 day 在這個月就用它', () => {
    expect(selectedDayOf('2026-11-05', '2026-11', TODAY)).toBe('2026-11-05');
  });

  it('沒給或不在這個月：本月選今天，別的月選 1 號', () => {
    expect(selectedDayOf(null, '2026-11', TODAY)).toBe(TODAY);
    expect(selectedDayOf('2026-12-05', '2026-11', TODAY)).toBe(TODAY);
    expect(selectedDayOf('亂寫', '2026-12', TODAY)).toBe('2026-12-01');
    expect(selectedDayOf('2026-11-31', '2026-11', TODAY)).toBe(TODAY);
  });
});

describe('dotsOfDay', () => {
  it('依序：過期、其他、完成、標示，最多三個', () => {
    // Arrange
    const overdue = agyItem({ number: 1, dueDate: '2026-11-10', stateCategory: 'in_progress' });
    const active = agyItem({ number: 2, dueDate: '2026-11-18', stateCategory: 'not_started' });
    const done = agyItem({ number: 3, dueDate: '2026-11-10', stateCategory: 'done' });

    // Act / Assert
    expect(dotsOfDay([done, active, overdue], 1, TODAY)).toEqual(['overdue', 'active', 'done']);
    expect(dotsOfDay([active], 2, TODAY)).toEqual(['active', 'marker', 'marker']);
    expect(dotsOfDay([], 0, TODAY)).toEqual([]);
  });
});

describe('dayButtonName', () => {
  it('日期、今天、項目數與標示數', () => {
    expect(dayButtonName('2026-11-17', TODAY, 2, 0)).toBe('11 月 17 日，2 個項目');
    expect(dayButtonName('2026-11-18', TODAY, 0, 1)).toBe('11 月 18 日（今天），沒有項目，1 個里程碑或週期標示');
  });
});

describe('moveSelectedDay', () => {
  it('方向鍵左右一天、上下一週，不出這個月', () => {
    expect(moveSelectedDay('2026-11-18', 'ArrowRight', '2026-11')).toBe('2026-11-19');
    expect(moveSelectedDay('2026-11-18', 'ArrowUp', '2026-11')).toBe('2026-11-11');
    expect(moveSelectedDay('2026-11-30', 'ArrowRight', '2026-11')).toBe('2026-11-30');
    expect(moveSelectedDay('2026-11-03', 'ArrowUp', '2026-11')).toBe('2026-11-03');
    expect(moveSelectedDay('2026-11-18', 'Home', '2026-11')).toBe('2026-11-01');
    expect(moveSelectedDay('2026-11-18', 'End', '2026-11')).toBe('2026-11-30');
    expect(moveSelectedDay('2026-11-18', 'a', '2026-11')).toBeNull();
  });
});
