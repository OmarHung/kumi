import { describe, expect, it } from 'vitest';
import { placeRange, windowOf } from './timelineRange';
import { isTickUnderToday, todayCol, trackSegment, weekTicks } from './timelineTrack';

// 2026-11-16 是週一：六週共 42 天，到 2026-12-27。
const WINDOW = windowOf('week', '2026-11-16');

function segmentOf(start: string, end: string) {
  const placement = placeRange(WINDOW, start, end);
  if (placement === null) throw new Error('測試資料要在範圍內');
  return trackSegment(placement);
}

describe('軌道上的一段', () => {
  it('完全在範圍內：第幾天開始、幾天，兩端都是圓的', () => {
    // Arrange & Act & Assert
    expect(segmentOf('2026-11-23', '2026-11-29')).toEqual({ col: 7, span: 7, isStartFlat: false, isEndFlat: false });
  });

  it('開始早於範圍：從第 0 天開始、左端是平的', () => {
    // Arrange & Act & Assert
    expect(segmentOf('2026-11-01', '2026-11-18')).toEqual({ col: 0, span: 3, isStartFlat: true, isEndFlat: false });
  });

  it('結束晚於範圍：一路畫到最後一天、右端是平的', () => {
    // Arrange & Act & Assert
    expect(segmentOf('2026-12-20', '2027-01-10')).toEqual({ col: 34, span: 8, isStartFlat: false, isEndFlat: true });
  });

  it('跨過整個範圍：整條，兩端都是平的', () => {
    // Arrange & Act & Assert
    expect(segmentOf('2026-10-01', '2027-02-01')).toEqual({ col: 0, span: 42, isStartFlat: true, isEndFlat: true });
  });

  it('只有開始日或只有到期日（同一天）：一天寬', () => {
    // Arrange & Act & Assert
    expect(segmentOf('2026-11-18', '2026-11-18')).toEqual({ col: 2, span: 1, isStartFlat: false, isEndFlat: false });
  });
});

describe('週的刻度', () => {
  it('每週一個，位置是那一週的第一天是範圍的第幾天，標籤是月/日', () => {
    // Arrange & Act
    const ticks = weekTicks(WINDOW);

    // Assert
    expect(ticks).toHaveLength(6);
    expect(ticks[0]).toEqual({ col: 0, label: '11/16' });
    expect(ticks[1]).toEqual({ col: 7, label: '11/23' });
    expect(ticks[5]).toEqual({ col: 35, label: '12/21' });
  });

  it('範圍跨年時標籤仍只寫月/日：年份在上面的範圍裡，帶年份的長標籤在手機上會疊到下一個', () => {
    // Arrange
    const window = windowOf('week', '2026-12-14');

    // Act
    const ticks = weekTicks(window);

    // Assert
    expect(ticks[3]?.label).toBe('01/04');
  });
});

describe('今天', () => {
  it('在範圍內：是範圍的第幾天，第一天與最後一天都算', () => {
    // Arrange & Act & Assert
    expect(todayCol(WINDOW, '2026-11-18')).toBe(2);
    expect(todayCol(WINDOW, '2026-11-16')).toBe(0);
    expect(todayCol(WINDOW, '2026-12-27')).toBe(41);
  });

  it('不在範圍內是 null', () => {
    // Arrange & Act & Assert
    expect(todayCol(WINDOW, '2026-11-15')).toBeNull();
    expect(todayCol(WINDOW, '2026-12-28')).toBeNull();
  });

  it('標籤被「今天」的膠囊蓋到時略過，離得夠遠的留著', () => {
    // Arrange：今天是第 2 天（11/18），11/16 的標籤寫在膠囊底下，11/23 離膠囊夠遠。
    const [first, second] = weekTicks(WINDOW);

    // Act & Assert
    expect(isTickUnderToday(first?.col ?? -1, 2)).toBe(true);
    expect(isTickUnderToday(second?.col ?? -1, 2)).toBe(false);
  });
});
