import { describe, expect, it } from 'vitest';
import type { AssistantConversationListing } from '../../api/aiTypes';
import { groupConversations, MAX_CURRENT_CONVERSATIONS, MAX_LISTED_CONVERSATIONS } from './conversationGroups';

function listing(id: string, projectKey: string | null): AssistantConversationListing {
  return { id, projectKey, title: id, messageCount: 2, isRunning: false, createdAt: '2026-11-26T02:00:00Z', updatedAt: '2026-11-26T02:00:00Z' };
}

function many(prefix: string, projectKey: string | null, count: number): AssistantConversationListing[] {
  return Array.from({ length: count }, (_, index) => listing(`${prefix}-${String(index)}`, projectKey));
}

describe('groupConversations', () => {
  it('目前的專案在最前面，其他的照最近一則的順序；不選專案是自己的一組', () => {
    // Arrange
    const all = [listing('a1', 'AGY'), listing('o1', null), listing('s1', 'SMK'), listing('a2', 'AGY')];

    // Act
    const groups = groupConversations(all, 'SMK');

    // Assert
    expect(groups.map((group) => [group.projectKey, group.conversations.map((c) => c.id)])).toEqual([
      ['SMK', ['s1']],
      ['AGY', ['a1', 'a2']],
      [null, ['o1']],
    ]);
  });

  it('目前的專案沒有對話：不畫空的組', () => {
    // Act
    const groups = groupConversations([listing('a1', 'AGY')], 'SMK');

    // Assert
    expect(groups.map((group) => group.projectKey)).toEqual(['AGY']);
  });

  it('別的專案很熱鬧：目前的專案先保留名額，打勾的那一則不會被擠出去；合計不超過上限', () => {
    // Arrange
    const all = [...many('a', 'AGY', 80), ...many('s', 'SMK', 30)];

    // Act
    const groups = groupConversations(all, 'SMK');

    // Assert
    const [current, other] = groups;
    expect(current?.conversations).toHaveLength(MAX_CURRENT_CONVERSATIONS);
    expect(other?.conversations).toHaveLength(MAX_LISTED_CONVERSATIONS - MAX_CURRENT_CONVERSATIONS);
  });

  it('目前的專案不多時，剩下的名額都給別的專案', () => {
    // Arrange
    const all = [...many('a', 'AGY', 80), ...many('s', 'SMK', 3)];

    // Act
    const groups = groupConversations(all, 'SMK');

    // Assert
    expect(groups.flatMap((group) => group.conversations)).toHaveLength(MAX_LISTED_CONVERSATIONS);
    expect(groups[0]?.conversations).toHaveLength(3);
  });
});
