import { describe, expect, it } from 'vitest';
import { milestoneOf } from '../../test/planning';
import { markersByDay } from './calendarData';
import { markerRowsOfDay, splitSpanRows } from './dayRows';

const MARKERS = markersByDay([], [milestoneOf('v1.0 上線', '2026-11-20'), milestoneOf('v1.1 上線', '2026-11-20')]).get('2026-11-20');

describe('splitSpanRows：期間排法每一格底下的一行', () => {
  const rows = markerRowsOfDay(MARKERS ?? { milestones: [], cycleStarts: [], cycleEnds: [] });

  it('沒有標示、沒有收起來的項目：什麼都不畫', () => {
    expect(splitSpanRows([], 0)).toEqual({ shown: [], hiddenCount: 0 });
  });

  it('一個標示、沒有收起來的項目：照畫', () => {
    expect(splitSpanRows(rows.slice(0, 1), 0)).toEqual({ shown: rows.slice(0, 1), hiddenCount: 0 });
  });

  it('只有收起來的項目：整行是「還有 N 個」', () => {
    expect(splitSpanRows([], 2)).toEqual({ shown: [], hiddenCount: 2 });
  });

  it('標示與收起來的項目一起放不下：標示也算進 N', () => {
    expect(splitSpanRows(rows.slice(0, 1), 2)).toEqual({ shown: [], hiddenCount: 3 });
    expect(splitSpanRows(rows, 0)).toEqual({ shown: [], hiddenCount: 2 });
  });
});
