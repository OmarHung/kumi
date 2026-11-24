import { describe, expect, it } from 'vitest';
import {
  daysBetween,
  defaultFrom,
  formatRange,
  formatRangeIn,
  fromFromSearchParams,
  placeRange,
  scaleFromSearchParams,
  stepDaysFor,
  timelineParams,
  windowOf,
} from './timelineRange';

const TODAY = '2026-11-18'; // 週三

describe('刻度與起點的網址參數', () => {
  it('沒有 scale 或不認得時是週，認得 month', () => {
    // Arrange & Act & Assert
    expect(scaleFromSearchParams(new URLSearchParams(''))).toBe('week');
    expect(scaleFromSearchParams(new URLSearchParams('scale=year'))).toBe('week');
    expect(scaleFromSearchParams(new URLSearchParams('scale=month'))).toBe('month');
  });

  it('沒有 from 時是今天往前一週所在的週一', () => {
    // Arrange & Act
    const from = fromFromSearchParams(new URLSearchParams(''), TODAY, 'monday');

    // Assert
    expect(defaultFrom(TODAY, 'monday')).toBe('2026-11-09');
    expect(from).toBe('2026-11-09');
  });

  it('今天是週一時，預設的起點是上一個週一', () => {
    expect(defaultFrom('2026-11-16', 'monday')).toBe('2026-11-09');
  });

  it('今天是週日時，預設的起點是上週的週一再往前一週', () => {
    // 11/22（週日）往前一週是 11/15（週日），所在週的週一是 11/09
    expect(defaultFrom('2026-11-22', 'monday')).toBe('2026-11-09');
  });

  it('from 不是週一時對齊到那一週的週一，格式不對的當作沒給', () => {
    expect(fromFromSearchParams(new URLSearchParams('from=2026-12-02'), TODAY, 'monday')).toBe('2026-11-30');
    expect(fromFromSearchParams(new URLSearchParams('from=2026-12-07'), TODAY, 'monday')).toBe('2026-12-07');
    expect(fromFromSearchParams(new URLSearchParams('from=明天'), TODAY, 'monday')).toBe('2026-11-09');
    expect(fromFromSearchParams(new URLSearchParams('from=2026-02-31'), TODAY, 'monday')).toBe('2026-11-09');
  });

  it('一週從週日開始：預設的起點是今天往前一週所在的週日', () => {
    // Arrange & Act
    const from = fromFromSearchParams(new URLSearchParams(''), TODAY, 'sunday');

    // Assert：今天 11/18（週三）往前一週是 11/11（週三），所在週的週日是 11/08。
    expect(defaultFrom(TODAY, 'sunday')).toBe('2026-11-08');
    expect(from).toBe('2026-11-08');
    expect(defaultFrom('2026-11-22', 'sunday')).toBe('2026-11-15');
  });

  it('一週從週日開始：from 不是週日時對齊到那一週的週日', () => {
    expect(fromFromSearchParams(new URLSearchParams('from=2026-12-02'), TODAY, 'sunday')).toBe('2026-11-29');
    expect(fromFromSearchParams(new URLSearchParams('from=2026-12-06'), TODAY, 'sunday')).toBe('2026-12-06');
    expect(fromFromSearchParams(new URLSearchParams('from=2026-12-07'), TODAY, 'sunday')).toBe('2026-12-06');
  });

  it('一週從週日開始：預設的起點（週日）不寫進網址，週一不是預設要寫', () => {
    // Arrange
    const current = new URLSearchParams('view=timeline');

    // Act
    const reset = timelineParams(current, { scale: 'week', from: '2026-11-08' }, TODAY, 'sunday');
    const monday = timelineParams(current, { scale: 'week', from: '2026-11-09' }, TODAY, 'sunday');

    // Assert
    expect(reset.has('from')).toBe(false);
    expect(monday.get('from')).toBe('2026-11-09');
  });

  it('寫回網址：預設的刻度與預設的起點不寫，其他參數留著', () => {
    // Arrange
    const current = new URLSearchParams('view=timeline&q=首頁&item=SMK-1&scale=month&from=2026-12-07');

    // Act
    const reset = timelineParams(current, { scale: 'week', from: '2026-11-09' }, TODAY, 'monday');
    const moved = timelineParams(current, { scale: 'month', from: '2026-12-14' }, TODAY, 'monday');

    // Assert
    expect(reset.toString()).toBe(new URLSearchParams('view=timeline&q=首頁&item=SMK-1').toString());
    expect(moved.get('scale')).toBe('month');
    expect(moved.get('from')).toBe('2026-12-14');
    expect(moved.get('q')).toBe('首頁');
  });
});

describe('畫面的範圍', () => {
  it('週的刻度是 42 天，月的刻度是 182 天，最後一天含在內', () => {
    expect(windowOf('week', '2026-11-09')).toEqual({ scale: 'week', from: '2026-11-09', to: '2026-12-20', days: 42 });
    expect(windowOf('month', '2026-11-09')).toEqual({ scale: 'month', from: '2026-11-09', to: '2027-05-09', days: 182 });
  });

  it('daysBetween 跨月與跨年都對', () => {
    expect(daysBetween('2026-11-09', '2026-11-09')).toBe(0);
    expect(daysBetween('2026-11-09', '2026-12-01')).toBe(22);
    expect(daysBetween('2026-12-30', '2027-01-02')).toBe(3);
    expect(daysBetween('2026-11-10', '2026-11-09')).toBe(-1);
  });
});

describe('一段期間在畫面上的位置', () => {
  const window = windowOf('week', '2026-11-09'); // 11/09 ～ 12/20

  it('畫面裡的期間：從第幾天開始、佔幾天（含頭尾）', () => {
    expect(placeRange(window, '2026-11-18', '2026-11-28')).toEqual({ col: 9, span: 11, isClippedStart: false, isClippedEnd: false });
  });

  it('只有一天', () => {
    expect(placeRange(window, '2026-11-09', '2026-11-09')).toEqual({ col: 0, span: 1, isClippedStart: false, isClippedEnd: false });
    expect(placeRange(window, '2026-12-20', '2026-12-20')).toEqual({ col: 41, span: 1, isClippedStart: false, isClippedEnd: false });
  });

  it('超出左邊與右邊的在邊上截斷並標示', () => {
    expect(placeRange(window, '2026-11-01', '2026-11-12')).toEqual({ col: 0, span: 4, isClippedStart: true, isClippedEnd: false });
    expect(placeRange(window, '2026-12-15', '2027-01-10')).toEqual({ col: 36, span: 6, isClippedStart: false, isClippedEnd: true });
    expect(placeRange(window, '2026-10-01', '2027-02-01')).toEqual({ col: 0, span: 42, isClippedStart: true, isClippedEnd: true });
  });

  it('完全在畫面外的沒有位置', () => {
    expect(placeRange(window, '2026-11-01', '2026-11-08')).toBeNull();
    expect(placeRange(window, '2026-12-21', '2026-12-25')).toBeNull();
  });
});

describe('日期範圍的寫法', () => {
  it('兩天以上是「月/日～月/日」，同一天只寫一天', () => {
    expect(formatRange('2026-11-18', '2026-11-28')).toBe('11/18～11/28');
    expect(formatRange('2026-11-28', '2026-11-28')).toBe('11/28');
  });
});

describe('stepDaysFor：往前、往後一次移動幾天', () => {
  it('量不到可見寬度（測試環境）時用固定的天數', () => {
    expect(stepDaysFor('week', null)).toBe(28);
    expect(stepDaysFor('month', null)).toBe(91);
  });

  it('用可見的天數減一週，湊成整週（起點一律對齊週一），才不會跳過沒看過的日子', () => {
    // 看得到 25 天：25 - 7 = 18，湊成整週是 14。
    expect(stepDaysFor('week', 25)).toBe(14);
    expect(stepDaysFor('week', 22.9)).toBe(14);
    expect(stepDaysFor('week', 15)).toBe(7);
  });

  it('至少一週，也不超過畫面的天數減一週', () => {
    expect(stepDaysFor('week', 3)).toBe(7);
    expect(stepDaysFor('week', 500)).toBe(35);
  });
});

describe('formatRangeIn：畫面跨年時日期帶年份', () => {
  it('同一年的畫面不帶年份', () => {
    expect(formatRangeIn(windowOf('week', '2026-11-09'), '2026-11-18', '2026-11-28')).toBe('11/18～11/28');
  });

  it('畫面跨年時兩端都帶年份，單日也是', () => {
    const crossing = windowOf('month', '2026-11-09');
    expect(formatRangeIn(crossing, '2026-12-28', '2027-01-05')).toBe('2026/12/28～2027/01/05');
    expect(formatRangeIn(crossing, '2027-01-05', '2027-01-05')).toBe('2027/01/05');
  });
});
