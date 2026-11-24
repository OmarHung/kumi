import type { KnowledgePageNode } from '../../../api/knowledgeTypes';

/** 空間裡看得到的頁面排成的一棵樹（唯讀）：畫樹、算搬移與拖曳的落點都從它來。 */
export interface TreeModel {
  readonly pages: readonly KnowledgePageNode[];
  readonly roots: readonly KnowledgePageNode[];
  readonly nodeOf: (pageNumber: number) => KnowledgePageNode | undefined;
  /** 同一層，依 position；null 是最上層。 */
  readonly childrenOf: (node: KnowledgePageNode | null) => readonly KnowledgePageNode[];
  /** 上層頁面；最上層是 null。 */
  readonly parentOf: (node: KnowledgePageNode) => KnowledgePageNode | null;
}

export interface TreeRow {
  readonly node: KnowledgePageNode;
  /** 最上層是 1（和 aria-level 一樣）。 */
  readonly level: number;
  readonly hasChildren: boolean;
  readonly isExpanded: boolean;
  /** 在同一層裡是第幾個（從 1 起）與同一層共幾個。 */
  readonly position: number;
  readonly setSize: number;
}

/**
 * 看得到的頁面它的上層一定也看得到（伺服器保證），所以接得成一棵樹；
 * 萬一找不到上層（資料不一致）就當成最上層，頁面不會因此從畫面上消失。
 */
export function buildTreeModel(pages: readonly KnowledgePageNode[]): TreeModel {
  const byId = new Map(pages.map((page) => [page.id, page]));
  const byNumber = new Map(pages.map((page) => [page.number, page]));
  const grouped = new Map<string | null, KnowledgePageNode[]>();
  for (const page of pages) {
    const parentId = page.parentId !== null && byId.has(page.parentId) ? page.parentId : null;
    grouped.set(parentId, [...(grouped.get(parentId) ?? []), page]);
  }
  const sorted = new Map(
    [...grouped].map(([parentId, children]) => [parentId, [...children].sort((a, b) => a.position - b.position)] as const),
  );
  const childrenById = (parentId: string | null) => sorted.get(parentId) ?? [];
  return {
    pages,
    roots: childrenById(null),
    nodeOf: (pageNumber) => byNumber.get(pageNumber),
    childrenOf: (node) => childrenById(node === null ? null : node.id),
    parentOf: (node) => {
      const parent = node.parentId === null ? undefined : byId.get(node.parentId);
      return parent ?? null;
    },
  };
}

/** 從最上層到上一層，不含自己。 */
export function ancestorsOf(model: TreeModel, node: KnowledgePageNode): readonly KnowledgePageNode[] {
  const parent = model.parentOf(node);
  return parent === null ? [] : [...ancestorsOf(model, parent), parent];
}

/** 最上層是 1。 */
export function levelOf(model: TreeModel, node: KnowledgePageNode): number {
  return ancestorsOf(model, node).length + 1;
}

/** 底下所有的頁面編號（不含自己）。 */
export function descendantNumbers(model: TreeModel, node: KnowledgePageNode): ReadonlySet<number> {
  const found = new Set<number>();
  const visit = (parent: KnowledgePageNode) => {
    for (const child of model.childrenOf(parent)) {
      found.add(child.number);
      visit(child);
    }
  };
  visit(node);
  return found;
}

/** 這一頁底下最深還有幾層（沒有子頁面是 0）。 */
export function subtreeHeight(model: TreeModel, node: KnowledgePageNode): number {
  return model.childrenOf(node).reduce((deepest, child) => Math.max(deepest, 1 + subtreeHeight(model, child)), 0);
}

/** 照畫面上的順序攤平：收合的頁面底下的不列。 */
export function visibleRows(model: TreeModel, expanded: ReadonlySet<number>): readonly TreeRow[] {
  const rows: TreeRow[] = [];
  const visit = (siblings: readonly KnowledgePageNode[], level: number) => {
    siblings.forEach((node, index) => {
      const children = model.childrenOf(node);
      const isExpanded = children.length > 0 && expanded.has(node.number);
      rows.push({ node, level, hasChildren: children.length > 0, isExpanded, position: index + 1, setSize: siblings.length });
      if (isExpanded) visit(children, level + 1);
    });
  };
  visit(model.roots, 1);
  return rows;
}

/** 「新人到職 › 帳號申請」：搬移的下拉裡分得出同名的頁面。 */
export function pathTitle(model: TreeModel, node: KnowledgePageNode): string {
  return [...ancestorsOf(model, node), node].map((page) => page.title).join(' › ');
}
