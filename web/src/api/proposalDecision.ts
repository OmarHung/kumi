import { useMutation, useQuery, useQueryClient, type QueryClient, type QueryKey } from '@tanstack/react-query';
import { isApiError } from './http';

// 「別的分頁已經處理過」的記號：重讀之後待處理的區塊會卸載，訊息要留在卡片層級。不以 -proposals 結尾，撤回時不會被一起重讀掉。
const closedNoticeKey = (proposalId: string): QueryKey => ['ai', 'proposal-closed-notice', proposalId];

/** 套用或不要得到 409 *_closed（別的分頁已處理）。 */
export function useProposalClosedNotice(proposalId: string): boolean {
  const query = useQuery({ queryKey: closedNoticeKey(proposalId), queryFn: () => false, enabled: false, staleTime: Infinity, gcTime: Infinity });
  return query.data === true;
}

function isClosedError(error: unknown): boolean {
  return isApiError(error) && error.code.endsWith('_closed');
}

/** applying（別的分頁按了、正在套用）時重讀的間隔（毫秒），直到不是 applying；新增項目建議與四種新建議共用。 */
export const APPLYING_POLL_MS = 2000;

/** useQuery 的 refetchInterval：建議是 applying 時定時重讀，其他狀態不重讀。 */
export function pollWhileApplying(query: { readonly state: { readonly data?: { readonly status: string } } }): number | false {
  return query.state.data?.status === 'applying' ? APPLYING_POLL_MS : false;
}

/** 專案底下的項目（內容、活動、附件）的查詢鍵是 ['item', 編號]，編號以「專案代號-」開頭。 */
export function invalidateItemsOfProject(queryClient: QueryClient, projectKey: string): void {
  void queryClient.invalidateQueries({
    predicate: ({ queryKey }) => queryKey[0] === 'item' && typeof queryKey[1] === 'string' && queryKey[1].startsWith(`${projectKey}-`),
  });
}

interface DecisionOptions<T extends { readonly id: string }> {
  readonly proposal: T;
  readonly queryKey: QueryKey;
  readonly apply: (proposalId: string) => Promise<T>;
  readonly dismiss: (proposalId: string) => Promise<T>;
  /** 套用成功或失敗之後都呼叫：失敗（5xx、逾時、網路）時伺服器可能已經改了一部分。 */
  readonly refresh: (queryClient: QueryClient, decided: T | null) => void;
}

/**
 * 週期與里程碑、專案、留言、復原項目四種建議共用的「套用」「不要」：換成伺服器回的新狀態，並讓寫入影響到的快取失效。
 * 409（別的分頁已處理）與第一步就失敗的維持待處理：重讀，卡片才會變成伺服器現在的狀態。
 */
export function useProposalDecision<T extends { readonly id: string }>(options: DecisionOptions<T>) {
  const { proposal, queryKey, refresh } = options;
  const queryClient = useQueryClient();
  const settle = (decided: T) => {
    queryClient.setQueryData(queryKey, decided);
  };
  const reread = (error?: unknown) => {
    if (isClosedError(error)) queryClient.setQueryData(closedNoticeKey(proposal.id), true);
    void queryClient.invalidateQueries({ queryKey });
  };
  const apply = useMutation({
    mutationFn: () => options.apply(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      refresh(queryClient, decided);
    },
    onError: (error) => {
      reread(error);
      refresh(queryClient, null);
    },
  });
  const dismiss = useMutation({
    mutationFn: () => options.dismiss(proposal.id),
    onSuccess: settle,
    onError: (error) => {
      reread(error);
    },
  });
  return { apply, dismiss };
}
