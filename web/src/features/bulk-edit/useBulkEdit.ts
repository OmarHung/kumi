import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { errorMessage } from '../../api/http';
import {
  bulkDeleteItems,
  bulkRestoreItems,
  bulkUpdateItems,
  type BulkItemFailure,
  type BulkUpdateRequest,
} from '../../api/itemRecovery';
import { refreshAfterRestore, removeItemsFromCache, replaceListedItemsInCache } from '../item-table/itemCache';
import type { ItemSelection } from './useItemSelection';

/** 表格上方的批次結果。全部成功的修改不提示（看得到結果）；其他都留在這裡，直到關掉或下一次操作。 */
export type BulkNotice =
  | { readonly kind: 'updated'; readonly updatedCount: number; readonly failed: readonly BulkItemFailure[] }
  | {
      readonly kind: 'deleted';
      /** 剛刪掉的編號：「復原」送的就是它們。 */
      readonly keys: readonly string[];
      readonly failed: readonly BulkItemFailure[];
      readonly isRestoring: boolean;
      /** 上一次「復原」整個請求失敗（網路、權限）：提示與「復原」照舊留著，多一行原因，可以再按一次。 */
      readonly restoreError?: string;
    }
  | { readonly kind: 'restored'; readonly count: number; readonly failed: readonly BulkItemFailure[] }
  /** 整個請求就失敗了（沒有權限、網路、請求不合格）：沒有任何一筆做成。 */
  | { readonly kind: 'error'; readonly message: string };

/** 要改的屬性（keys 由選取來）。state 和其他屬性只能擇一。 */
export type BulkPatch = BulkUpdateRequest extends infer Request ? (Request extends unknown ? Omit<Request, 'keys'> : never) : never;

export interface BulkEdit {
  readonly isPending: boolean;
  readonly notice: BulkNotice | null;
  readonly update: (patch: BulkPatch) => Promise<void>;
  readonly remove: () => Promise<void>;
  readonly undoRemove: () => Promise<void>;
  readonly dismissNotice: () => void;
}

function keysOf(failures: readonly BulkItemFailure[]): readonly string[] {
  return failures.map((failure) => failure.key);
}

/** 批次操作需要的：專案、選取、快取。 */
interface BulkContext {
  readonly projectKey: string;
  readonly queryClient: QueryClient;
  readonly selection: ItemSelection;
}

/** 選取換成這些編號；沒有剩下的就是取消選取，焦點回到表頭的勾選框（動作列跟著不見）。 */
function keepOrCancel(selection: ItemSelection, itemKeys: readonly string[]): void {
  if (itemKeys.length > 0) selection.keep(itemKeys);
  else selection.cancel();
}

async function sendUpdate({ projectKey, queryClient, selection }: BulkContext, patch: BulkPatch): Promise<BulkNotice | null> {
  const keys = selection.selectedItems.map((item) => item.key);
  const result = await bulkUpdateItems(projectKey, { keys, ...patch });
  replaceListedItemsInCache(queryClient, projectKey, result.updated);
  if (result.failed.length === 0) return null;
  keepOrCancel(selection, keysOf(result.failed));
  return { kind: 'updated', updatedCount: result.updated.length, failed: result.failed };
}

async function sendDelete({ projectKey, queryClient, selection }: BulkContext): Promise<BulkNotice> {
  const keys = selection.selectedItems.map((item) => item.key);
  const result = await bulkDeleteItems(projectKey, keys);
  const deleted = result.deleted.map((item) => item.key);
  removeItemsFromCache(queryClient, projectKey, deleted);
  keepOrCancel(selection, keysOf(result.failed));
  return { kind: 'deleted', keys: deleted, failed: result.failed, isRestoring: false };
}

/** 沒復原的（例如上層還在刪除狀態）也列出來；刪除時沒刪成的那幾筆這裡不再提。 */
async function sendRestore({ projectKey, queryClient }: BulkContext, keys: readonly string[]): Promise<BulkNotice> {
  const result = await bulkRestoreItems(projectKey, keys);
  refreshAfterRestore(queryClient, projectKey, keys);
  return { kind: 'restored', count: result.restored.length, failed: result.failed };
}

/**
 * 表格的批次操作：送出選取的項目、把結果換進快取、決定選取與提示（docs/DESIGN.md 8.3「批次操作」）。
 * 一筆一筆各自成敗：成功的取消選取、失敗的留著，方便接著看原因、處理完再試。
 * 全部成功的修改選取留著（可以接著做下一件事）；刪除之後選的是已經不在清單裡的項目，所以一定取消選取。
 */
export function useBulkEdit(projectKey: string, selection: ItemSelection): BulkEdit {
  const queryClient = useQueryClient();
  const [isPending, setIsPending] = useState(false);
  const [notice, setNotice] = useState<BulkNotice | null>(null);
  const context: BulkContext = { projectKey, queryClient, selection };

  /** 送出一件事：送出中不收第二件；整個請求失敗就是一則錯誤的提示。 */
  const run = async (action: () => Promise<BulkNotice | null>) => {
    if (isPending) return;
    setIsPending(true);
    setNotice(null);
    try {
      setNotice(await action());
    } catch (error) {
      setNotice({ kind: 'error', message: errorMessage(error) });
    } finally {
      setIsPending(false);
    }
  };

  const undoRemove = async () => {
    if (isPending || notice?.kind !== 'deleted' || notice.isRestoring) return;
    setNotice({ ...notice, isRestoring: true, restoreError: undefined });
    try {
      setNotice(await sendRestore(context, notice.keys));
    } catch (error) {
      setNotice({ ...notice, isRestoring: false, restoreError: errorMessage(error) });
    }
  };

  return {
    isPending,
    notice,
    update: (patch) => run(() => sendUpdate(context, patch)),
    remove: () => run(() => sendDelete(context)),
    undoRemove,
    dismissNotice: () => {
      setNotice(null);
    },
  };
}
