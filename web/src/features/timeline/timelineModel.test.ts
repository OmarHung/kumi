import { describe, expect, it } from 'vitest';
import { makeItem } from '../../test/fixtures';
import { cycleOf, milestoneOf } from '../../test/planning';
import { cycleSegments, dayCells, milestoneMarks, monthCells, timelineRows } from './timelineModel';
import { windowOf } from './timelineRange';

const TODAY = '2026-11-18';
const WEEK = windowOf('week', '2026-11-09'); // 11/09 ～ 12/20
const MONTH = windowOf('month', '2026-11-09');

describe('timelineRows：項目變成一列', () => {
  it('有開始日與到期日是條，只有一個是點，名稱裡有日期', () => {
    // Arrange
    const items = [
      makeItem({ number: 1, title: '首頁', startDate: '2026-11-18', dueDate: '2026-11-28' }),
      makeItem({ number: 2, title: '只有到期', dueDate: '2026-11-20' }),
      makeItem({ number: 3, title: '只有開始', startDate: '2026-11-21' }),
      makeItem({ number: 4, title: '同一天', startDate: '2026-11-22', dueDate: '2026-11-22' }),
    ];

    // Act
    const rows = timelineRows(items, WEEK, TODAY);

    // Assert
    expect(rows.map((row) => [row.item.number, row.kind, row.name])).toEqual([
      [1, 'bar', 'WEB-1 首頁，11/18～11/28'],
      [2, 'point', 'WEB-2 只有到期，11/20'],
      [3, 'point', 'WEB-3 只有開始，11/21'],
      [4, 'bar', 'WEB-4 同一天，11/22'],
    ]);
  });

  it('照開始日（沒有就用到期日）排，同一天照編號', () => {
    // Arrange
    const items = [
      makeItem({ number: 5, dueDate: '2026-11-15' }),
      makeItem({ number: 9, startDate: '2026-11-12', dueDate: '2026-11-30' }),
      makeItem({ number: 2, startDate: '2026-11-15', dueDate: '2026-11-16' }),
      makeItem({ number: 3, dueDate: '2026-11-12' }),
    ];

    // Act
    const rows = timelineRows(items, WEEK, TODAY);

    // Assert
    expect(rows.map((row) => row.item.number)).toEqual([3, 9, 2, 5]);
  });

  it('截斷的標出往哪邊還有，完全在畫面外的不畫', () => {
    // Arrange
    const items = [
      makeItem({ number: 1, startDate: '2026-10-20', dueDate: '2026-11-12' }),
      makeItem({ number: 2, startDate: '2026-12-18', dueDate: '2027-01-05' }),
      makeItem({ number: 3, startDate: '2026-01-01', dueDate: '2026-01-05' }),
    ];

    // Act
    const rows = timelineRows(items, WEEK, TODAY);

    // Assert
    expect(rows.map((row) => [row.item.number, row.placement.isClippedStart, row.placement.isClippedEnd])).toEqual([
      [1, true, false],
      [2, false, true],
    ]);
  });

  it('過期的（到期日在今天以前、還沒完成）標出來，名稱最後多一句；完成的不算', () => {
    // Arrange
    const items = [
      makeItem({ number: 1, dueDate: '2026-11-10' }),
      makeItem({ number: 2, dueDate: '2026-11-10', state: 'done', stateCategory: 'done' }),
      makeItem({ number: 3, dueDate: '2026-11-18' }),
    ];

    // Act
    const rows = timelineRows(items, WEEK, TODAY);

    // Assert
    expect(rows.map((row) => row.isOverdue)).toEqual([true, false, false]);
    expect(rows[0]?.name).toBe('WEB-1 首頁設計稿，11/10，已過期');
  });

  it('開始日比到期日晚的資料：當成同一段，不會畫出負的長度', () => {
    // Arrange
    const items = [makeItem({ number: 1, startDate: '2026-11-20', dueDate: '2026-11-15' })];

    // Act
    const [row] = timelineRows(items, WEEK, TODAY);

    // Assert
    expect(row?.placement).toMatchObject({ col: 6, span: 6 });
    expect(row?.name).toBe('WEB-1 首頁設計稿，11/15～11/20，已過期');
  });
});

describe('表頭的刻度', () => {
  it('月份：每個月一格，頭尾的月份只佔畫面裡有的天數', () => {
    expect(monthCells(WEEK)).toEqual([
      { key: '2026-11', label: '2026 年 11 月', col: 0, span: 22, isToday: false },
      { key: '2026-12', label: '2026 年 12 月', col: 22, span: 20, isToday: false },
    ]);
  });

  it('週的刻度每天一格，今天標出來；月的刻度每週一格，寫週一的日期', () => {
    // Act
    const days = dayCells(WEEK, TODAY);
    const weeks = dayCells(MONTH, TODAY);

    // Assert
    expect(days).toHaveLength(42);
    expect(days[0]).toMatchObject({ label: '9', col: 0, span: 1, isToday: false });
    expect(days.filter((cell) => cell.isToday).map((cell) => cell.label)).toEqual(['18']);
    expect(weeks).toHaveLength(26);
    expect(weeks[0]).toMatchObject({ label: '9', col: 0, span: 7 });
    expect(weeks.filter((cell) => cell.isToday).map((cell) => cell.col)).toEqual([7]);
  });
});

describe('週期與里程碑', () => {
  it('週期：和畫面重疊的才畫，超出的截斷；字是名稱加範圍', () => {
    // Arrange
    const cycles = [
      cycleOf('衝刺 1', '2026-10-27', '2026-11-15'),
      cycleOf('衝刺 2', '2026-11-16', '2026-11-29'),
      cycleOf('衝刺 0', '2026-09-01', '2026-09-14'),
    ];

    // Act
    const segments = cycleSegments(cycles, WEEK);

    // Assert
    expect(
      segments.map((segment) => [segment.cycle.name, segment.placement.col, segment.placement.span, segment.placement.isClippedStart]),
    ).toEqual([
      ['衝刺 1', 0, 7, true],
      ['衝刺 2', 7, 14, false],
    ]);
    expect(segments[1]?.range).toBe('11/16～11/29');
  });

  it('里程碑：在到期日那一天，名稱可以用到下一個里程碑之前的寬度', () => {
    // Arrange
    const milestones = [
      milestoneOf('上線', '2026-12-01'),
      milestoneOf('設計定稿', '2026-11-20'),
      milestoneOf('去年的', '2025-12-01'),
    ];

    // Act
    const marks = milestoneMarks(milestones, WEEK);

    // Assert
    expect(marks.map((mark) => [mark.milestone.name, mark.col, mark.labelSpan])).toEqual([
      ['設計定稿', 11, 11],
      ['上線', 22, 20],
    ]);
  });
});

describe('timelineRows：提示文字與跨年', () => {
  it('title 是滑過條時看到的字：編號 標題，日期（過期的不加字，過期有自己的標示）', () => {
    // Arrange
    const items = [makeItem({ number: 1, title: '首頁設計稿', startDate: '2026-11-18', dueDate: '2026-11-28' })];

    // Act
    const [row] = timelineRows(items, WEEK, TODAY);

    // Assert
    expect(row?.title).toBe('WEB-1 首頁設計稿，11/18～11/28');
    expect(row?.range).toBe('11/18～11/28');
  });

  it('畫面跨年（月刻度的半年）時，名稱裡的日期帶年份', () => {
    // Arrange
    const items = [makeItem({ number: 5, title: '跨年', startDate: '2026-12-28', dueDate: '2027-01-05' })];

    // Act
    const [row] = timelineRows(items, MONTH, TODAY);

    // Assert
    expect(row?.name).toBe('WEB-5 跨年，2026/12/28～2027/01/05');
    expect(row?.title).toBe('WEB-5 跨年，2026/12/28～2027/01/05');
  });

  it('週期的範圍也跟著帶年份', () => {
    // Arrange & Act
    const [segment] = cycleSegments([cycleOf('衝刺', '2026-12-28', '2027-01-10')], MONTH);

    // Assert
    expect(segment?.range).toBe('2026/12/28～2027/01/10');
  });
});
