import {
  infiniteQueryOptions,
  keepPreviousData,
  useInfiniteQuery,
  useQueries,
  useQuery,
  type QueryKey,
} from '@tanstack/react-query';
import { listAccessTokens } from './account';
import { listCommentRevisions } from './activity';
import { fetchActivityPage, type ActivityPages, type ActivityView } from './activityPages';
import { getChangeSet, listChangeSets } from './changesets';
import { getItem, listItems } from './items';
import { getMyWork } from './myWork';
import { listPeople } from './people';
import { getProject, listProjects } from './projects';
import { queryKeys } from './queryKeys';
import { searchItems, searchProjects } from './search';
import { listMyWorkViews, listSavedViews } from './savedViews';
import { listItemTypes } from './itemTypes';
import { listTemplates } from './templates';
import type { ItemQuery, ProjectDetail } from './types';

/**
 * 共用的讀取。多個畫面讀同一份資料時用同一個鍵，例如側欄與專案清單共用 useProjects，只會打一次 API。
 */
export function useProjects() {
  return useQuery({
    queryKey: queryKeys.projects,
    queryFn: ({ signal }) => listProjects(signal),
  });
}

export function useProject(projectKey: string) {
  return useQuery({
    queryKey: queryKeys.project(projectKey),
    queryFn: ({ signal }) => getProject(projectKey, signal),
  });
}

/** 一次讀好幾個專案的內容（藍圖），和 useProject 共用快取。回傳拿到的那些，以代號為鍵。 */
export function useProjectDetails(projectKeys: readonly string[]): ReadonlyMap<string, ProjectDetail> {
  return useQueries({
    queries: projectKeys.map((projectKey) => ({
      queryKey: queryKeys.project(projectKey),
      queryFn: ({ signal }: { readonly signal: AbortSignal }) => getProject(projectKey, signal),
    })),
    combine: (results) =>
      new Map(
        results.flatMap((result): [string, ProjectDetail][] =>
          result.data ? [[result.data.project.key, result.data]] : [],
        ),
      ),
  });
}

export function useTemplates() {
  return useQuery({
    queryKey: queryKeys.templates,
    queryFn: ({ signal }) => listTemplates(signal),
    // 模板是內建的，執行期間不會變。
    staleTime: Infinity,
  });
}

/** 跨專案「類型」篩選的選項。藍圖不常改：五分鐘內不重抓。 */
export function useItemTypes() {
  return useQuery({
    queryKey: queryKeys.itemTypes,
    queryFn: ({ signal }) => listItemTypes(signal),
    staleTime: 5 * 60 * 1000,
  });
}

/** 自己的「我的工作」檢視。 */
export function useMyWorkViews() {
  return useQuery({
    queryKey: queryKeys.myWorkViews,
    queryFn: ({ signal }) => listMyWorkViews(signal),
  });
}

export function useMyWork() {
  return useQuery({
    queryKey: queryKeys.myWork,
    queryFn: ({ signal }) => getMyWork(signal),
  });
}

/** 跨專案搜尋項目（快速跳轉）。text 空的時候不發請求；不接上一次的結果，換字之後舊的不留著。 */
export function useItemSearchQuery(text: string, limit: number) {
  return useQuery({
    queryKey: queryKeys.searchItems(text, limit),
    queryFn: ({ signal }) => searchItems(text, limit, signal),
    enabled: text !== '',
    select: (result) => result.items,
  });
}

/** 搜尋專案的代號、名稱與說明（快速跳轉、專案清單頁）。text 空的時候不發請求；換字之後舊的結果預設不留著（keepsPreviousResult 才留）。 */
export function useProjectSearchQuery(text: string, limit: number, { keepsPreviousResult = false } = {}) {
  return useQuery({
    queryKey: queryKeys.searchProjects(text, limit),
    queryFn: ({ signal }) => searchProjects(text, limit, signal),
    enabled: text !== '',
    // 專案清單頁改字時先留著舊的結果（變淡），不先變骨架再變回來；快速跳轉不留（舊結果和輸入的字對不上）。
    placeholderData: keepsPreviousResult ? keepPreviousData : undefined,
  });
}

/** 項目清單一頁一頁往下載：query 不含游標，游標由這裡依上一頁的 nextCursor 帶。換條件時先留著舊的結果。 */
function itemPagesOptions(projectKey: string, query: ItemQuery, queryKey: QueryKey) {
  return infiniteQueryOptions({
    queryKey,
    queryFn: ({ pageParam, signal }) =>
      listItems(projectKey, pageParam === null ? query : { ...query, cursor: pageParam }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
    placeholderData: keepPreviousData,
  });
}

/**
 * 專案的項目清單（表格、下層項目），「載入更多」往下接；表格不會整個閃成「載入中」。
 * isEnabled 是 false 時不發請求，只讀已經在快取裡的（例如刪除前看看已經載入的下層）。
 */
export function useProjectItems(projectKey: string, query: ItemQuery, isEnabled = true) {
  return useInfiniteQuery({ ...itemPagesOptions(projectKey, query, queryKeys.items(projectKey, query)), enabled: isEnabled });
}

/** 自己在這個專案的檢視（個人的，依名稱排序）。 */
export function useSavedViews(projectKey: string) {
  return useQuery({
    queryKey: queryKeys.savedViews(projectKey),
    queryFn: ({ signal }) => listSavedViews(projectKey, signal),
  });
}

/** 看板的一欄。和表格同一個端點，但查詢鍵不同：改了狀態的項目要在欄與欄之間搬（itemCache.ts）。 */
export function useBoardColumnItems(projectKey: string, query: ItemQuery, isEnabled = true) {
  return useInfiniteQuery({ ...itemPagesOptions(projectKey, query, queryKeys.boardColumn(projectKey, query)), enabled: isEnabled });
}

/** 行事曆與時間軸的清單。和表格同一個端點，但查詢鍵不同：動到條件的修改要整份重抓（itemCache.ts）。 */
export function useDateViewItems(projectKey: string, query: ItemQuery, isEnabled = true) {
  return useInfiniteQuery({ ...itemPagesOptions(projectKey, query, queryKeys.dateView(projectKey, query)), enabled: isEnabled });
}

/** 一次最多拿這麼多個可以當上層的項目；超過的要用搜尋縮小範圍（之後再做）。 */
export const PARENT_CANDIDATE_LIMIT = 200;

/** 可以當上層項目的項目（類型符合 parents 的）。只有真的要選上層項目時才讀。 */
export function useParentCandidates(projectKey: string, types: readonly string[], isEnabled: boolean) {
  return useQuery({
    queryKey: queryKeys.parentCandidates(projectKey, types),
    queryFn: ({ signal }) =>
      listItems(projectKey, { type: types, limit: PARENT_CANDIDATE_LIMIT }, signal),
    enabled: isEnabled && types.length > 0,
    select: (page) => page.items,
  });
}

export function usePeople(isEnabled = true) {
  return useQuery({
    queryKey: queryKeys.people,
    queryFn: ({ signal }) => listPeople(signal),
    enabled: isEnabled,
  });
}

/**
 * 自己的存取權杖清單（我的帳號）。明文不在這裡：只有建立的那一次回應帶它，由對話框自己拿著。
 * pollMs：每隔多久重讀一次（設定精靈看終端機那邊做完了沒；分頁看不見時 TanStack Query 預設不讀）。
 */
export function useAccessTokens(pollMs?: number) {
  return useQuery({
    queryKey: queryKeys.accessTokens,
    queryFn: ({ signal }) => listAccessTokens(signal),
    refetchInterval: pollMs,
  });
}

/** 單一項目（帶說明）。表格與項目頁改好的項目會寫進同一個鍵（itemCache.ts）。 */
export function useItem(itemKey: string, isEnabled = true) {
  return useQuery({
    queryKey: queryKeys.item(itemKey),
    enabled: isEnabled,
    queryFn: async ({ signal, client }) => {
      const item = await getItem(itemKey, signal);
      // 網址的編號可能是別的寫法（smk-7、SMK-07），後端回的是正規的編號；也寫進正規編號的鍵，
      // 項目頁換到正規的網址時不必再讀一次，之後的修改也都寫在那個鍵。
      if (item.key !== itemKey) {
        client.setQueryData(queryKeys.item(item.key), item);
      }
      return item;
    },
  });
}

/** 項目的活動與留言（view：只看留言或全部，各自一份快取），由舊到新，一頁一頁往後接；重抓時保持原本讀到的範圍（activityPages.ts）。 */
export function useItemActivity(itemKey: string, view: ActivityView) {
  return useInfiniteQuery({
    queryKey: queryKeys.activityView(itemKey, view),
    queryFn: ({ pageParam, signal, client, queryKey }) =>
      fetchActivityPage(itemKey, view, pageParam, signal, client.getQueryData<ActivityPages>(queryKey)),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
  });
}

/** 一則留言的版本（新的在前）；只在開著「留言的版本」時讀，活動失效時一起重抓（鍵在活動的前綴底下）。 */
export function useCommentRevisions(itemKey: string, commentId: string) {
  return useQuery({
    queryKey: queryKeys.commentRevisions(itemKey, commentId),
    queryFn: ({ signal }) => listCommentRevisions(itemKey, commentId, signal),
  });
}

/** 專案的變更集，新的在前。沒有 blueprint.read 的人拿不到（403），呼叫端用 isEnabled 不發請求。 */
export function useChangeSets(projectKey: string, isEnabled = true) {
  return useQuery({
    queryKey: queryKeys.changeSets(projectKey),
    queryFn: ({ signal }) => listChangeSets(projectKey, signal),
    enabled: isEnabled,
  });
}

/** 一份變更集的內容。等待核准的每次讀都依目前的資料重算影響與擋住的原因。 */
export function useChangeSet(projectKey: string, number: number, isEnabled = true) {
  return useQuery({
    queryKey: queryKeys.changeSet(projectKey, number),
    queryFn: ({ signal }) => getChangeSet(projectKey, number, signal),
    enabled: isEnabled,
  });
}
