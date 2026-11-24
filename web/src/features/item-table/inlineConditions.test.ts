import { describe, expect, it } from 'vitest';
import { COLLAPSE_ORDER, groupPlacement, inlineBarMinRem, inlineConditionKeys, type CollapsibleCondition } from './inlineConditions';

const ALL: readonly CollapsibleCondition[] = ['type', 'state', 'assignee', 'due', 'priority'];

function keysAt(widthRem: number | null, options: { present?: readonly CollapsibleCondition[]; fixedCount?: number; hasPlanning?: boolean } = {}) {
  const { present = ALL, fixedCount = 1, hasPlanning = false } = options;
  return [...inlineConditionKeys(widthRem, { present, fixedCount, hasPlanning })].sort();
}

describe('收起來的先後', () => {
  it('依序是優先度、到期、類型、狀態、負責人（搜尋不在裡面，一律留在列上）', () => {
    // Arrange / Act / Assert
    expect(COLLAPSE_ORDER).toEqual(['priority', 'due', 'type', 'state', 'assignee']);
  });
});

describe('需要的寬度', () => {
  it('沒有收起來的欄位時：每個欄位 8rem 加 0.5rem 間距，加預留的「清除條件」5rem', () => {
    // Arrange / Act / Assert：搜尋加五個條件 = 6 × 8.5 + 5
    expect(inlineBarMinRem(6, false)).toBe(56);
  });

  it('有收起來的欄位時再加「更多條件」按鈕的 7rem', () => {
    // Arrange / Act / Assert：搜尋加四個條件 = 5 × 8.5 + 5 + 7
    expect(inlineBarMinRem(5, true)).toBe(54.5);
  });
});

describe('依列的寬度決定留在列上的欄位', () => {
  it('量不到寬度（null）時全部留在列上', () => {
    expect(keysAt(null)).toEqual([...ALL].sort());
  });

  it('放得下全部（不需要「更多條件」）時全部留在列上', () => {
    expect(keysAt(56)).toEqual([...ALL].sort());
  });

  it('差一點放不下：先收優先度；收了之後要多算「更多條件」', () => {
    // Arrange / Act / Assert：56 以下先收優先度，剩五個欄位加按鈕要 54.5
    expect(keysAt(55.9)).toEqual(['assignee', 'due', 'state', 'type']);
    expect(keysAt(54.5)).toEqual(['assignee', 'due', 'state', 'type']);
  });

  it('再窄就接著收到期，然後類型、狀態、負責人', () => {
    expect(keysAt(54.4)).toEqual(['assignee', 'state', 'type']);
    expect(keysAt(45.9)).toEqual(['assignee', 'state']);
    expect(keysAt(37.4)).toEqual(['assignee']);
    expect(keysAt(29)).toEqual(['assignee']);
    expect(keysAt(28.9)).toEqual([]);
  });

  it('窄到放不下任何條件時只剩搜尋', () => {
    expect(keysAt(0)).toEqual([]);
  });

  it('專案有週期或里程碑時，「更多條件」一直在，每個寬度都多算它的 7rem', () => {
    // Arrange / Act / Assert：全部留在列上要 56 + 7
    expect(keysAt(62.9, { hasPlanning: true })).toEqual(['assignee', 'due', 'state', 'type']);
    expect(keysAt(63, { hasPlanning: true })).toEqual([...ALL].sort());
  });

  it('只有一種類型的專案沒有「類型」：列上少一個欄位', () => {
    // Arrange
    const present: readonly CollapsibleCondition[] = ['state', 'assignee', 'due', 'priority'];

    // Act / Assert：搜尋加四個條件 = 5 × 8.5 + 5 = 47.5
    expect(keysAt(47.5, { present })).toEqual(['assignee', 'due', 'priority', 'state']);
    expect(keysAt(47.4, { present })).toEqual(['assignee', 'due', 'state']);
  });

  it('看板的流程與搜尋是固定的欄位，不會收起來，但佔寬度', () => {
    // Arrange
    const present: readonly CollapsibleCondition[] = ['assignee', 'due', 'priority'];

    // Act / Assert：流程、搜尋加三個條件 = 5 × 8.5 + 5 = 47.5
    expect(keysAt(47.5, { present, fixedCount: 2 })).toEqual(['assignee', 'due', 'priority']);
    expect(keysAt(47.4, { present, fixedCount: 2 })).toEqual(['assignee', 'due']);
  });
});

describe('看板的「分組」最後才收', () => {
  // 1024 寬、側欄展開時欄位那一組約 427px（26.7rem）；1280 寬約 42.7rem。看板：負責人、到期、優先度可以收，搜尋與流程固定。
  const board = { present: ['assignee', 'due', 'priority'] as const, fixedCount: 2, hasPlanning: true };

  it('篩選全部收起來之後還放不下，分組也收進「更多條件」', () => {
    // Arrange / Act
    const placement = groupPlacement(26.7, board);

    // Assert
    expect(placement.inline).toEqual(new Set());
    expect(placement.isGroupInline).toBe(false);
  });

  it('放得下就留在列上：篩選先收，分組不收', () => {
    // Arrange / Act
    const placement = groupPlacement(42.7, board);

    // Assert：搜尋、流程、分組三個固定的 25.5rem，加清除與更多 12rem，37.5rem 放得下，條件全部收掉
    expect(placement.isGroupInline).toBe(true);
    expect(placement.inline).toEqual(new Set());
  });

  it('量不到寬度時留在列上', () => {
    // Arrange / Act / Assert
    expect(groupPlacement(null, board).isGroupInline).toBe(true);
  });
});
