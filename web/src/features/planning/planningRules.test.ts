import { describe, expect, it } from 'vitest';
import { cycleOf, milestoneOf } from '../../test/planning';
import { cycleStatusOf, defaultCycleDraft, isMilestoneOverdue, validateCycle, validateMilestone } from './planningRules';

describe('週期的狀態', () => {
  it('今天在開始日與結束日之間（含頭尾）是進行中', () => {
    // Arrange
    const cycle = cycleOf('衝刺 1', '2026-11-16', '2026-11-29');

    // Act & Assert
    expect(cycleStatusOf(cycle, '2026-11-16')).toBe('active');
    expect(cycleStatusOf(cycle, '2026-11-29')).toBe('active');
  });

  it('開始日之前是之後，結束日之後是已結束', () => {
    const cycle = cycleOf('衝刺 1', '2026-11-16', '2026-11-29');

    expect(cycleStatusOf(cycle, '2026-11-15')).toBe('upcoming');
    expect(cycleStatusOf(cycle, '2026-11-30')).toBe('ended');
  });
});

describe('新增週期的預設值', () => {
  it('沒有週期時：衝刺 1，今天開始，13 天後結束', () => {
    expect(defaultCycleDraft([], '2026-11-18')).toEqual({ name: '衝刺 1', startDate: '2026-11-18', endDate: '2026-12-01' });
  });

  it('有週期時：名稱是數量加一，開始日是最後一個結束的隔天', () => {
    // Arrange
    const cycles = [cycleOf('衝刺 1', '2026-11-02', '2026-11-15'), cycleOf('衝刺 2', '2026-11-16', '2026-11-29')];

    // Act
    const draft = defaultCycleDraft(cycles, '2026-11-18');

    // Assert
    expect(draft).toEqual({ name: '衝刺 3', startDate: '2026-11-30', endDate: '2026-12-13' });
  });

  it('刪過中間的一個之後不撞名：從數量加一往上找第一個沒被用過的名稱', () => {
    // Arrange：衝刺 2 刪掉了，剩兩個，數量加一是 3，已經有了
    const cycles = [cycleOf('衝刺 1', '2026-11-02', '2026-11-15'), cycleOf('衝刺 3', '2026-11-16', '2026-11-29')];

    // Act
    const draft = defaultCycleDraft(cycles, '2026-11-18');

    // Assert
    expect(draft.name).toBe('衝刺 4');
  });

  it('比對名稱不分大小寫', () => {
    // Arrange
    const cycles = [cycleOf('衝刺 1', '2026-11-02', '2026-11-15'), cycleOf('衝刺 2', '2026-11-16', '2026-11-29')];

    // Act & Assert：伺服器的唯一索引不分大小寫，預設名稱也不能只差大小寫
    expect(defaultCycleDraft([cycleOf('衝刺 1', '2026-11-02', '2026-11-15')], '2026-11-18').name).toBe('衝刺 2');
    expect(defaultCycleDraft(cycles, '2026-11-18').name).toBe('衝刺 3');
  });

  it('週期排序不照開始日給時，仍取結束日最晚的那一個', () => {
    const cycles = [cycleOf('B', '2026-11-16', '2026-11-29'), cycleOf('A', '2026-11-02', '2026-11-15')];

    expect(defaultCycleDraft(cycles, '2026-11-18').startDate).toBe('2026-11-30');
  });
});

describe('里程碑的已過期', () => {
  const done = { total: 4, done: 3, canceled: 1, estimateTotal: null, estimateDone: null };
  const open = { total: 4, done: 2, canceled: 1, estimateTotal: null, estimateDone: null };

  it('過了到期日、還有項目沒結束：已過期', () => {
    expect(isMilestoneOverdue(milestoneOf('v1', '2026-11-17', { progress: open }), '2026-11-18')).toBe(true);
  });

  it('到期日當天還不算過期', () => {
    expect(isMilestoneOverdue(milestoneOf('v1', '2026-11-18', { progress: open }), '2026-11-18')).toBe(false);
  });

  it('項目都完成或取消了就不算過期；沒有項目也不算', () => {
    expect(isMilestoneOverdue(milestoneOf('v1', '2026-11-01', { progress: done }), '2026-11-18')).toBe(false);
    expect(isMilestoneOverdue(milestoneOf('v1', '2026-11-01'), '2026-11-18')).toBe(false);
  });
});

describe('驗證週期', () => {
  it('名稱空白、日期沒填、結束早於開始都有各自的訊息', () => {
    expect(validateCycle({ name: ' ', startDate: '', endDate: '' })).toEqual({
      name: '請輸入週期的名稱。',
      startDate: '請選開始日。',
      endDate: '請選結束日。',
    });
    expect(validateCycle({ name: 'A', startDate: '2026-11-10', endDate: '2026-11-09' })).toEqual({ endDate: '結束日不能早於開始日。' });
  });

  it('名稱最多 50 個字', () => {
    expect(validateCycle({ name: 'a'.repeat(51), startDate: '2026-01-01', endDate: '2026-01-02' }).name).toBe('名稱最多 50 個字。');
  });

  it('頭尾都算，最長 365 天：結束日減開始日最多 364', () => {
    // Arrange：2026-01-01 到 2026-12-31 含頭尾剛好 365 天；到 2027-01-01 是 366 天
    expect(validateCycle({ name: 'A', startDate: '2026-01-01', endDate: '2026-12-31' })).toEqual({});
    expect(validateCycle({ name: 'A', startDate: '2026-01-01', endDate: '2027-01-01' })).toEqual({ endDate: '一個週期最長 365 天。' });
    expect(validateCycle({ name: 'A', startDate: '2026-01-01', endDate: '2027-01-02' })).toEqual({ endDate: '一個週期最長 365 天。' });
    expect(validateCycle({ name: 'A', startDate: '2026-01-01', endDate: '2026-01-01' })).toEqual({});
  });

  it('日期打得不成樣子時說要選日期', () => {
    expect(validateCycle({ name: 'A', startDate: 'abc', endDate: '2026-01-02' }).startDate).toBe('請選開始日。');
  });
});

describe('驗證里程碑', () => {
  it('名稱最多 100 個字、說明最多 2,000 個字、到期日必填', () => {
    expect(validateMilestone({ name: '', dueDate: '', description: null })).toEqual({ name: '請輸入里程碑的名稱。', dueDate: '請選到期日。' });
    expect(validateMilestone({ name: 'a'.repeat(101), dueDate: '2026-11-18', description: null }).name).toBe('名稱最多 100 個字。');
    expect(validateMilestone({ name: 'a', dueDate: '2026-11-18', description: 'x'.repeat(2001) }).description).toBe('說明最多 2,000 個字。');
  });
});
