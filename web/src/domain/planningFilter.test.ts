import { describe, expect, it } from 'vitest';
import type { Item } from '../api/types';
import { cycleOf, milestoneOf } from '../test/planning';
import {
  currentCycle,
  cycleDefault,
  cycleQuery,
  DELETED_CYCLE_TEXT,
  DELETED_MILESTONE_TEXT,
  UNKNOWN_CYCLE_TEXT,
  UNKNOWN_MILESTONE_TEXT,
  displayCycleName,
  displayMilestoneName,
  hasNoCurrentCycle,
  matchesCycle,
  matchesMilestone,
  milestoneDefault,
  milestoneQuery,
  parseCycleFilter,
  parseMilestoneFilter,
} from './planningFilter';

const SPRINT_1 = '0190a000-0000-7000-8000-0000000000d1';
const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';
const TODAY = '2026-11-18';

const CYCLES = [
  cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 }),
  cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 }),
];
const MILESTONES = [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })];

describe('網址上的週期與里程碑條件', () => {
  it('認得 current、none 與編號，其他的略過，後端才不會回查詢參數錯誤', () => {
    // Arrange / Act / Assert
    expect(parseCycleFilter('current')).toBe('current');
    expect(parseCycleFilter('none')).toBe('none');
    expect(parseCycleFilter(SPRINT_1)).toBe(SPRINT_1);
    expect(parseCycleFilter('sprint-1')).toBeNull();
    expect(parseCycleFilter('')).toBeNull();
    expect(parseCycleFilter(null)).toBeNull();
  });

  it('編號一律轉成小寫：伺服器回的是小寫，大小寫不同比對不上', () => {
    // Arrange / Act / Assert
    expect(parseCycleFilter(SPRINT_1.toUpperCase())).toBe(SPRINT_1);
    expect(parseMilestoneFilter(` ${V1.toUpperCase()} `)).toBe(V1);
  });

  it('里程碑沒有 current', () => {
    // Arrange / Act / Assert
    expect(parseMilestoneFilter('none')).toBe('none');
    expect(parseMilestoneFilter(V1)).toBe(V1);
    expect(parseMilestoneFilter('current')).toBeNull();
  });
});

describe('目前的週期', () => {
  it('依使用者的今天找，開始日與結束日當天都算', () => {
    // Arrange / Act / Assert
    expect(currentCycle(CYCLES, TODAY)?.id).toBe(SPRINT_2);
    expect(currentCycle(CYCLES, '2026-11-16')?.id).toBe(SPRINT_2);
    expect(currentCycle(CYCLES, '2026-11-29')?.id).toBe(SPRINT_2);
    expect(currentCycle(CYCLES, '2026-11-30')).toBeUndefined();
  });
});

describe('換成 API 的條件', () => {
  it('current 換成今天所在的週期編號', () => {
    // Arrange / Act / Assert
    expect(cycleQuery('current', CYCLES, TODAY)).toEqual({ cycleId: SPRINT_2 });
  });

  it('沒有目前的週期時不產生條件（呼叫端先用 hasNoCurrentCycle 判斷，不送請求；不送假編號）', () => {
    // Arrange / Act / Assert
    expect(hasNoCurrentCycle('current', CYCLES, '2027-01-01')).toBe(true);
    expect(hasNoCurrentCycle('current', CYCLES, TODAY)).toBe(false);
    expect(hasNoCurrentCycle('none', CYCLES, '2027-01-01')).toBe(false);
    expect(hasNoCurrentCycle(null, [], TODAY)).toBe(false);
    expect(cycleQuery('current', CYCLES, '2027-01-01')).toEqual({});
  });

  it('none、編號與沒有條件', () => {
    // Arrange / Act / Assert
    expect(cycleQuery('none', CYCLES, TODAY)).toEqual({ noCycle: true });
    expect(cycleQuery(SPRINT_1, CYCLES, TODAY)).toEqual({ cycleId: SPRINT_1 });
    expect(cycleQuery(null, CYCLES, TODAY)).toEqual({});
    expect(milestoneQuery('none')).toEqual({ noMilestone: true });
    expect(milestoneQuery(V1)).toEqual({ milestoneId: V1 });
    expect(milestoneQuery(null)).toEqual({});
  });
});

describe('快速新增帶的週期與里程碑', () => {
  it('篩了週期（含目前的）就帶那一個；沒有週期、沒有條件、沒有目前的週期都不帶', () => {
    // Arrange / Act / Assert
    expect(cycleDefault('current', CYCLES, TODAY)).toBe(SPRINT_2);
    expect(cycleDefault(SPRINT_1, CYCLES, TODAY)).toBe(SPRINT_1);
    expect(cycleDefault('none', CYCLES, TODAY)).toBeUndefined();
    expect(cycleDefault(null, CYCLES, TODAY)).toBeUndefined();
    expect(cycleDefault('current', CYCLES, '2027-01-01')).toBeUndefined();
    expect(milestoneDefault(V1, MILESTONES)).toBe(V1);
    expect(milestoneDefault('none', MILESTONES)).toBeUndefined();
    expect(milestoneDefault(null, MILESTONES)).toBeUndefined();
  });

  it('編號不在已載入的清單裡（已刪除、清單讀不到）就不帶，免得建出指到不存在的項目', () => {
    // Arrange / Act / Assert
    expect(cycleDefault('0190a000-0000-7000-8000-0000000000ff', CYCLES, TODAY)).toBeUndefined();
    expect(cycleDefault(SPRINT_1, [], TODAY)).toBeUndefined();
    expect(milestoneDefault('0190a000-0000-7000-8000-0000000000ff', MILESTONES)).toBeUndefined();
    expect(milestoneDefault(V1, [])).toBeUndefined();
  });
});

describe('一個項目符不符合週期與里程碑的條件', () => {
  const item = { cycleId: SPRINT_2, milestoneId: null } as Item;

  it('和送給後端的是同一個意思', () => {
    // Arrange / Act / Assert
    expect(matchesCycle(item, 'current', CYCLES, TODAY)).toBe(true);
    expect(matchesCycle(item, SPRINT_1, CYCLES, TODAY)).toBe(false);
    expect(matchesCycle(item, 'none', CYCLES, TODAY)).toBe(false);
    expect(matchesCycle(item, null, CYCLES, TODAY)).toBe(true);
    expect(matchesCycle(item, 'current', CYCLES, '2027-01-01')).toBe(false);
    expect(matchesMilestone(item, 'none')).toBe(true);
    expect(matchesMilestone(item, V1)).toBe(false);
    expect(matchesMilestone(item, null)).toBe(true);
  });
});

describe('名稱的顯示', () => {
  it('用目前的名稱，查不到的是「已刪除的…」', () => {
    // Arrange / Act / Assert
    expect(displayCycleName(SPRINT_1, CYCLES)).toBe('衝刺 1');
    expect(displayCycleName('gone', CYCLES)).toBe(DELETED_CYCLE_TEXT);
    expect(displayMilestoneName(V1, MILESTONES)).toBe('v1.0 上線');
    expect(displayMilestoneName('gone', MILESTONES)).toBe(DELETED_MILESTONE_TEXT);
    expect(DELETED_CYCLE_TEXT).toBe('已刪除的週期');
    expect(DELETED_MILESTONE_TEXT).toBe('已刪除的里程碑');
  });

  it('清單讀不到（null）時用中性的說法，不說成已刪除', () => {
    // Arrange / Act / Assert
    expect(displayCycleName(SPRINT_1, null)).toBe(UNKNOWN_CYCLE_TEXT);
    expect(displayMilestoneName(V1, null)).toBe(UNKNOWN_MILESTONE_TEXT);
    expect(UNKNOWN_CYCLE_TEXT).toBe('某個週期');
    expect(UNKNOWN_MILESTONE_TEXT).toBe('某個里程碑');
  });
});
