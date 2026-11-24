import { describe, expect, it } from 'vitest';
import { dayHeading, monthGrid, monthOf, monthTitle, shiftMonth, weekdayHeaders } from './calendarMonth';

const TODAY = '2026-11-18';

describe('monthOf', () => {
  it('網址的 month 合法就用它，沒給或打錯就是今天所在的月', () => {
    // Arrange / Act / Assert
    expect(monthOf('2026-12', TODAY)).toBe('2026-12');
    expect(monthOf(null, TODAY)).toBe('2026-11');
    expect(monthOf('2026-13', TODAY)).toBe('2026-11');
    expect(monthOf('hello', TODAY)).toBe('2026-11');
  });
});

describe('shiftMonth', () => {
  it('跨年也對', () => {
    // Arrange / Act / Assert
    expect(shiftMonth('2026-12', 1)).toBe('2027-01');
    expect(shiftMonth('2026-01', -1)).toBe('2025-12');
    expect(shiftMonth('2026-11', 0)).toBe('2026-11');
  });
});

describe('monthGrid', () => {
  it('一週從週一開始，頭尾補上下個月的日子，整週整週排', () => {
    // Arrange / Act
    const grid = monthGrid('2026-11', 'monday');

    // Assert：2026-11-01 是週日，所以第一週從 10/26 開始；11/30 是週一，最後一週到 12/06。
    expect(grid.weeks).toHaveLength(6);
    expect(grid.weeks[0]?.[0]).toBe('2026-10-26');
    expect(grid.weeks[0]?.[6]).toBe('2026-11-01');
    expect(grid.weeks.at(-1)?.[0]).toBe('2026-11-30');
    expect(grid.weeks.at(-1)?.[6]).toBe('2026-12-06');
    expect(grid.start).toBe('2026-10-26');
    expect(grid.end).toBe('2026-12-06');
  });

  it('剛好四個整週的月份（2027 年 2 月，1 日是週一、28 天）只有四週，不補上下個月', () => {
    // Arrange / Act
    const grid = monthGrid('2027-02', 'monday');

    // Assert
    expect(grid.weeks).toHaveLength(4);
    expect(grid.start).toBe('2027-02-01');
    expect(grid.end).toBe('2027-02-28');
  });
});

describe('monthGrid：一週從週日開始', () => {
  it('頭尾補上下個月的日子，整週整週排，第一欄是週日', () => {
    // Arrange / Act
    const grid = monthGrid('2026-11', 'sunday');

    // Assert：2026-11-01 是週日，第一週就從 11/01 開始；11/30 是週一，最後一週到 12/05（週六）。
    expect(grid.weeks).toHaveLength(5);
    expect(grid.weeks[0]?.[0]).toBe('2026-11-01');
    expect(grid.weeks[0]?.[6]).toBe('2026-11-07');
    expect(grid.weeks.at(-1)?.[0]).toBe('2026-11-29');
    expect(grid.weeks.at(-1)?.[6]).toBe('2026-12-05');
    expect(grid.start).toBe('2026-11-01');
    expect(grid.end).toBe('2026-12-05');
  });

  it('1 日是週一時，前面補到上個月的週日', () => {
    // Arrange / Act
    const grid = monthGrid('2027-02', 'sunday');

    // Assert：2027-02-01 是週一，2027-02-28 是週日。
    expect(grid.start).toBe('2027-01-31');
    expect(grid.end).toBe('2027-03-06');
    expect(grid.weeks).toHaveLength(5);
  });

  it('同一個月，兩種設定的週數可能不同：1 日是週日、31 天的月份，從週一開始要六週，從週日開始五週', () => {
    // Arrange：2026-03-01 是週日，2026-03-31 是週二。

    // Act
    const monday = monthGrid('2026-03', 'monday');
    const sunday = monthGrid('2026-03', 'sunday');

    // Assert
    expect(monday.weeks).toHaveLength(6);
    expect(monday.start).toBe('2026-02-23');
    expect(monday.end).toBe('2026-04-05');
    expect(sunday.weeks).toHaveLength(5);
    expect(sunday.start).toBe('2026-03-01');
    expect(sunday.end).toBe('2026-04-04');
  });

  it('1 日是週六、30 天的月份，從週日開始要六週，從週一開始五週', () => {
    // Arrange：2025-11-01 是週六，2025-11-30 是週日。

    // Act
    const sunday = monthGrid('2025-11', 'sunday');
    const monday = monthGrid('2025-11', 'monday');

    // Assert
    expect(sunday.weeks).toHaveLength(6);
    expect(sunday.start).toBe('2025-10-26');
    expect(sunday.end).toBe('2025-12-06');
    expect(sunday.weeks.every((week) => week.length === 7)).toBe(true);
    expect(monday.weeks).toHaveLength(5);
    expect(monday.start).toBe('2025-10-27');
    expect(monday.end).toBe('2025-11-30');
  });
});

describe('weekdayHeaders', () => {
  it('從週一開始：週一到週日', () => {
    // Arrange / Act / Assert
    expect(weekdayHeaders('monday')).toEqual(['週一', '週二', '週三', '週四', '週五', '週六', '週日']);
  });

  it('從週日開始：週日到週六', () => {
    // Arrange / Act / Assert
    expect(weekdayHeaders('sunday')).toEqual(['週日', '週一', '週二', '週三', '週四', '週五', '週六']);
  });
});

describe('文字', () => {
  it('標題與每一天的標題', () => {
    // Arrange / Act / Assert
    expect(monthTitle('2026-11')).toBe('2026 年 11 月');
    expect(dayHeading('2026-11-18')).toBe('11 月 18 日（週三）');
    expect(dayHeading('2026-11-22')).toBe('11 月 22 日（週日）');
  });
});
