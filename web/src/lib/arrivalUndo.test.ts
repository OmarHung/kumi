import { describe, expect, it } from 'vitest';
import { undoOf } from './useArrivalNotice';

describe('換頁提示的「復原」資料', () => {
  it('state 帶了專案代號與編號就是可以復原', () => {
    // Arrange
    const state = { notice: '已刪除 AGY-7「首頁」。', undo: { projectKey: 'AGY', keys: ['AGY-7'] } };

    // Act
    const undo = undoOf(state);

    // Assert
    expect(undo).toEqual({ projectKey: 'AGY', keys: ['AGY-7'] });
  });

  it.each([null, undefined, 'text', { notice: '沒有 undo' }, { undo: null }, { undo: { projectKey: 'AGY' } }, { undo: { projectKey: 1, keys: [] } }, { undo: { projectKey: 'AGY', keys: [1] } }])(
    '沒有或形狀不對（%j）就不能復原',
    (state) => {
      // Arrange / Act
      const undo = undoOf(state);

      // Assert
      expect(undo).toBeNull();
    },
  );
});
