import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { LocalEngineKind } from './aiTypes';
import type {
  HostAgent,
  HostAgentInput,
  HostAgentOverview,
  HostAgentPairing,
  HostLoginStart,
  HostSource,
  HostSourceInput,
  HostSourceUsage,
} from './hostAgentTypes';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';

/**
 * 主機 AI 代理的管理（internal/HANDOFF-host-agent.md 第 5.1 節）：只有組織擁有者、只能用 Cookie；管理員與成員一律 403 ai.host_owner_only。
 * 配對碼、登入碼只在建立的回應裡：由呼叫端的對話框自己拿著，不放進查詢的快取。
 */
const BASE = '/v1/ai/host';
const agentPath = (id: string) => `${BASE}/agents/${encodeURIComponent(id)}`;

/** 代理與來源的清單每 5 秒重讀（分頁在背景時 TanStack Query 自己暫停）：在線、排隊的件數、登入的進度才跟得上。 */
export const HOST_AGENTS_REFRESH_MS = 5_000;

export function getHostOverview(signal?: AbortSignal): Promise<HostAgentOverview> {
  return apiRequest<HostAgentOverview>(BASE, { signal });
}

/** 422 名稱不對、已經到上限。 */
export function createHostAgent(name: string): Promise<HostAgentPairing> {
  return apiRequest<HostAgentPairing>(`${BASE}/agents`, { method: 'POST', body: { name } });
}

/** 改名稱或停用（enabled: false）／啟用。 */
export function updateHostAgent(id: string, input: HostAgentInput): Promise<HostAgent> {
  return apiRequest<HostAgent>(agentPath(id), { method: 'PATCH', body: input });
}

export function repairHostAgent(id: string): Promise<HostAgentPairing> {
  return apiRequest<HostAgentPairing>(`${agentPath(id)}/pairing`, { method: 'POST' });
}

export function deleteHostAgent(id: string): Promise<null> {
  return apiRequest<null>(agentPath(id), { method: 'DELETE' });
}

/** engine 是 claude_code 或 codex。422 ai.host_agent_unavailable（還沒配對或已停用）。 */
export function startHostLogin(agentId: string, engine: LocalEngineKind): Promise<HostLoginStart> {
  return apiRequest<HostLoginStart>(`${agentPath(agentId)}/engines/${engine}/login`, { method: 'POST' });
}

export function logoutHostEngine(agentId: string, engine: LocalEngineKind): Promise<null> {
  return apiRequest<null>(`${agentPath(agentId)}/engines/${engine}/logout`, { method: 'POST' });
}

/** 作廢進行中的登入。 */
export function cancelHostLogin(loginId: string): Promise<null> {
  return apiRequest<null>(`${BASE}/logins/${encodeURIComponent(loginId)}`, { method: 'DELETE' });
}

/** 422 ai.host_billing_not_shareable（引擎登入的是訂閱帳號）等。 */
export function createHostSource(input: HostSourceInput): Promise<HostSource> {
  return apiRequest<HostSource>(`${BASE}/sources`, { method: 'POST', body: input });
}

export function updateHostSource(id: string, input: HostSourceInput): Promise<HostSource> {
  return apiRequest<HostSource>(`${BASE}/sources/${encodeURIComponent(id)}`, { method: 'PUT', body: input });
}

export function deleteHostSource(id: string): Promise<null> {
  return apiRequest<null>(`${BASE}/sources/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

/** month 是 yyyy-MM；省略是這個月（組織的時區）。 */
export function getHostSourceUsage(id: string, month: string | null, signal?: AbortSignal): Promise<HostSourceUsage> {
  const query = month === null ? '' : `?month=${encodeURIComponent(month)}`;
  return apiRequest<HostSourceUsage>(`${BASE}/sources/${encodeURIComponent(id)}/usage${query}`, { signal });
}

/** 只有擁有者的畫面才掛上（enabled 由呼叫端的元件是否渲染決定）；403 重試也一樣。 */
export function useHostOverview() {
  return useQuery({
    queryKey: queryKeys.hostAgents,
    queryFn: ({ signal }) => getHostOverview(signal),
    retry: false,
    refetchInterval: HOST_AGENTS_REFRESH_MS,
  });
}

export function useHostSourceUsage(id: string, month: string | null) {
  return useQuery({
    queryKey: queryKeys.hostSourceUsage(id, month),
    queryFn: ({ signal }) => getHostSourceUsage(id, month, signal),
    staleTime: 0,
    gcTime: 0,
  });
}

/**
 * 管理的動作做完（成功或失敗）都重讀：清單和伺服器對上，成員的模型選單（來源、notice）也要換。
 * 回應裡的配對碼、登入碼不寫進任何快取。
 */
function useInvalidateHost() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.hostAgents }),
      queryClient.invalidateQueries({ queryKey: queryKeys.aiOptions }),
    ]);
}

export function useCreateHostAgent() {
  const invalidate = useInvalidateHost();
  // gcTime 0：回應裡的配對碼、登入碼不留在 mutation 的快取（呼叫端拿到之後 reset）。
  return useMutation({ mutationFn: createHostAgent, gcTime: 0, onSettled: () => {
      void invalidate();
    } });
}

export function useUpdateHostAgent() {
  const invalidate = useInvalidateHost();
  return useMutation({
    mutationFn: ({ id, input }: { readonly id: string; readonly input: HostAgentInput }) => updateHostAgent(id, input),
    onSettled: () => {
      void invalidate();
    },
  });
}

/**
 * 等重讀完才算結束：警告列（和它的「重新配對」鈕）要等清單重讀才換掉，配對一結束就放行的話，
 * 連按的第二下落在這個空檔就會再配對一次，第二組配對碼讓第一組失效。
 */
export function useRepairHostAgent() {
  const invalidate = useInvalidateHost();
  return useMutation({ mutationFn: repairHostAgent, gcTime: 0, onSettled: () => invalidate() });
}

export function useDeleteHostAgent() {
  const invalidate = useInvalidateHost();
  return useMutation({ mutationFn: deleteHostAgent, onSettled: () => {
      void invalidate();
    } });
}

export function useStartHostLogin() {
  const invalidate = useInvalidateHost();
  return useMutation({
    mutationFn: ({ agentId, engine }: { readonly agentId: string; readonly engine: LocalEngineKind }) => startHostLogin(agentId, engine),
    gcTime: 0,
    onSettled: () => {
      void invalidate();
    },
  });
}

export function useLogoutHostEngine() {
  const invalidate = useInvalidateHost();
  return useMutation({
    mutationFn: ({ agentId, engine }: { readonly agentId: string; readonly engine: LocalEngineKind }) => logoutHostEngine(agentId, engine),
    onSettled: () => {
      void invalidate();
    },
  });
}

export function useCancelHostLogin() {
  const invalidate = useInvalidateHost();
  return useMutation({ mutationFn: cancelHostLogin, onSettled: () => {
      void invalidate();
    } });
}

export function useSaveHostSource() {
  const invalidate = useInvalidateHost();
  return useMutation({
    mutationFn: ({ id, input }: { readonly id: string | null; readonly input: HostSourceInput }) =>
      id === null ? createHostSource(input) : updateHostSource(id, input),
    onSettled: () => {
      void invalidate();
    },
  });
}

export function useDeleteHostSource() {
  const invalidate = useInvalidateHost();
  return useMutation({ mutationFn: deleteHostSource, onSettled: () => {
      void invalidate();
    } });
}
