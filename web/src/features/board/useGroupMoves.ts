import { useMutation, useMutationState, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';
import { updateItem } from '../../api/items';
import type { Item, ItemDetail, ItemPatch } from '../../api/types';
import { sameBoardGroup, type BoardGroup } from '../../domain/boardGroup';
import { replaceItemInCache } from '../item-table/itemCache';
import type { BoardMove, MoveProblem } from './useBoardMoves';

/** 依負責人、欄位分欄時的一次移動：to 是欄的 key，patch 是要送的修改。 */
export interface GroupMove extends BoardMove {
  readonly patch: ItemPatch;
  /** 移動當時的分組：to 是那個分組的欄 key，換了分組之後不能照另一種分組解讀。 */
  readonly group: BoardGroup;
}

export interface GroupMoveProblem extends MoveProblem {
  readonly group: BoardGroup;
}

/** 只留這個分組自己的移動與說明；換了分組，別的分組的欄 key 在這裡沒有意義。 */
export function movesOfGroup<T extends { readonly group: BoardGroup }>(moves: readonly T[], group: BoardGroup): readonly T[] {
  return moves.filter((move) => sameBoardGroup(move.group, group));
}

function isGroupMove(value: unknown): value is GroupMove {
  return typeof value === 'object' && value !== null && 'item' in value && 'to' in value && 'patch' in value && 'group' in value;
}

/**
 * 依負責人、欄位分欄時看板上的移動：就是修改項目（`PATCH /v1/items/{key}`）。和 useBoardMoves 同一個做法：
 * 送出中的移動放在 mutation 狀態裡、不寫進快取（成功時才換成新的位置，失敗時卡片自然回到原欄）；
 * 每次移動是獨立的 mutation，兩張卡片可以同時送出。修改不會被「要先填欄位」擋下，所以沒有補填的對話框。
 * 放在 BoardView（不隨分組重建）：換分組之後，說明與送出中的移動都還在。
 */
export function useGroupMoves(projectKey: string) {
  const queryClient = useQueryClient();
  const [problems, setProblems] = useState<readonly GroupMoveProblem[]>([]);
  const [landed, setLanded] = useState<ItemDetail | null>(null);
  const focusAfterMoveRef = useRef<string | null>(null);
  const mutationKey = ['board-group-move', projectKey];
  const { mutate } = useMutation({
    mutationKey,
    mutationFn: ({ item, patch }: GroupMove) => updateItem(item.key, patch),
    onSuccess: (updated) => {
      replaceItemInCache(queryClient, updated);
      setLanded(updated);
    },
    onError: (error, move) => {
      // 焦點在卡片上的（從「移到…」選的），卡片回到原欄重新掛上時跟過去。
      if (document.activeElement?.closest('[data-card]')?.getAttribute('data-card') === move.item.key) {
        focusAfterMoveRef.current = move.item.key;
      }
      setProblems((current) => [...current.filter((problem) => problem.item.key !== move.item.key), { item: move.item, to: move.to, group: move.group, error }]);
    },
  });
  const variables = useMutationState({ filters: { mutationKey, status: 'pending' }, select: (mutation) => mutation.state.variables });
  const pending = useMemo<readonly GroupMove[]>(() => variables.filter(isGroupMove), [variables]);

  function move(item: Item, to: string, patch: ItemPatch, group: BoardGroup, keepFocus: boolean) {
    if (pending.some((candidate) => candidate.item.key === item.key)) {
      return;
    }
    focusAfterMoveRef.current = keepFocus ? item.key : null;
    setProblems((current) => current.filter((problem) => problem.item.key !== item.key));
    setLanded(null);
    mutate({ item, to, patch, group });
  }

  function dismiss(itemKey: string) {
    setProblems((current) => current.filter((problem) => problem.item.key !== itemKey));
  }

  return { pending, problems, landed, focusAfterMoveRef, move, dismiss };
}

export type GroupMoves = ReturnType<typeof useGroupMoves>;
