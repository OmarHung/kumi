import { describe, expect, it } from 'vitest';
import { KNOWLEDGE_MAX_DEPTH } from '../../../api/knowledge';
import type { KnowledgePageNode } from '../../../api/knowledgeTypes';
import { exampleKnowledgeTree, pageNodeOf } from '../../../test/knowledge';
import { applyMove, dropZoneOf, moveBlock, planDrop } from './treeDrop';
import { buildTreeModel } from './treeModel';

// 1 新人到職（2 帳號申請、3 開發環境）、4 會議記錄、5 手冊
const pages = (): KnowledgePageNode[] => [...exampleKnowledgeTree(), pageNodeOf({ number: 5, title: '手冊', position: 2 })];
const model = () => buildTreeModel(pages());
const NONE: ReadonlySet<number> = new Set();

describe('拖曳的落點', () => {
  it('一列的上四分之一是前、下四分之一是後，中間是變成子頁面', () => {
    // Arrange
    const height = 40;

    // Act & Assert
    expect(dropZoneOf(2, height)).toBe('before');
    expect(dropZoneOf(20, height)).toBe('inside');
    expect(dropZoneOf(38, height)).toBe('after');
  });

  it('放在另一頁的前面：同一層、位置在它前面', () => {
    // Arrange & Act
    const plan = planDrop(model(), 5, 4, 'before', NONE);

    // Assert
    expect(plan).toEqual({ parentNumber: null, beforeNumber: 4 });
  });

  it('放在最後一頁的後面：同一層的最後', () => {
    // Arrange & Act
    const plan = planDrop(model(), 1, 5, 'after', NONE);

    // Assert
    expect(plan).toEqual({ parentNumber: null, beforeNumber: null });
  });

  it('放在中間的頁面的後面：放在它下一頁的前面', () => {
    // Arrange & Act
    const plan = planDrop(model(), 5, 1, 'after', NONE);

    // Assert
    expect(plan).toEqual({ parentNumber: null, beforeNumber: 4 });
  });

  it('放在展開的頁面的後面：變成它的第一個子頁面', () => {
    // Arrange & Act
    const plan = planDrop(model(), 5, 1, 'after', new Set([1]));

    // Assert
    expect(plan).toEqual({ parentNumber: 1, beforeNumber: 2 });
  });

  it('放在一頁的中間：變成它的最後一個子頁面', () => {
    // Arrange & Act
    const plan = planDrop(model(), 5, 4, 'inside', NONE);

    // Assert
    expect(plan).toEqual({ parentNumber: 4, beforeNumber: null });
  });

  it('放在自己或自己底下的頁面上：不能放', () => {
    // Arrange
    const tree = model();

    // Act & Assert
    expect(planDrop(tree, 1, 1, 'inside', NONE)).toBeNull();
    expect(planDrop(tree, 1, 2, 'inside', NONE)).toBeNull();
    expect(planDrop(tree, 1, 3, 'before', NONE)).toBeNull();
  });

  it('放回原來的位置：不能放（不送請求）', () => {
    // Arrange
    const tree = model();

    // Act & Assert
    expect(planDrop(tree, 4, 5, 'before', NONE)).toBeNull();
    expect(planDrop(tree, 4, 1, 'after', NONE)).toBeNull();
    expect(planDrop(tree, 5, 4, 'after', NONE)).toBeNull();
  });

  it('搬進去之後超過 10 層：不能放', () => {
    // Arrange：一條 10 層深的鏈，另有一頁帶一個子頁面（2 層）
    const chain = Array.from({ length: KNOWLEDGE_MAX_DEPTH }, (_, index) =>
      pageNodeOf({ number: index + 1, parentId: index === 0 ? null : `page-${String(index)}` }),
    );
    const heavy = pageNodeOf({ number: 20, position: 1 });
    const child = pageNodeOf({ number: 21, parentId: heavy.id });
    const tree = buildTreeModel([...chain, heavy, child]);

    // Act & Assert
    expect(planDrop(tree, 20, KNOWLEDGE_MAX_DEPTH - 1, 'inside', NONE)).toBeNull();
    expect(planDrop(tree, 20, 1, 'inside', NONE)).not.toBeNull();
    expect(moveBlock(tree, heavy, KNOWLEDGE_MAX_DEPTH - 1)).toBe('too_deep');
    expect(moveBlock(tree, heavy, 20)).toBe('self');
    expect(moveBlock(tree, heavy, 21)).toBe('inside');
    expect(moveBlock(tree, heavy, null)).toBeNull();
  });
});

describe('搬移之後的樹（先改畫面用）', () => {
  it('搬到別的上層：兩邊的位置都重新編號', () => {
    // Arrange & Act
    const moved = applyMove(pages(), 2, { parentNumber: 4, beforeNumber: null });

    // Assert
    const tree = buildTreeModel(moved);
    expect(tree.childrenOf(tree.nodeOf(1) ?? null).map((node) => [node.number, node.position])).toEqual([[3, 0]]);
    expect(tree.childrenOf(tree.nodeOf(4) ?? null).map((node) => [node.number, node.position])).toEqual([[2, 0]]);
  });

  it('同一層換順序：放在指定的頁面前面', () => {
    // Arrange & Act
    const moved = applyMove(pages(), 5, { parentNumber: null, beforeNumber: 1 });

    // Assert
    expect(buildTreeModel(moved).roots.map((node) => [node.number, node.position])).toEqual([[5, 0], [1, 1], [4, 2]]);
  });

  it('結果是先序，不改傳進來的清單', () => {
    // Arrange
    const original = pages();
    const snapshot = JSON.stringify(original);

    // Act
    const moved = applyMove(original, 4, { parentNumber: 1, beforeNumber: 2 });

    // Assert
    expect(moved.map((node) => node.number)).toEqual([1, 4, 2, 3, 5]);
    expect(JSON.stringify(original)).toBe(snapshot);
  });
});
