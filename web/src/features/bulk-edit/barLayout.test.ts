import { describe, expect, it } from 'vitest';
import { collapsedActions, inlineActionKeys, type FieldActionKey } from './barLayout';

const ALL: readonly FieldActionKey[] = ['state', 'assignee', 'priority', 'due', 'cycle', 'milestone'];
const OPTIONS = { hasDelete: true, hasNote: false };

describe('批次動作列：放不下時欄位動作依序收進「更多動作」', () => {
  it('量不到寬度（null）當成放得下：全部排在列上', () => {
    // Arrange / Act
    const inline = inlineActionKeys(null, ALL, OPTIONS);

    // Assert
    expect([...inline]).toEqual(ALL);
  });

  it('夠寬：全部排在列上，沒有「更多動作」', () => {
    // Arrange / Act
    const inline = inlineActionKeys(70, ALL, OPTIONS);

    // Assert
    expect(collapsedActions(inline, ALL)).toEqual([]);
  });

  it('1024 寬（動作列約 47rem）、專案有週期與里程碑：里程碑、週期、到期日先收，狀態與負責人、優先度留在列上', () => {
    // Arrange / Act
    const inline = inlineActionKeys(47, ALL, OPTIONS);

    // Assert
    expect(collapsedActions(inline, ALL)).toEqual(['milestone', 'cycle', 'due']);
    expect([...inline]).toEqual(['state', 'assignee', 'priority']);
  });

  it('收的順序是里程碑、週期、到期日、優先度、負責人、狀態；窄到極限時只剩「更多動作」', () => {
    // Arrange / Act
    const order = [60, 50, 44, 40, 36, 32, 28, 10].map((width) => collapsedActions(inlineActionKeys(width, ALL, OPTIONS), ALL));

    // Assert
    expect(order.at(-1)).toEqual(['milestone', 'cycle', 'due', 'priority', 'assignee', 'state']);
    expect(order.every((collapsed, index) => index === 0 || collapsed.length >= (order[index - 1]?.length ?? 0))).toBe(true);
  });

  it('只收沒有的不算：沒有週期與里程碑的專案，present 沒有它們，同樣的寬度收得少', () => {
    // Arrange
    const present: readonly FieldActionKey[] = ['state', 'assignee', 'priority', 'due'];

    // Act
    const collapsed = collapsedActions(inlineActionKeys(47, present, OPTIONS), present);

    // Assert
    expect(collapsed).toEqual([]);
  });

  it('旁邊還有一行說明（超過上限、處理中）時要多預留寬度', () => {
    // Arrange / Act
    const plain = inlineActionKeys(52, ALL, OPTIONS);
    const withNote = inlineActionKeys(52, ALL, { ...OPTIONS, hasNote: true });

    // Assert
    expect(withNote.size).toBeLessThan(plain.size);
  });
});
