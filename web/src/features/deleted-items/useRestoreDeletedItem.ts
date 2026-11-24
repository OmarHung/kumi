import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { isApiError, errorMessage } from '../../api/http';
import { restoreItem, type DeletedItemSummary } from '../../api/itemRecovery';
import { queryKeys } from '../../api/queryKeys';
import { refreshAfterRestore } from '../item-table/itemCache';
import type { DeletedItemPages } from './useDeletedItemPages';
import { flattenDeletedItems, focusTargetAfterRemoving, withoutDeletedItem } from './useDeletedItemPages';

const PARENT_DELETED = 'item.parent_deleted';

/** 頁面上方的提示：復原成功、上層也刪除了（附上層的編號）、其他錯誤（伺服器的訊息）。 */
export type RestoreNotice =
  | { readonly kind: 'restored'; readonly key: string; readonly title: string }
  | { readonly kind: 'parent_deleted'; readonly key: string; readonly parentKey: string | null; readonly message: string }
  | { readonly kind: 'error'; readonly message: string };

function failureNotice(error: unknown, item: DeletedItemSummary): RestoreNotice {
  if (isApiError(error) && error.code === PARENT_DELETED) {
    return { kind: 'parent_deleted', key: item.key, parentKey: item.parentKey, message: error.message };
  }
  return { kind: 'error', message: errorMessage(error) };
}

interface UseRestoreDeletedItemOptions {
  readonly projectKey: string;
  /** 一列復原成功、已經從清單拿掉之後：焦點要去的那一列的編號，沒有就是 null（去頁面標題）。 */
  readonly onRemoved: (focusKey: string | null) => void;
}

/**
 * 復原一筆。成功就先把那一列從快取拿掉（不等重抓）、換成「已復原」的提示；失敗留著那一列、換成說明。
 * 成功或失敗都讓清單與我的工作失效：伺服器上的狀態才是準的（例如別人已經復原了它）。
 * 每一筆各自送出中；同一筆送出中再按不會重複送出。
 */
export function useRestoreDeletedItem({ projectKey, onRemoved }: UseRestoreDeletedItemOptions) {
  const queryClient = useQueryClient();
  const [pendingKeys, setPendingKeys] = useState<ReadonlySet<string>>(new Set());
  const [notice, setNotice] = useState<RestoreNotice | null>(null);

  const setPending = (itemKey: string, isPending: boolean) => {
    setPendingKeys((current) => {
      const next = new Set(current);
      if (isPending) next.add(itemKey);
      else next.delete(itemKey);
      return next;
    });
  };

  const restore = async (item: DeletedItemSummary) => {
    if (pendingKeys.has(item.key)) return;
    setPending(item.key, true);
    const listKey = queryKeys.deletedItems(projectKey);
    try {
      const restored = await restoreItem(item.key);
      const current = queryClient.getQueryData<DeletedItemPages>(listKey);
      const focusKey = focusTargetAfterRemoving(flattenDeletedItems(current), item.key);
      // 焦點的去處先記下來、再拿掉那一列：拿掉之後畫面更新時才接得手。
      onRemoved(focusKey);
      queryClient.setQueryData<DeletedItemPages | undefined>(listKey, (data) => withoutDeletedItem(data, item.key));
      setNotice({ kind: 'restored', key: restored.key, title: restored.title });
    } catch (error) {
      setNotice(failureNotice(error, item));
    } finally {
      setPending(item.key, false);
      // 和批次、換頁提示的「復原」同一組失效（含上層的選項）：itemCache.ts 的 refreshAfterRestore。
      refreshAfterRestore(queryClient, projectKey, [item.key]);
    }
  };

  return {
    pendingKeys,
    notice,
    restore,
    dismissNotice: () => {
      setNotice(null);
    },
  };
}
