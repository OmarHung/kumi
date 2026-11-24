import { KNOWLEDGE_MAX_DEPTH } from '../../../api/knowledge';
import type { KnowledgePageNode } from '../../../api/knowledgeTypes';
import { buildTreeModel, descendantNumbers, levelOf, subtreeHeight, type TreeModel } from './treeModel';

/** 搬移要送出的位置（和 API 的 POST …/move 一樣）：上層的編號，與放在哪一個同層頁面的前面；beforeNumber 是 null 就放最後。 */
export interface MovePlan {
  readonly parentNumber: number | null;
  readonly beforeNumber: number | null;
}

/** 放在一列的前面、變成它的子頁面、放在它的後面。 */
export type DropZone = 'before' | 'inside' | 'after';

/** 一列的上四分之一是「前」、下四分之一是「後」，中間一半是「變成它的子頁面」：中間要夠大，指標才好瞄準。 */
const EDGE_RATIO = 0.25;

export function dropZoneOf(offsetY: number, height: number): DropZone {
  if (height <= 0) return 'inside';
  const ratio = offsetY / height;
  if (ratio < EDGE_RATIO) return 'before';
  return ratio > 1 - EDGE_RATIO ? 'after' : 'inside';
}

/** 不能放進這個上層的原因：自己、自己底下、超過 10 層。 */
export type MoveBlock = 'self' | 'inside' | 'too_deep';

/** parentNumber 是 null 就是最上層（一定放得下）。 */
export function moveBlock(model: TreeModel, moved: KnowledgePageNode, parentNumber: number | null): MoveBlock | null {
  if (parentNumber === null) return null;
  if (parentNumber === moved.number) return 'self';
  if (descendantNumbers(model, moved).has(parentNumber)) return 'inside';
  const parent = model.nodeOf(parentNumber);
  if (parent === undefined) return null;
  return levelOf(model, parent) + 1 + subtreeHeight(model, moved) > KNOWLEDGE_MAX_DEPTH ? 'too_deep' : null;
}

/** 同一層裡排在它後面的那一頁（不算自己）；沒有是 null。 */
function nextSiblingNumber(model: TreeModel, node: KnowledgePageNode): number | null {
  const siblings = model.childrenOf(model.parentOf(node));
  const index = siblings.findIndex((sibling) => sibling.number === node.number);
  return siblings[index + 1]?.number ?? null;
}

/** 放回原來的位置：不送請求、不顯示落點。 */
export function isNoopMove(model: TreeModel, moved: KnowledgePageNode, plan: MovePlan): boolean {
  const currentParent = model.parentOf(moved)?.number ?? null;
  return plan.parentNumber === currentParent && plan.beforeNumber === nextSiblingNumber(model, moved);
}

/** 檢查一個搬移（拖曳與「搬移…」對話框共用）：能搬而且真的換了位置才回傳，不然是 null。 */
export function validMove(model: TreeModel, moved: KnowledgePageNode, plan: MovePlan): MovePlan | null {
  if (moveBlock(model, moved, plan.parentNumber) !== null) return null;
  return isNoopMove(model, moved, plan) ? null : plan;
}

/** 第一個不是被拖的那一頁的子頁面；沒有是 null。 */
function firstChildOtherThan(model: TreeModel, parent: KnowledgePageNode, moved: KnowledgePageNode): number | null {
  return model.childrenOf(parent).find((child) => child.number !== moved.number)?.number ?? null;
}

function planAround(model: TreeModel, moved: KnowledgePageNode, target: KnowledgePageNode, zone: DropZone, isExpanded: boolean): MovePlan {
  if (zone === 'inside') return { parentNumber: target.number, beforeNumber: null };
  if (zone === 'before') return { parentNumber: model.parentOf(target)?.number ?? null, beforeNumber: target.number };
  // 展開的頁面「後面」看起來就是它的第一個子頁面的前面：落點的線畫在那裡，放下也放在那裡。
  if (isExpanded && model.childrenOf(target).length > 0) {
    return { parentNumber: target.number, beforeNumber: firstChildOtherThan(model, target, moved) };
  }
  const siblings = model.childrenOf(model.parentOf(target));
  const next = siblings.slice(siblings.findIndex((sibling) => sibling.number === target.number) + 1).find((sibling) => sibling.number !== moved.number);
  return { parentNumber: model.parentOf(target)?.number ?? null, beforeNumber: next?.number ?? null };
}

/**
 * 拖曳的頁面放在另一列的哪一區之後，要搬到哪裡。不能放的位置（自己或自己底下、超過 10 層、放回原位）是 null：不顯示落點。
 * expanded 是畫面上展開的頁面：展開的頁面的「後面」是它的第一個子頁面的前面。
 */
export function planDrop(
  model: TreeModel,
  movedNumber: number,
  targetNumber: number,
  zone: DropZone,
  expanded: ReadonlySet<number>,
): MovePlan | null {
  const moved = model.nodeOf(movedNumber);
  const target = model.nodeOf(targetNumber);
  if (moved === undefined || target === undefined) return null;
  if (target.number === moved.number || descendantNumbers(model, moved).has(target.number)) return null;
  return validMove(model, moved, planAround(model, moved, target, zone, expanded.has(target.number)));
}

/** 這一個搬移之後的頁面清單（先序、position 重新編號）：先改畫面，伺服器回來再換成它回的樹。不改傳進來的清單。 */
export function applyMove(pages: readonly KnowledgePageNode[], movedNumber: number, plan: MovePlan): readonly KnowledgePageNode[] {
  const model = buildTreeModel(pages);
  const moved = model.nodeOf(movedNumber);
  if (moved === undefined) return pages;
  const parent = plan.parentNumber === null ? null : (model.nodeOf(plan.parentNumber) ?? null);
  const parentId = parent?.id ?? null;
  const others = model.childrenOf(parent).filter((sibling) => sibling.number !== moved.number);
  const index = plan.beforeNumber === null ? -1 : others.findIndex((sibling) => sibling.number === plan.beforeNumber);
  const at = index === -1 ? others.length : index;
  const ordered = [...others.slice(0, at), moved, ...others.slice(at)];
  const updated = new Map<string, KnowledgePageNode>();
  ordered.forEach((node, position) => updated.set(node.id, { ...node, parentId, position }));
  model.childrenOf(model.parentOf(moved)).filter((sibling) => sibling.number !== moved.number).forEach((node, position) => {
    if (!updated.has(node.id)) updated.set(node.id, { ...node, position });
  });
  return preorder(buildTreeModel(pages.map((page) => updated.get(page.id) ?? page)));
}

function preorder(model: TreeModel): readonly KnowledgePageNode[] {
  const visit = (node: KnowledgePageNode): KnowledgePageNode[] => [node, ...model.childrenOf(node).flatMap(visit)];
  return model.roots.flatMap(visit);
}
