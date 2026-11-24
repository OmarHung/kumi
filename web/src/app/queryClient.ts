import { MutationCache, QueryClient, type DefaultOptions } from '@tanstack/react-query';
import { isApiError } from '../api/http';
import { queryKeys } from '../api/queryKeys';

const MAX_RETRIES = 2;
const STALE_TIME_MS = 30_000;
const HTTP_CLIENT_ERROR_MIN = 400;
const HTTP_CLIENT_ERROR_MAX = 499;

// 4xx（沒權限、找不到、太頻繁）重試也一樣，只會多等；網路斷線與 5xx 才值得再試。
function shouldRetry(failureCount: number, error: unknown): boolean {
  const isClientError =
    isApiError(error) &&
    error.status >= HTTP_CLIENT_ERROR_MIN &&
    error.status <= HTTP_CLIENT_ERROR_MAX;
  return !isClientError && failureCount < MAX_RETRIES;
}

/** 寫入被封存的專案擋下（409）：伺服器的代碼，和後端的 WorkErrorCodes.ProjectArchived 一樣。 */
export const PROJECT_ARCHIVED_CODE = 'project.archived';

// ['project', 專案代號]：專案的內容（權限、封存的時刻）。底下的項目清單等不必跟著重抓。
const PROJECT_DETAIL_KEY_LENGTH = 2;

function isProjectDetailKey(queryKey: readonly unknown[]): boolean {
  return queryKey[0] === 'project' && queryKey.length === PROJECT_DETAIL_KEY_LENGTH;
}

/**
 * 別人剛封存了專案、這一頁還是舊的：寫入回 409 project.archived 時，讓專案的內容與專案清單失效，
 * 頁面重抓後權限只剩讀取、換成唯讀並出現封存的提示。錯誤沒有帶專案代號，所以所有載入過的專案內容都重抓（通常只有一兩個）。
 * 放在 mutation cache 的 onError，一處處理，各個寫入的畫面不必各自處理。
 */
function refreshWhenProjectArchived(queryClient: QueryClient, error: unknown): void {
  if (!isApiError(error) || error.code !== PROJECT_ARCHIVED_CODE) return;
  void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
  void queryClient.invalidateQueries({ predicate: (query) => isProjectDetailKey(query.queryKey) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
}

export function createQueryClient(overrides: DefaultOptions = {}): QueryClient {
  const queryClient: QueryClient = new QueryClient({
    mutationCache: new MutationCache({
      onError: (error) => {
        refreshWhenProjectArchived(queryClient, error);
      },
    }),
    defaultOptions: {
      queries: { retry: shouldRetry, staleTime: STALE_TIME_MS, ...overrides.queries },
      mutations: { retry: false, ...overrides.mutations },
    },
  });
  return queryClient;
}
