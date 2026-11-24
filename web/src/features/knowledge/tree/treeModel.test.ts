import { describe, expect, it } from 'vitest';
import { exampleKnowledgeTree, pageNodeOf } from '../../../test/knowledge';
import { ancestorsOf, buildTreeModel, descendantNumbers, levelOf, pathTitle, subtreeHeight, visibleRows } from './treeModel';

const model = () => buildTreeModel(exampleKnowledgeTree());

describe('頁面樹的模型', () => {
  it('只有收合的頁面底下的頁面不列在畫面上', () => {
    // Arrange
    const tree = model();

    // Act
    const collapsed = visibleRows(tree, new Set());
    const expanded = visibleRows(tree, new Set([1]));

    // Assert
    expect(collapsed.map((row) => row.node.number)).toEqual([1, 4]);
    expect(expanded.map((row) => row.node.number)).toEqual([1, 2, 3, 4]);
    expect(expanded.map((row) => row.level)).toEqual([1, 2, 2, 1]);
    expect(expanded[1]).toMatchObject({ position: 1, setSize: 2, hasChildren: false });
  });

  it('展開的記號只給有子頁面的頁面', () => {
    // Arrange
    const tree = model();

    // Act
    const rows = visibleRows(tree, new Set([1, 4]));

    // Assert
    expect(rows.find((row) => row.node.number === 1)?.isExpanded).toBe(true);
    expect(rows.find((row) => row.node.number === 4)?.isExpanded).toBe(false);
  });

  it('同一層依 position 排，不依陣列的順序', () => {
    // Arrange
    const tree = buildTreeModel([pageNodeOf({ number: 5, position: 1 }), pageNodeOf({ number: 6, position: 0 })]);

    // Act
    const numbers = tree.roots.map((node) => node.number);

    // Assert
    expect(numbers).toEqual([6, 5]);
  });

  it('找得到上層、層級、底下的頁面與底下有幾層', () => {
    // Arrange
    const tree = model();
    const root = tree.nodeOf(1);
    const leaf = tree.nodeOf(2);
    if (root === undefined || leaf === undefined) throw new Error('範例的樹少了頁面');

    // Act & Assert
    expect(ancestorsOf(tree, leaf).map((node) => node.number)).toEqual([1]);
    expect(levelOf(tree, root)).toBe(1);
    expect(levelOf(tree, leaf)).toBe(2);
    expect([...descendantNumbers(tree, root)]).toEqual([2, 3]);
    expect(subtreeHeight(tree, root)).toBe(1);
    expect(subtreeHeight(tree, leaf)).toBe(0);
    expect(pathTitle(tree, leaf)).toBe('新人到職 › 帳號申請');
  });

  it('找不到上層的頁面當成最上層，不會從畫面上消失', () => {
    // Arrange
    const orphan = pageNodeOf({ number: 9, parentId: 'page-404' });

    // Act
    const tree = buildTreeModel([orphan]);

    // Assert
    expect(tree.roots.map((node) => node.number)).toEqual([9]);
    expect(tree.parentOf(orphan)).toBeNull();
  });
});
