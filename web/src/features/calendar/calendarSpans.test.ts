import { describe, expect, it } from 'vitest';
import { agyItem } from '../../test/fixtures';
import { monthGrid } from './calendarMonth';
import { formatSpanRange, itemSequence, layoutWeeks, spanOf, SPAN_LANE_BUDGET } from './calendarSpans';

// 2026-11 從週一開始：第 1 週 10/26～11/01、第 2 週 11/02～11/08、第 3 週 11/09～11/15。
const WEEKS = monthGrid('2026-11', 'monday').weeks;

function item(number: number, startDate: string | null, dueDate: string | null, extra: Parameters<typeof agyItem>[0] = {}) {
  return agyItem({ number, title: `項目${String(number)}`, startDate, dueDate, ...extra });
}

describe('spanOf：一個項目佔哪幾天', () => {
  it.each([
    ['有開始日與到期日：從開始到到期', '2026-11-02', '2026-11-06', { start: '2026-11-02', end: '2026-11-06' }],
    ['只有到期日：那一天', null, '2026-11-06', { start: '2026-11-06', end: '2026-11-06' }],
    ['只有開始日：開始那一天', '2026-11-02', null, { start: '2026-11-02', end: '2026-11-02' }],
    ['開始日晚於到期日（資料有誤）：當成只有到期日', '2026-11-09', '2026-11-06', { start: '2026-11-06', end: '2026-11-06' }],
    ['兩個日期都沒有：不在月曆上', null, null, null],
  ])('%s', (_name, startDate, dueDate, expected) => {
    // Arrange / Act
    const span = spanOf(item(1, startDate, dueDate));

    // Assert
    expect(span).toEqual(expected);
  });
});

describe('formatSpanRange：連結名稱裡的日期範圍', () => {
  it('同一年不帶年份，月與日不補零', () => {
    expect(formatSpanRange({ start: '2026-11-02', end: '2026-11-06' })).toBe('11/2～11/6');
  });

  it('跨年時兩邊都帶年份', () => {
    expect(formatSpanRange({ start: '2026-12-28', end: '2027-01-03' })).toBe('2026/12/28～2027/1/3');
  });
});

describe('layoutWeeks：每一週的條', () => {
  it('同一週裡的條：位置是第幾天開始、佔幾天，兩端都沒有接續', () => {
    // Arrange
    const items = [item(1, '2026-11-03', '2026-11-05')];

    // Act
    const [, week2] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week2?.segments).toMatchObject([{ col: 1, length: 3, lane: 0, continuesBefore: false, continuesAfter: false, isBar: true }]);
  });

  it('跨週的項目每一週各一段：接續的那一端標出來，只有第一段在 Tab 的順序裡', () => {
    // Arrange
    const items = [item(1, '2026-11-06', '2026-11-11')];

    // Act
    const [, week2, week3] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week2?.segments).toMatchObject([{ col: 4, length: 3, continuesBefore: false, continuesAfter: true, isTabbable: true }]);
    expect(week3?.segments).toMatchObject([{ col: 0, length: 3, continuesBefore: true, continuesAfter: false, isTabbable: false }]);
  });

  it('項目在畫面之前就開始：第一個畫出來的那一段就是可以 Tab 的那一段，左端仍是接續', () => {
    // Arrange
    const items = [item(1, '2026-10-20', '2026-10-27')];

    // Act
    const [week1] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week1?.segments).toMatchObject([{ col: 0, length: 2, continuesBefore: true, isTabbable: true }]);
  });

  it('單天的項目也走車道，但不是條', () => {
    // Arrange
    const items = [item(1, null, '2026-11-04'), item(2, '2026-11-05', null)];

    // Act
    const [, week2] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week2?.segments).toMatchObject([
      { col: 2, length: 1, lane: 0, isBar: false },
      { col: 3, length: 1, lane: 0, isBar: false },
    ]);
  });

  it('車道：不重疊的共用一條，重疊的往下排', () => {
    // Arrange
    const items = [item(1, '2026-11-02', '2026-11-04'), item(2, '2026-11-03', '2026-11-05'), item(3, '2026-11-05', '2026-11-06')];

    // Act
    const [, week2] = layoutWeeks(WEEKS, items);

    // Assert
    const lanes = Object.fromEntries((week2?.segments ?? []).map((segment) => [segment.item.number, segment.lane]));
    expect(lanes).toEqual({ 1: 0, 2: 1, 3: 0 });
  });

  it('先開始的在上面；一樣早開始的，長的在上面', () => {
    // Arrange
    const items = [item(1, '2026-11-03', '2026-11-03'), item(2, '2026-11-02', '2026-11-03'), item(3, '2026-11-02', '2026-11-06')];

    // Act
    const [, week2] = layoutWeeks(WEEKS, items);

    // Assert
    const lanes = Object.fromEntries((week2?.segments ?? []).map((segment) => [segment.item.number, segment.lane]));
    expect(lanes).toEqual({ 3: 0, 2: 1, 1: 2 });
  });

  it('接續上一週的排在最上面（一樣是第一天開始，開始得比較早的在前）', () => {
    // Arrange
    const items = [item(1, '2026-11-09', '2026-11-10'), item(2, '2026-11-05', '2026-11-10')];

    // Act
    const [, , week3] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week3?.segments.map((segment) => [segment.item.number, segment.lane])).toEqual([
      [2, 0],
      [1, 1],
    ]);
  });

  it('超過車道的預算：多出來的不畫，那幾天記下收起來幾個', () => {
    // Arrange
    const items = [1, 2, 3, 4, 5].map((number) => item(number, '2026-11-03', '2026-11-04'));
    const lonely = item(6, '2026-11-06', '2026-11-06');

    // Act
    const [, week2] = layoutWeeks(WEEKS, [...items, lonely]);

    // Assert
    expect(SPAN_LANE_BUDGET).toBe(3);
    expect(week2?.segments.map((segment) => segment.item.number)).toEqual([1, 2, 3, 6]);
    expect(week2?.days.map((day) => day.hiddenCount)).toEqual([0, 2, 2, 0, 0, 0, 0]);
  });

  it('第一段被收起來時，Tab 的順序留給下一個畫得出來的那一段', () => {
    // Arrange：前三條佔滿 11/05～11/06 的車道，項目 4 在第 2 週被收起來、第 3 週畫得出來。
    const items = [
      item(1, '2026-11-05', '2026-11-06'),
      item(2, '2026-11-05', '2026-11-06'),
      item(3, '2026-11-05', '2026-11-06'),
      item(4, '2026-11-06', '2026-11-10'),
    ];

    // Act
    const [, week2, week3] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week2?.segments.map((segment) => segment.item.number)).toEqual([1, 2, 3]);
    expect(week3?.segments).toMatchObject([{ item: { number: 4 }, isTabbable: true, continuesBefore: true }]);
  });

  it('浮層用的整天清單：那一天碰到的全部項目（含收起來的），依車道排，範圍只有條才有', () => {
    // Arrange
    const items = [1, 2, 3, 4].map((number) => item(number, '2026-11-03', number === 4 ? '2026-11-03' : '2026-11-04'));

    // Act
    const [, week2] = layoutWeeks(WEEKS, items);

    // Assert
    expect(week2?.days[1]?.entries.map((entry) => [entry.item.number, entry.range])).toEqual([
      [1, '11/3～11/4'],
      [2, '11/3～11/4'],
      [3, '11/3～11/4'],
      [4, null],
    ]);
    expect(week2?.days[0]?.entries).toEqual([]);
  });

  it('沒有任何日期的項目沒有位置', () => {
    // Act
    const layout = layoutWeeks(WEEKS, [item(1, null, null)]);

    // Assert
    expect(layout.every((week) => week.segments.length === 0)).toBe(true);
  });
});

describe('layoutWeeks：一週從週日開始與跨年', () => {
  it('一週從週日開始：條的起點與切段照週日算（11/06 週五～11/09 週一跨過週六日）', () => {
    // Arrange
    const sundayWeeks = monthGrid('2026-11', 'sunday').weeks;
    const items = [item(1, '2026-11-06', '2026-11-09')];

    // Act
    const [week1, week2] = layoutWeeks(sundayWeeks, items);

    // Assert：第 1 週 11/01（日）～11/07（六），第 2 週 11/08（日）～11/14（六）。
    expect(week1?.segments).toMatchObject([{ col: 5, length: 2, continuesBefore: false, continuesAfter: true, isTabbable: true }]);
    expect(week2?.segments).toMatchObject([{ col: 0, length: 2, continuesBefore: true, continuesAfter: false, isTabbable: false }]);
  });

  it('跨年的那一週（12/28～1/3）：一段一週，範圍的說法帶年份', () => {
    // Arrange
    const decemberWeeks = monthGrid('2026-12', 'monday').weeks;
    const items = [item(1, '2026-12-30', '2027-01-05'), item(2, '2026-12-28', '2027-01-03')];

    // Act
    const layout = layoutWeeks(decemberWeeks, items);
    const lastWeek = layout.at(-1);

    // Assert
    expect(decemberWeeks.at(-1)).toEqual(['2026-12-28', '2026-12-29', '2026-12-30', '2026-12-31', '2027-01-01', '2027-01-02', '2027-01-03']);
    expect(lastWeek?.segments).toMatchObject([
      { item: { number: 2 }, col: 0, length: 7, continuesBefore: false, continuesAfter: false, range: '2026/12/28～2027/1/3' },
      { item: { number: 1 }, col: 2, length: 5, continuesBefore: false, continuesAfter: true, range: '2026/12/30～2027/1/5' },
    ]);
  });
});

describe('itemSequence：面板的上一個、下一個', () => {
  it('照開始日（只有到期日的用到期日），同一天再照編號，每個項目只列一次', () => {
    // Arrange
    const items = [item(3, '2026-11-05', '2026-11-12'), item(1, null, '2026-11-02'), item(2, '2026-11-05', null), item(4, null, null)];

    // Act
    const keys = itemSequence(items);

    // Assert
    expect(keys).toEqual(['AGY-1', 'AGY-2', 'AGY-3']);
  });
});
