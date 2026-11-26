import { describe, expect, it } from 'vitest';
import { smkItem } from '../../test/fixtures';
import { conditionsOf, dependsOnServer, touchesConditions } from './listConditions';

describe('停滯清單的條件（updatedBefore）', () => {
  it('從查詢鍵讀得到，而且算「由伺服器判斷」：前端不猜哪個項目該進這份清單', () => {
    // Arrange
    const key = ['project', 'SMK', 'items', { updatedBefore: '2026-11-12T08:30:00.000Z' }];

    // Act
    const conditions = conditionsOf(key);

    // Assert
    expect(conditions.updatedBefore).toBe('2026-11-12T08:30:00.000Z');
    expect(dependsOnServer(conditions)).toBe(true);
  });

  it('項目一改更新時間就變，視為動到清單的條件（改過的項目該離開停滯的清單）', () => {
    // Arrange
    const conditions = conditionsOf(['project', 'SMK', 'items', { updatedBefore: '2026-11-12T08:30:00.000Z' }]);
    const before = smkItem({ number: 1, updatedAt: '2026-10-01T00:00:00Z' });
    const after = { ...before, updatedAt: '2026-11-26T00:00:00Z' };

    // Act / Assert
    expect(touchesConditions(conditions, before, after)).toBe(true);
    expect(touchesConditions(conditions, before, before)).toBe(false);
  });
});
