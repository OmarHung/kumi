import { useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router';
import { knowledgePageUrl } from '../knowledgeLinks';
import type { TreeModel, TreeRow } from './treeModel';

interface Expansion {
  readonly expand: (pageNumber: number) => void;
  readonly collapse: (pageNumber: number) => void;
}

/** 方向鍵、Home、End 之後焦點要去的那一列（不動就是 null）。→ 與 ← 的展開、收合在 handleKeyDown 先處理。 */
function keyTarget(rows: readonly TreeRow[], index: number, key: string, model: TreeModel): TreeRow | null {
  const row = rows[index];
  if (row === undefined) return null;
  switch (key) {
    case 'ArrowDown':
      return rows[index + 1] ?? null;
    case 'ArrowUp':
      return rows[index - 1] ?? null;
    case 'Home':
      return rows[0] ?? null;
    case 'End':
      return rows.at(-1) ?? null;
    case 'ArrowRight':
      return row.isExpanded ? (rows[index + 1] ?? null) : null;
    case 'ArrowLeft': {
      const parent = model.parentOf(row.node);
      return rows.find((candidate) => candidate.node.number === parent?.number) ?? null;
    }
    default:
      return null;
  }
}

interface TreeKeyboardOptions {
  readonly spaceKey: string;
  readonly model: TreeModel;
  readonly rows: readonly TreeRow[];
  readonly currentNumber: number | null;
  readonly expansion: Expansion;
  readonly onNavigate?: () => void;
}

/**
 * 頁面樹的鍵盤（ARIA 的 tree）：整棵樹只有一個 Tab 停靠點（roving tabindex，預設是目前這一頁、沒有就第一列），
 * ↑↓ 移動、→ 展開或進第一個子頁面、← 收起或回上層、Home／End、Enter（空白鍵）打開。
 * 只處理焦點在列本身的按鍵：列上的按鈕（＋、更多）自己處理。
 */
export function useTreeKeyboard({ spaceKey, model, rows, currentNumber, expansion, onNavigate }: TreeKeyboardOptions) {
  const navigate = useNavigate();
  const rowElements = useRef(new Map<number, HTMLLIElement>());
  const [activeNumber, setActiveNumber] = useState<number | null>(null);
  const stopNumber = rows.some((row) => row.node.number === activeNumber)
    ? activeNumber
    : (rows.find((row) => row.node.number === currentNumber) ?? rows[0])?.node.number;

  function focusRow(row: TreeRow) {
    setActiveNumber(row.node.number);
    rowElements.current.get(row.node.number)?.focus();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLUListElement>) {
    const target = event.target;
    if (!(target instanceof HTMLElement) || target.getAttribute('role') !== 'treeitem') return;
    const index = rows.findIndex((row) => String(row.node.number) === target.dataset.page);
    const row = rows[index];
    if (row === undefined) return;
    const handled = (action: () => void) => {
      event.preventDefault();
      action();
    };
    if (event.key === 'Enter' || event.key === ' ') {
      handled(() => {
        void navigate(knowledgePageUrl(spaceKey, row.node.number));
        onNavigate?.();
      });
    } else if (event.key === 'ArrowRight' && row.hasChildren && !row.isExpanded) {
      handled(() => { expansion.expand(row.node.number); });
    } else if (event.key === 'ArrowLeft' && row.isExpanded) {
      handled(() => { expansion.collapse(row.node.number); });
    } else {
      const destination = keyTarget(rows, index, event.key, model);
      if (destination !== null) handled(() => { focusRow(destination); });
    }
  }

  return {
    stopNumber,
    handleKeyDown,
    onFocusRow: setActiveNumber,
    registerRow: (pageNumber: number, element: HTMLLIElement | null) => {
      if (element === null) rowElements.current.delete(pageNumber);
      else rowElements.current.set(pageNumber, element);
    },
  };
}
