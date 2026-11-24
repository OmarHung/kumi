import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { makeItem } from '../../test/fixtures';
import { myWorkStatus } from './myWorkStatus';
import { rowOf as placedRow, type MyWorkRow } from './useMyWorkRows';

const EARLY = '2026-11-16T08:00:00Z';
const LATE = '2026-11-16T09:00:00Z';

function rowOf(item: Item): MyWorkRow {
  return placedRow(item, item);
}

const open = makeItem({ number: 1 });
const done = makeItem({ number: 3, state: 'done', stateCategory: 'done', updatedAt: EARLY });
const filledDone = makeItem({ number: 4, state: 'done', stateCategory: 'done', updatedAt: LATE });
const filledOpen = makeItem({ number: 4, state: 'doing', stateCategory: 'in_progress', updatedAt: LATE });
const FILLED_DONE = '已填好「到期日」，WEB-4 已移到「完成」。';

describe('myWorkStatus：工具列上一直掛著的那一句', () => {
  it('沒有改成完成、取消的，也沒有補填時是空的', () => {
    // Arrange / Act / Assert
    expect(myWorkStatus([rowOf(open)], null)).toBe('');
  });

  it('最近一個改成完成或取消的：重新整理後會離開清單', () => {
    // Arrange / Act / Assert
    expect(myWorkStatus([rowOf(open), rowOf(done)], null)).toBe('WEB-3 已經完成，重新整理後會離開清單。');
  });

  it('補填之後移到完成：和「重新整理後會離開清單」合成一句，不是兩句', () => {
    // Arrange
    const rows = [rowOf(done), rowOf(filledDone)];

    // Act
    const status = myWorkStatus(rows, { item: filledDone, message: FILLED_DONE });

    // Assert
    expect(status).toBe('已填好「到期日」，WEB-4 已移到「完成」，重新整理後會離開清單。');
  });

  it('列還沒換上移好的那一份（快取晚一步）時也合成一句：看的是補填回來的項目', () => {
    // Arrange
    const before = makeItem({ number: 4, state: 'doing', stateCategory: 'in_progress', updatedAt: EARLY });

    // Act
    const status = myWorkStatus([rowOf(before)], { item: filledDone, message: FILLED_DONE });

    // Assert
    expect(status).toBe('已填好「到期日」，WEB-4 已移到「完成」，重新整理後會離開清單。');
  });

  it('補填之後移到還沒結束的狀態：只說補填的結果', () => {
    // Arrange
    const message = '已填好「到期日」，WEB-4 已移到「進行中」。';

    // Act
    const status = myWorkStatus([rowOf(done), rowOf(filledOpen)], { item: filledOpen, message });

    // Assert
    expect(status).toBe(message);
  });

  it('補填之後又有別的項目改成完成：說最新的那一個', () => {
    // Arrange
    const laterDone = makeItem({ number: 5, state: 'done', stateCategory: 'done', updatedAt: '2026-11-16T10:00:00Z' });

    // Act
    const status = myWorkStatus([rowOf(filledDone), rowOf(laterDone)], { item: filledDone, message: FILLED_DONE });

    // Assert
    expect(status).toBe('WEB-5 已經完成，重新整理後會離開清單。');
  });

  it('補填的那一列之後又改過（例如改回進行中）：不再說補填的結果', () => {
    // Arrange
    const reverted = makeItem({ number: 4, state: 'doing', stateCategory: 'in_progress', updatedAt: '2026-11-16T10:00:00Z' });

    // Act
    const status = myWorkStatus([rowOf(reverted)], { item: filledDone, message: FILLED_DONE });

    // Assert
    expect(status).toBe('');
  });

  it('補填的項目不在清單上（面板裡換到別的項目）：只說補填的結果，不說會離開清單', () => {
    // Arrange / Act
    const status = myWorkStatus([rowOf(open)], { item: filledDone, message: FILLED_DONE });

    // Assert
    expect(status).toBe(FILLED_DONE);
  });
});

describe('myWorkStatus：還符合條件的完成項目不說會離開清單', () => {
  const stays = () => false;

  it('完成的項目還符合條件（例如「所有狀態」）：什麼都不說', () => {
    // Arrange / Act / Assert
    expect(myWorkStatus([rowOf(open), rowOf(done)], null, stays)).toBe('');
  });

  it('補填之後移到完成、還符合條件：只說補填的結果', () => {
    // Arrange
    const rows = [rowOf(filledDone)];

    // Act
    const status = myWorkStatus(rows, { item: filledDone, message: FILLED_DONE }, stays);

    // Assert
    expect(status).toBe(FILLED_DONE);
  });

  it('只說真的會離開的那一個', () => {
    // Arrange
    const leaves = (item: Item) => item.key === 'WEB-4';
    const rows = [rowOf(done), rowOf(filledDone)];

    // Act
    const status = myWorkStatus(rows, null, leaves);

    // Assert
    expect(status).toBe('WEB-4 已經完成，重新整理後會離開清單。');
  });
});
