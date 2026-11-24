import { useMutation, useMutationState, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';
import { transitionItem } from '../../../api/items';
import type { Item, ItemDetail, ProjectDetail } from '../../../api/types';
import { abilitiesOf } from '../../item-table/ProjectTableContext';
import { fillFieldsFor, isModalDialogOpen } from '../../item-table/cells/fillFields';
import { missingFieldPaths } from '../../item-table/cells/fillPlan';
import { replaceItemInCache } from '../../item-table/itemCache';
import type { BoardMove } from '../../board/useBoardMoves';
import { useMoveFillQueue } from './useMoveFillQueue';

/** 被擋下來的移動。label 是要去的地方的名稱：沒有狀態可送時（流程沒有那個類別）用類別的名稱。 */
export interface MyWorkMoveProblem extends BoardMove {
  readonly error: unknown;
  readonly label?: string;
}

const MUTATION_KEY = ['my-work-board-move'] as const;

function isBoardMove(value: unknown): value is BoardMove {
  return typeof value === 'object' && value !== null && 'item' in value && 'to' in value;
}

function withoutItem(problems: readonly MyWorkMoveProblem[], itemKey: string): readonly MyWorkMoveProblem[] {
  return problems.filter((problem) => problem.item.key !== itemKey);
}

/** 焦點在這張卡片裡嗎？卡片換欄時元素會重新掛上，焦點要跟過去。 */
function hasFocusIn(itemKey: string): boolean {
  return document.activeElement?.closest('[data-card]')?.getAttribute('data-card') === itemKey;
}

/**
 * 改狀態失敗時能不能開補填的對話框：是「要先填欄位」、我能改這個項目（那個項目自己專案的權限）、每個欄位都有編輯器。
 * 專案單一的看板在 useFillableFields 讀目前的專案；這裡每張卡片是不同的專案。
 */
function fillableFieldsOf(error: unknown, item: Item, projects: ReadonlyMap<string, ProjectDetail>): readonly string[] | null {
  const detail = projects.get(item.projectKey);
  const paths = missingFieldPaths(error);
  if (detail === undefined || !abilitiesOf(detail).update || paths === null || paths.length === 0) return null;
  return fillFieldsFor(paths, item, detail) === null ? null : paths;
}

/**
 * 被擋下來的移動：卡片已經回到原欄（送出中的移動不在快取裡，失敗就不畫了）。缺欄位而且能在原地補時開補填的對話框，
 * 好幾張接連被擋下時排隊；別的對話框開著時不疊上去，和其他原因一樣在看板上方說明。
 */
function useMoveFailures(projects: ReadonlyMap<string, ProjectDetail>) {
  const fills = useMoveFillQueue();
  const [problems, setProblems] = useState<readonly MyWorkMoveProblem[]>([]);
  const focusAfterMoveRef = useRef<string | null>(null);
  const addProblems = (added: readonly MyWorkMoveProblem[]) => {
    const keys = added.map((problem) => problem.item.key);
    setProblems((current) => [...current.filter((problem) => !keys.includes(problem.item.key)), ...added]);
  };
  const onError = (error: unknown, move: BoardMove) => {
    if (move.fromDialog === true) return;
    if (hasFocusIn(move.item.key)) focusAfterMoveRef.current = move.item.key;
    const fields = fillableFieldsOf(error, move.item, projects);
    if (fields !== null && (fills.isActive() || !isModalDialogOpen())) {
      fills.add({ item: move.item, to: move.to, fields, error });
      return;
    }
    addProblems([{ ...move, error }]);
  };
  return { problems, setProblems, fills, focusAfterMoveRef, onError, addProblems };
}

/**
 * 改狀態的 mutation：送出中的移動放在 TanStack Query 的 mutation 狀態裡，成功時「快取換成新的位置」和「不再送出中」
 * 在同一批通知裡生效，卡片不會先閃回原欄一下；每次移動是獨立的 mutation。
 */
function useTransition(failures: ReturnType<typeof useMoveFailures>, setLanded: (item: ItemDetail | null) => void) {
  const queryClient = useQueryClient();
  const { mutate, mutateAsync } = useMutation({
    mutationKey: MUTATION_KEY,
    mutationFn: ({ item, to }: BoardMove) => transitionItem(item.key, { to }),
    onSuccess: (updated) => {
      replaceItemInCache(queryClient, updated);
      setLanded(updated);
    },
    onError: failures.onError,
  });
  const variables = useMutationState({ filters: { mutationKey: MUTATION_KEY, status: 'pending' }, select: (mutation) => mutation.state.variables });
  const pending = useMemo(() => variables.filter(isBoardMove), [variables]);
  return { mutate, mutateAsync, pending };
}

/**
 * 「我的工作」看板上的改狀態。和專案的看板（useBoardMoves）同一套（送出中的移動見 useTransition）。
 * 差別是每張卡片是不同的專案：補填要看那個項目自己專案的權限與藍圖（projects）。
 */
export function useMyWorkBoardMoves(projects: ReadonlyMap<string, ProjectDetail>) {
  const failures = useMoveFailures(projects);
  const { setProblems, focusAfterMoveRef } = failures;
  const [landed, setLanded] = useState<ItemDetail | null>(null);
  const { mutate, mutateAsync, pending } = useTransition(failures, setLanded);

  function move(item: Item, to: string, keepFocus: boolean) {
    if (item.state === to || pending.some((candidate) => candidate.item.key === item.key)) return;
    focusAfterMoveRef.current = keepFocus ? item.key : null;
    setProblems((current) => withoutItem(current, item.key));
    // 上一次的「已把…移到…」不再是最新的結果；這次失敗時旁邊不會還掛著上一次的成功。
    setLanded(null);
    mutate({ item, to });
  }

  /** 補填對話框裡欄位存好之後送的改狀態：和拖曳、「移到…」同一條路，失敗時交給對話框說明。 */
  function moveFromDialog(item: Item, to: string): Promise<ItemDetail> {
    focusAfterMoveRef.current = item.key;
    setLanded(null);
    return mutateAsync({ item, to, fromDialog: true }).catch((error: unknown) => {
      // 又被擋下：對話框還開著，卡片回到原欄時焦點不必跟過去（關上對話框時才放回卡片）。
      focusAfterMoveRef.current = null;
      throw error;
    });
  }

  /** 拖到流程裡沒有的類別：沒有東西可送，直接在看板上方說明。 */
  function explain(item: Item, label: string, message: string) {
    setLanded(null);
    failures.addProblems([{ item, to: '', label, error: new Error(message) }]);
  }

  return {
    pending,
    problems: failures.problems,
    landed,
    focusAfterMoveRef,
    move,
    moveFromDialog,
    explain,
    dismiss: (itemKey: string) => {
      setProblems((current) => withoutItem(current, itemKey));
    },
    ...fillControls(failures),
  };
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

export type MyWorkBoardMoves = ReturnType<typeof useMyWorkBoardMoves>;
