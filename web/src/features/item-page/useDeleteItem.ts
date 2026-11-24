import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router';
import { isApiError } from '../../api/http';
import { deleteItem } from '../../api/items';
import { queryKeys } from '../../api/queryKeys';
import type { DeletedItem, Item } from '../../api/types';
import type { ArrivalState } from '../../lib/useArrivalNotice';
import { forgetItem, removeItemFromCache } from '../item-table/itemCache';
import { projectPageLink } from '../projects/projectViewPreference';
import { entryReturnTo } from '../work-items/itemLinks';
import { useIsInItemPanel, useItemPanel } from './ItemPanelContext';
import { childItemsQuery } from './knownChildren';

const HAS_CHILDREN = 'item.has_children';
const NOT_FOUND = 'item.not_found';

/** 別人剛刪掉（伺服器回 404 item.not_found）就是已經刪除了：結果和自己刪掉一樣，照成功的路徑走。 */
async function deleteOrAlreadyDeleted(itemKey: string): Promise<DeletedItem | null> {
  try {
    return await deleteItem(itemKey);
  } catch (error) {
    if (isApiError(error) && error.status === 404 && error.code === NOT_FOUND) return null;
    throw error;
  }
}

/**
 * 刪除之後（或改給別人而自己看不到之後）到哪裡、帶著提示過去：整頁回到專案頁（從表格或看板進來的回到原本的檢視，其他照上次用的檢視）；
 * 面板裡刪除的關上面板、留在原本的清單（檢視與條件照舊），提示一樣出現在清單上方（ItemPanelValue 的 closeWith）。
 */
export function useLeaveAfterDelete(projectKey: string): (state: ArrivalState) => Promise<void> {
  const location = useLocation();
  const navigate = useNavigate();
  const panel = useItemPanel();
  const isInPanel = useIsInItemPanel();
  if (isInPanel && panel !== null) return panel.closeWith;
  const returnTo = entryReturnTo(projectKey, location.state) ?? projectPageLink(projectKey);
  return async (state) => {
    await navigate(returnTo, { replace: true, state });
  };
}

/**
 * 刪除（封存）項目。成功後先把它從清單與我的工作的快取拿掉，再回到專案頁或原本的清單（useLeaveAfterDelete），
 * 頁面上說「已刪除 AGY-7「…」」；離開項目頁之後才丟掉它的快取，正在看的這一頁不會先去重抓出一個 404。
 * 伺服器說還有下層時，重新讀下層清單：使用者在項目頁上看得到是哪些。伺服器說找不到（別人剛刪掉）當成已經刪除。
 */
export function useDeleteItem(item: Item) {
  const queryClient = useQueryClient();
  const leave = useLeaveAfterDelete(item.projectKey);
  return useMutation({
    mutationFn: () => deleteOrAlreadyDeleted(item.key),
    onSuccess: async (deleted) => {
      removeItemFromCache(queryClient, item.projectKey, item.key);
      // 別人先刪掉的（404）不是我刪的：不說「已刪除」、也不給「復原」（那是別人的決定）。
      const state: ArrivalState =
        deleted === null
          ? { notice: `${item.key}「${item.title}」已經被別人刪除了。` }
          : { notice: `已刪除 ${item.key}「${item.title}」。`, undo: { projectKey: item.projectKey, keys: [item.key] } };
      await leave(state);
      forgetItem(queryClient, item.key);
    },
    onError: (error) => {
      if (isApiError(error) && error.code === HAS_CHILDREN) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.items(item.projectKey, childItemsQuery(item.key)), exact: true });
      }
    },
  });
}

export function isHasChildren(error: unknown): boolean {
  return isApiError(error) && error.code === HAS_CHILDREN;
}
