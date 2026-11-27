import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ItemBatchProposal, ItemDeletionProposal } from './aiTypes';
import { apiRequest } from './http';
import { refreshAfterItemsChanged, refreshAfterItemsDeleted } from '../features/item-table/revisionCache';
import { queryKeys } from './queryKeys';

const deletionPath = (proposalId: string) => `/v1/ai/item-deletion-proposals/${encodeURIComponent(proposalId)}`;
const batchPath = (proposalId: string) => `/v1/ai/item-batch-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.item_deletion_proposal_not_found：別人的、不存在的。 */
export function getItemDeletionProposal(proposalId: string, signal?: AbortSignal): Promise<ItemDeletionProposal> {
  return apiRequest<ItemDeletionProposal>(deletionPath(proposalId), { signal });
}

/** 409 ai.item_deletion_proposal_closed；全部失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyItemDeletionProposal(proposalId: string): Promise<ItemDeletionProposal> {
  return apiRequest<ItemDeletionProposal>(`${deletionPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.item_deletion_proposal_closed。 */
export function dismissItemDeletionProposal(proposalId: string): Promise<ItemDeletionProposal> {
  return apiRequest<ItemDeletionProposal>(`${deletionPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

/** 404 ai.item_batch_proposal_not_found：別人的、不存在的。 */
export function getItemBatchProposal(proposalId: string, signal?: AbortSignal): Promise<ItemBatchProposal> {
  return apiRequest<ItemBatchProposal>(batchPath(proposalId), { signal });
}

/** 409 ai.item_batch_proposal_closed；第一筆第一步就失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyItemBatchProposal(proposalId: string): Promise<ItemBatchProposal> {
  return apiRequest<ItemBatchProposal>(`${batchPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.item_batch_proposal_closed。 */
export function dismissItemBatchProposal(proposalId: string): Promise<ItemBatchProposal> {
  return apiRequest<ItemBatchProposal>(`${batchPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useItemDeletionProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.itemDeletionProposal(proposalId),
    queryFn: ({ signal }) => getItemDeletionProposal(proposalId, signal),
  });
}

export function useItemBatchProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.itemBatchProposal(proposalId),
    queryFn: ({ signal }) => getItemBatchProposal(proposalId, signal),
  });
}

/** 套用之後換成伺服器回的新狀態；刪除的項目從清單拿掉、不重抓它們自己（項目頁開著會是 404），「已刪除」那份清單一起失效。 */
export function useDecideItemDeletionProposal(proposal: ItemDeletionProposal) {
  const queryClient = useQueryClient();
  const itemKeys = proposal.items.map((item) => item.itemKey);
  const settle = (decided: ItemDeletionProposal) => {
    queryClient.setQueryData(queryKeys.itemDeletionProposal(proposal.id), decided);
  };
  const reread = () => {
    // 409（別的分頁已處理）：以伺服器現在的狀態為準。
    void queryClient.invalidateQueries({ queryKey: queryKeys.itemDeletionProposal(proposal.id) });
  };
  const apply = useMutation({
    mutationFn: () => applyItemDeletionProposal(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      refreshAfterItemsDeleted(queryClient, proposal.projectKey, decided.deletedKeys, itemKeys);
    },
    onError: () => {
      reread();
      // 失敗時不知道伺服器刪了哪些：清單照樣重抓，單一項目只標成過期（刪掉的重抓只會是 404）。
      refreshAfterItemsDeleted(queryClient, proposal.projectKey, [], itemKeys);
    },
  });
  const dismiss = useMutation({
    mutationFn: () => dismissItemDeletionProposal(proposal.id),
    onSuccess: settle,
    onError: reread,
  });
  return { apply, dismiss };
}

/** 套用之後換成伺服器回的新狀態，並讓每個動到的項目與清單失效。 */
export function useDecideItemBatchProposal(proposal: ItemBatchProposal) {
  const queryClient = useQueryClient();
  const itemKeys = proposal.items.map((item) => item.itemKey);
  const settle = (decided: ItemBatchProposal) => {
    queryClient.setQueryData(queryKeys.itemBatchProposal(proposal.id), decided);
  };
  const reread = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.itemBatchProposal(proposal.id) });
  };
  const apply = useMutation({
    mutationFn: () => applyItemBatchProposal(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      refreshAfterItemsChanged(queryClient, proposal.projectKey, itemKeys);
    },
    onError: () => {
      reread();
      refreshAfterItemsChanged(queryClient, proposal.projectKey, itemKeys);
    },
  });
  const dismiss = useMutation({
    mutationFn: () => dismissItemBatchProposal(proposal.id),
    onSuccess: settle,
    onError: reread,
  });
  return { apply, dismiss };
}
