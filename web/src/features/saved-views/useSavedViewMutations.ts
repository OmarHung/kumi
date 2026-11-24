import { useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { createMyWorkView, createSavedView, deleteMyWorkView, deleteSavedView, replaceMyWorkView, replaceSavedView } from '../../api/savedViews';
import { queryKeys } from '../../api/queryKeys';
import type { SavedView, SavedViewDraft } from '../../api/types';

/** 一份檢視清單的讀寫：專案的與「我的工作」的各一份，選單與對話框不必知道是哪一種。 */
export interface ViewApi {
  /** 這份清單的快取鍵：改完（成功或失敗）都讓它重抓。 */
  readonly queryKey: QueryKey;
  readonly create: (draft: SavedViewDraft) => Promise<SavedView>;
  readonly replace: (id: string, draft: SavedViewDraft) => Promise<SavedView>;
  readonly remove: (id: string) => Promise<unknown>;
}

export function projectViewApi(projectKey: string): ViewApi {
  return {
    queryKey: queryKeys.savedViews(projectKey),
    create: (draft) => createSavedView(projectKey, draft),
    replace: (id, draft) => replaceSavedView(projectKey, id, draft),
    remove: (id) => deleteSavedView(projectKey, id),
  };
}

export const MY_WORK_VIEW_API: ViewApi = {
  queryKey: queryKeys.myWorkViews,
  create: createMyWorkView,
  replace: replaceMyWorkView,
  remove: deleteMyWorkView,
};

/**
 * 檢視的新增、整個換掉與刪除。結束後（成功或失敗）等清單重抓完才算完成：呼叫端接著算「目前是哪一個檢視」時，
 * 清單裡已經是新的樣子（按鈕上的名稱不會慢一拍）；失敗也重抓，因為 404 代表檢視在別處已經被刪掉，清單還留著它是舊的。
 */
export function useViewMutations(api: ViewApi) {
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: api.queryKey });
  return {
    create: useMutation({
      mutationFn: (draft: SavedViewDraft) => api.create(draft),
      onSettled: refresh,
    }),
    replace: useMutation({
      mutationFn: ({ id, draft }: { readonly id: string; readonly draft: SavedViewDraft }) => api.replace(id, draft),
      onSettled: refresh,
    }),
    remove: useMutation({
      mutationFn: (id: string) => api.remove(id),
      onSettled: refresh,
    }),
  };
}

export function useSavedViewMutations(projectKey: string) {
  return useViewMutations(projectViewApi(projectKey));
}

export type SavedViewMutations = ReturnType<typeof useViewMutations>;
