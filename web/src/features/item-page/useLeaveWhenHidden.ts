import { useQueryClient } from '@tanstack/react-query';
import { isApiError } from '../../api/http';
import { getItem } from '../../api/items';
import { queryKeys } from '../../api/queryKeys';
import type { Item } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { readsAllItems } from '../../domain/projectPermissions';
import { forgetItem, removeItemFromCache } from '../item-table/itemCache';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useLeaveAfterDelete } from './useDeleteItem';

const HTTP_NOT_FOUND = 404;

/**
 * 負責人改好之後的善後（項目面板與項目頁）：只看自己的項目的人把項目改給別人，如果建立的人也不是他，
 * 他就看不到這個項目了，留在原地重抓只會變成「找不到項目」。所以離開（面板關上、整頁回專案的清單）並說明。
 * 建立的人網頁拿不到，所以不自己判斷，直接問伺服器還讀不讀得到（404 才離開）；看得到全部的人與改給自己的人不多發這個請求。
 */
export function useLeaveWhenHidden(): (saved: Item) => void {
  const queryClient = useQueryClient();
  const { personId } = useCurrentUser();
  const { detail } = useProjectTable();
  const leave = useLeaveAfterDelete(detail.project.key);
  return (saved) => {
    if (readsAllItems(detail.myPermissions) || saved.assignee === null || saved.assignee.personId === personId) return;
    const { assignee } = saved;
    void (async () => {
      try {
        await queryClient.query({ queryKey: queryKeys.item(saved.key), queryFn: ({ signal }) => getItem(saved.key, signal), staleTime: 0 });
      } catch (error) {
        if (!isApiError(error) || error.status !== HTTP_NOT_FOUND) return;
        removeItemFromCache(queryClient, saved.projectKey, saved.key);
        await leave({ notice: `已經改給「${assignee.displayName}」負責，你看不到這個項目了。` });
        forgetItem(queryClient, saved.key);
      }
    })();
  };
}
