import { useMutation, useMutationState, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';
import { transitionItem } from '../../api/items';
import type { Item, ItemDetail } from '../../api/types';
import { isModalDialogOpen, useFillableFields } from '../item-table/cells/fillFields';
import type { FillRequest } from '../item-table/cells/useFillSubmit';
import { replaceItemInCache } from '../item-table/itemCache';

/** 一次移動：把這張卡片（移動前的樣子）移到 to 這個狀態。 */
export interface BoardMove {
  readonly item: Item;
  readonly to: string;
  /** 補填對話框送的：失敗時由對話框說明，看板上方不另外加一則。 */
  readonly fromDialog?: boolean;
}

export interface MoveProblem extends BoardMove {
  readonly error: unknown;
}

function isBoardMove(value: unknown): value is BoardMove {
  return typeof value === 'object' && value !== null && 'item' in value && 'to' in value;
}

function withoutItem(problems: readonly MoveProblem[], itemKey: string): readonly MoveProblem[] {
  return problems.filter((problem) => problem.item.key !== itemKey);
}

/** 焦點在這張卡片裡嗎？卡片換欄時元素會重新掛上，焦點要跟過去。 */
function hasFocusIn(itemKey: string): boolean {
  const card = document.activeElement?.closest('[data-card]');
  return card?.getAttribute('data-card') === itemKey;
}

/** 等著補填的移動：補填對話框一次一個，後面的排隊；連同錯誤一起記著（改成在看板上方說明時用）。 */
interface QueuedFill extends FillRequest {
  readonly error: unknown;
}

/**
 * 補填的佇列。最新的內容另外放在 ref：好幾個移動接連失敗時，各自的 onError 是建立那個移動時的版本（舊的閉包），
 * 要看同一份才不會互相蓋掉。
 */
function useFillQueue() {
  const [queue, setQueue] = useState<readonly QueuedFill[]>([]);
  const queueRef = useRef<readonly QueuedFill[]>([]);
  const replace = (next: readonly QueuedFill[]) => {
    queueRef.current = next;
    setQueue(next);
  };
  return {
    queue,
    /** 自己的補填對話框開著（或馬上要開）。 */
    isActive: () => queueRef.current.length > 0,
    add: (fill: QueuedFill) => {
      replace([...queueRef.current.filter((other) => other.item.key !== fill.item.key), fill]);
    },
    /** 關上前面那一個，後面的接著開。 */
    next: () => {
      replace(queueRef.current.slice(1));
    },
    /** 全部收起來，回傳還沒開的那些。 */
    clear: (): readonly QueuedFill[] => {
      const rest = queueRef.current.slice(1);
      replace([]);
      return rest;
    },
  };
}

/**
 * 被擋下來的移動：卡片已經回到原欄（送出中的移動不在快取裡，失敗就不畫了）。缺欄位而且能在原地補時開補填的對話框
 * （docs/DESIGN.md 8.3「補填再前進」）：好幾張接連被擋下時排隊，一次開一個，等著的卡片留在原欄。別的對話框開著時
 * 不疊上去，和其他原因一樣在看板上方說明。焦點原本在卡片上的，跟著卡片回到原欄。
 */
function useMoveFailures() {
  const fillableFields = useFillableFields();
  const fills = useFillQueue();
  const [problems, setProblems] = useState<readonly MoveProblem[]>([]);
  const focusAfterMoveRef = useRef<string | null>(null);
  const addProblems = (added: readonly MoveProblem[]) => {
    const keys = added.map((problem) => problem.item.key);
    setProblems((current) => [...current.filter((problem) => !keys.includes(problem.item.key)), ...added]);
  };
  const onError = (error: unknown, move: BoardMove) => {
    if (move.fromDialog === true) return;
    if (hasFocusIn(move.item.key)) {
      focusAfterMoveRef.current = move.item.key;
    }
    const fields = fillableFields(error, move.item);
    if (fields !== null && (fills.isActive() || !isModalDialogOpen())) {
      fills.add({ item: move.item, to: move.to, fields, error });
      return;
    }
    addProblems([{ ...move, error }]);
  };
  return { problems, setProblems, fills, focusAfterMoveRef, onError, addProblems };
}

/**
 * 看板上的改狀態。送出中的移動放在 TanStack Query 的 mutation 狀態裡（不是元件狀態）：
 * 成功時「快取換成新的位置」和「不再送出中」在同一批通知裡生效，卡片不會先閃回原欄一下。
 * 每次移動是一個獨立的 mutation，兩張卡片可以同時在送出。
 * 放在 BoardView（不隨流程重建）：送出中切換流程，被擋下來的說明、剛移好的卡片都還在，切回來看得到。
 */
export function useBoardMoves(projectKey: string) {
  const queryClient = useQueryClient();
  const failures = useMoveFailures();
  const { setProblems, focusAfterMoveRef } = failures;
  const [landed, setLanded] = useState<ItemDetail | null>(null);
  const mutationKey = ['board-move', projectKey];
  const { mutate, mutateAsync } = useMutation({
    mutationKey,
    mutationFn: ({ item, to }: BoardMove) => transitionItem(item.key, { to }),
    onSuccess: (updated) => {
      replaceItemInCache(queryClient, updated);
      setLanded(updated);
    },
    onError: failures.onError,
  });
  const variables = useMutationState({
    filters: { mutationKey, status: 'pending' },
    select: (mutation) => mutation.state.variables,
  });
  const pending = useMemo(() => variables.filter(isBoardMove), [variables]);

  function move(item: Item, to: string, keepFocus: boolean) {
    if (item.state === to || pending.some((candidate) => candidate.item.key === item.key)) {
      return;
    }
    focusAfterMoveRef.current = keepFocus ? item.key : null;
    setProblems((current) => withoutItem(current, item.key));
    // 上一次的「已把…移到…」不再是最新的結果；這次失敗時旁邊不會還掛著上一次的成功。
    setLanded(null);
    mutate({ item, to });
  }

  /**
   * 補填對話框裡欄位存好之後送的改狀態：和拖曳、「移到…」同一條路（送出中卡片先畫在新的欄、成功時更新所有快取、
   * 落點閃一下、「已把…移到…」），失敗時交給對話框說明。卡片換欄重新掛上時焦點跟著它（對話框關上後）。
   */
  function moveFromDialog(item: Item, to: string): Promise<ItemDetail> {
    focusAfterMoveRef.current = item.key;
    setLanded(null);
    return mutateAsync({ item, to, fromDialog: true }).catch((error: unknown) => {
      // 又被擋下：對話框還開著，卡片回到原欄時焦點不必跟過去（關上對話框時才放回卡片）。
      focusAfterMoveRef.current = null;
      throw error;
    });
  }

  function dismiss(itemKey: string) {
    setProblems((current) => withoutItem(current, itemKey));
  }

  return { pending, problems: failures.problems, landed, focusAfterMoveRef, move, moveFromDialog, dismiss, ...fillControls(failures) };
}

/** 補填對話框的開關：現在開的是哪一個、後面還有沒有、關上（換下一個），以及去成員之前全部收起來。 */
function fillControls({ fills, addProblems }: ReturnType<typeof useMoveFailures>) {
  return {
    fill: fills.queue[0] ?? null,
    hasMoreFills: fills.queue.length > 1,
    closeFill: fills.next,
    /** 從對話框去「成員」：不在成員上面再開下一個補填，還沒開的改成在看板上方說明（不會不見）。 */
    leaveFills: () => {
      addProblems(fills.clear());
    },
  };
}

export type BoardMoves = ReturnType<typeof useBoardMoves>;
