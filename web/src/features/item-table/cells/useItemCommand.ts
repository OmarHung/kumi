import { useMutation, useQueryClient } from '@tanstack/react-query';
import { transitionItem, updateItem } from '../../../api/items';
import type { ItemPatch } from '../../../api/types';
import { refreshSubscriptionLater } from '../../../api/notificationCache';
import { replaceItemInCache } from '../itemCache';

/** 一格的修改送到後端的方式：一般欄位用 PATCH，狀態走轉換。 */
export type SaveCommand =
  | { readonly kind: 'patch'; readonly patch: ItemPatch }
  | { readonly kind: 'transition'; readonly to: string };

/** 從編輯中的值決定要做什麼：沒改就不送，前端看得出來的錯就不必問後端。 */
export type CellCommand =
  | SaveCommand
  | { readonly kind: 'unchanged' }
  | { readonly kind: 'invalid'; readonly message: string };

export const UNCHANGED: CellCommand = { kind: 'unchanged' };

export function patchCommand(patch: ItemPatch): CellCommand {
  return { kind: 'patch', patch };
}

export function invalidCommand(message: string): CellCommand {
  return { kind: 'invalid', message };
}

export function useItemCommand(itemKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (command: SaveCommand) =>
      command.kind === 'patch'
        ? updateItem(itemKey, command.patch)
        : transitionItem(itemKey, { to: command.to }),
    onSuccess: (item, command) => {
      replaceItemInCache(queryClient, item);
      // 指派與改說明會讓後端自動關注（新的負責人、被提及的人）：延遲一下再問關注的狀態。
      if (command.kind === 'patch' && ('assigneeId' in command.patch || 'description' in command.patch)) {
        refreshSubscriptionLater(queryClient, itemKey);
      }
    },
  });
}
