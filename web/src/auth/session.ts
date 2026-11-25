import {
  useMutation,
  useQuery,
  useQueryClient,
  type Query,
  type QueryClient,
} from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { isApiError } from '../api/http';
import { queryKeys } from '../api/queryKeys';
import { getSession, signIn, signOut } from '../api/session';
import type { SessionUser, SignInRequest } from '../api/types';
import { clearPanelMemory } from '../features/assistant-panel/panelMemory';
import { clearRecentItems } from '../features/jump/recentItems';
import { forgetAllDrafts } from '../features/settings/settingsDraftMemory';
import { clearAllDrafts } from '../lib/draftStorage';

const HTTP_UNAUTHORIZED = 401;

/** 沒登入不是錯誤，是一種狀態：回 null，讓畫面決定要不要導到登入頁。 */
async function fetchSessionUser(signal: AbortSignal): Promise<SessionUser | null> {
  try {
    return (await getSession(signal)).user;
  } catch (error) {
    if (isApiError(error) && error.status === HTTP_UNAUTHORIZED) {
      return null;
    }
    throw error;
  }
}

export function useSession() {
  return useQuery({
    queryKey: queryKeys.session,
    queryFn: ({ signal }) => fetchSessionUser(signal),
    // 登入狀態只在登入、登出、或任何請求收到 401 時改變，不必定期重抓。
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

function isNotSession(query: Query): boolean {
  return query.queryKey[0] !== queryKeys.session[0];
}

// 換人登入時，上一個人的專案與項目不能留在快取裡被下一個人看到。
function forgetUserData(queryClient: QueryClient): void {
  queryClient.removeQueries({ predicate: isNotSession });
}

/**
 * 帳號選單的登出成功、任何請求收到 401（工作階段失效）都經過這裡：打到一半的留言與說明、快速跳轉的最近看過的項目一起清掉，
 * 共用電腦上下一個登入的人看不到（lib/draftStorage.ts、features/jump/recentItems.ts）；設定頁送出後記在記憶體的草稿也是（features/settings/settingsDraftMemory.ts），AI 助理面板記的專案與大小也是（features/assistant-panel/panelMemory.ts）。
 */
export function markSignedOut(queryClient: QueryClient): void {
  clearAllDrafts();
  clearRecentItems();
  clearPanelMemory();
  forgetAllDrafts(queryClient);
  queryClient.setQueryData(queryKeys.session, null);
}

const rechecks = new WeakMap<QueryClient, Promise<void>>();

function isSessionSettled(queryClient: QueryClient): boolean {
  // 已經標成沒登入、或登入狀態的查詢自己正在問（它的 401 由它自己處理）：不必再問一次。
  return queryClient.getQueryData(queryKeys.session) === null || queryClient.isFetching({ queryKey: queryKeys.session }) > 0;
}

function isUnauthorizedFailure(query: Query): boolean {
  return query.state.status === 'error' && isApiError(query.state.error) && query.state.error.status === HTTP_UNAUTHORIZED;
}

/** 重抓之後還有查詢是 401（重抓的這段時間裡登入又失效了）。 */
function hasUnauthorizedQuery(queryClient: QueryClient): boolean {
  return queryClient.getQueryCache().findAll({ type: 'active', predicate: isUnauthorizedFailure }).length > 0;
}

// 重抓時又收到 401 之後，最多再問這麼多次；問完還是 401 就登出，不無限重來。
const MAX_EXTRA_RECHECKS = 1;

async function recheckSession(queryClient: QueryClient, extraChecksLeft = MAX_EXTRA_RECHECKS): Promise<void> {
  try {
    const { user } = await getSession();
    // 問的這段時間裡自己登出了（登出時伺服器回 401 也算成功）：不能把人又標回登入。
    if (queryClient.getQueryData(queryKeys.session) === null) return;
    queryClient.setQueryData(queryKeys.session, user);
    // 只重抓被 401 打掉的查詢（例如另一個分頁改了密碼、Cookie 還沒換）：別的原因失敗的不動。
    await queryClient.refetchQueries({ type: 'active', predicate: isUnauthorizedFailure });
  } catch (error) {
    // 還是 401 才算登出；斷線或伺服器暫時有問題時不能憑一次失敗就把人踢出去。
    if (isApiError(error) && error.status === HTTP_UNAUTHORIZED) markSignedOut(queryClient);
    return;
  }
  // 重抓的這段時間裡又收到 401：那些 401 當時被「正在重問」吃掉了，這裡補問一次。
  if (extraChecksLeft > 0 && hasUnauthorizedQuery(queryClient)) await recheckSession(queryClient, extraChecksLeft - 1);
}

/**
 * 任何請求收到 401 時先重問一次登入狀態，還是 401 才標成登出。改密碼、登出其他裝置會換掉 Cookie，
 * 同一個瀏覽器的另一個分頁在那幾十毫秒裡送出的請求會被 401 打回，不是真的登出了。
 * 同時來好幾個 401 只問一次（同一個 QueryClient 共用同一次重問）。
 */
export function handleUnauthorized(queryClient: QueryClient): Promise<void> {
  const running = rechecks.get(queryClient);
  if (running !== undefined) return running;
  if (isSessionSettled(queryClient)) return Promise.resolve();
  const started = recheckSession(queryClient).finally(() => {
    rechecks.delete(queryClient);
  });
  rechecks.set(queryClient, started);
  return started;
}

export function useSignIn() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SignInRequest) => signIn(request),
    onSuccess: ({ user }) => {
      forgetUserData(queryClient);
      queryClient.setQueryData(queryKeys.session, user);
    },
  });
}

async function signOutIgnoringExpired(): Promise<void> {
  try {
    await signOut();
  } catch (error) {
    // 工作階段已經失效也算登出成功。
    if (!(isApiError(error) && error.status === HTTP_UNAUTHORIZED)) {
      throw error;
    }
  }
}

export function useSignOut() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return useMutation({
    mutationFn: signOutIgnoringExpired,
    onSuccess: async () => {
      // 先標成已登出再換頁：登入頁看到的是登出後的狀態，不會又把人送回主畫面。
      // 換頁要和「已登出」在同一次渲染完成（flushSync），否則登入檢查會先看到「沒登入」，
      // 自己導到 /login?next=… 蓋掉這次換頁。
      markSignedOut(queryClient);
      await navigate('/login', { replace: true, flushSync: true });
      forgetUserData(queryClient);
    },
  });
}
