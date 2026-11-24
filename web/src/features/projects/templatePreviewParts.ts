import type { StateCategory } from '../../api/blueprint';
import type { TemplateParents, TemplateWorkflowPreview } from '../../api/types';

export interface PreviewState {
  readonly name: string;
  readonly category: StateCategory;
}

export interface WorkflowParts {
  /** 主線：照流程的順序，到最後一個「完成」類的狀態為止。 */
  readonly main: readonly PreviewState[];
  /** 不在主線上的：「完成」之後的狀態（暫停這類）與取消類。不是下一步，另外列。 */
  readonly others: readonly PreviewState[];
}

/**
 * 一個流程拆成主線與其他。「完成」之後還排著的狀態（接案模板的「暫停」）不是完成的下一步，
 * 接在箭頭後面會看成「完成之後要暫停」，所以和取消類一起另外列。沒有「完成」類時整條都是主線。
 */
export function workflowParts(workflow: TemplateWorkflowPreview): WorkflowParts {
  const states = workflow.states.map((name, index): PreviewState => ({ name, category: workflow.stateCategories[index] ?? 'not_started' }));
  const lastDone = states.findLastIndex((state) => state.category === 'done');
  const canceled = workflow.canceledStates.map((name): PreviewState => ({ name, category: 'canceled' }));
  return lastDone < 0
    ? { main: states, others: canceled }
    : { main: states.slice(0, lastDone + 1), others: [...states.slice(lastDone + 1), ...canceled] };
}

export interface HierarchyChain {
  /** 第一行：最上層的類型（可以是幾種之一）。 */
  readonly roots: readonly string[];
  /** 往下一層一層的類型，每一層縮排一次。 */
  readonly levels: readonly string[];
}

/**
 * 上下層畫成縮排的樹：下一筆的上層只有一種、而且就是上一筆的類型時接在它底下（需求、需求變更單 → 任務 → 子任務），
 * 不然另起一棵（上層有好幾種時一棵樹畫不下，例如待辦可以放在會議或決議底下）。照藍圖的順序。
 */
export function hierarchyChains(parents: readonly TemplateParents[]): readonly HierarchyChain[] {
  return parents.reduce<readonly HierarchyChain[]>((chains, entry) => {
    const last = chains.at(-1);
    const extendsLast = last !== undefined && entry.parents.length === 1 && entry.parents[0] === last.levels.at(-1);
    return extendsLast
      ? [...chains.slice(0, -1), { ...last, levels: [...last.levels, entry.itemType] }]
      : [...chains, { roots: entry.parents, levels: [entry.itemType] }];
  }, []);
}
