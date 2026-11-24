import type { DragEndEvent } from '@dnd-kit/core';
import { describe, expect, it } from 'vitest';
import { exampleKnowledgeTree } from '../../../test/knowledge';
import { buildTreeModel } from './treeModel';
import { moveSummary, treeAnnouncements, type DropHover } from './usePageTreeDnd';

const model = () => buildTreeModel(exampleKnowledgeTree());

const endEvent = (over: boolean): DragEndEvent => ({ active: { id: 4 }, over: over ? { id: 1 } : null }) as unknown as DragEndEvent;

describe('頁面樹的拖曳提示', () => {
  it('放在不能放的位置：說這裡不能放，沒有搬移', () => {
    // Arrange
    const hover: DropHover = { targetNumber: 4, zone: 'inside', plan: null };

    // Act
    const text = treeAnnouncements(model(), () => hover).onDragEnd(endEvent(true));

    // Assert
    expect(text).toBe('這裡不能放，沒有搬移。');
  });

  it('放在不在任何頁面上：放回原處，沒有搬移', () => {
    // Arrange & Act
    const text = treeAnnouncements(model(), () => null).onDragEnd(endEvent(false));

    // Assert
    expect(text).toContain('沒有搬移');
  });

  it('真的要搬時：不重複唸，結果由樹上的即時區域說', () => {
    // Arrange
    const hover: DropHover = { targetNumber: 1, zone: 'inside', plan: { parentNumber: 1, beforeNumber: null } };

    // Act
    const text = treeAnnouncements(model(), () => hover).onDragEnd(endEvent(true));

    // Assert
    expect(text).toBe('');
  });

  it('搬移的結果：搬到某一頁底下、搬到最上層', () => {
    // Arrange & Act & Assert
    expect(moveSummary(model(), 4, { parentNumber: 1, beforeNumber: null })).toBe('已把「會議記錄」搬到「新人到職」底下。');
    expect(moveSummary(model(), 2, { parentNumber: null, beforeNumber: 1 })).toBe('已把「帳號申請」搬到最上層。');
  });
});
