import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  AiConnection,
  AiConnectionInput,
  AiConnectionScope,
  AiDiscoveredModel,
  AiDiscoveryInput,
  LocalAgentMachine,
  OpenRouterLoginResult,
  OpenRouterLoginStart,
} from './aiTypes';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';

/**
 * 自己的 AI 帳號（個人連線）與「用 OpenRouter 登入」（internal/HANDOFF-ai-floating.md 第 4.4 節）。
 * 形狀和組織的連線（aiSettings.ts）相同，只是路徑在 /v1/account 底下、只看得到自己的；都只能用 Cookie。
 */
const BASE = '/v1/account/ai-connections';
const connectionPath = (id: string) => `${BASE}/${encodeURIComponent(id)}`;

export function listPersonalConnections(signal?: AbortSignal): Promise<readonly AiConnection[]> {
  return apiRequest<readonly AiConnection[]>(BASE, { signal });
}

/** 403 ai.personal_disabled（組織不允許）；422 ai.settings_invalid、ai.provider_failed；429。 */
export function createPersonalConnection(input: AiConnectionInput): Promise<AiConnection> {
  return apiRequest<AiConnection>(BASE, { method: 'POST', body: input });
}

export function updatePersonalConnection(id: string, input: AiConnectionInput): Promise<AiConnection> {
  return apiRequest<AiConnection>(connectionPath(id), { method: 'PUT', body: input });
}

export function refreshPersonalConnection(id: string): Promise<AiConnection> {
  return apiRequest<AiConnection>(`${connectionPath(id)}/refresh`, { method: 'POST' });
}

export function deletePersonalConnection(id: string): Promise<null> {
  return apiRequest<null>(connectionPath(id), { method: 'DELETE' });
}

export function discoverPersonalModels(input: AiDiscoveryInput): Promise<{ readonly models: readonly AiDiscoveredModel[] }> {
  return apiRequest<{ readonly models: readonly AiDiscoveredModel[] }>('/v1/account/ai-models/discover', { method: 'POST', body: input });
}

/** 403 ai.forbidden（組織的、不是管理員）或 ai.personal_disabled。 */
export function startOpenRouterLogin(scope: AiConnectionScope): Promise<OpenRouterLoginStart> {
  return apiRequest<OpenRouterLoginStart>('/v1/ai/openrouter/login', { method: 'POST', body: { scope } });
}

/** 422 ai.login_failed（說明是原因）、ai.settings_invalid；429。code 只能用一次。 */
export function completeOpenRouterLogin(input: { readonly code: string; readonly state: string }): Promise<OpenRouterLoginResult> {
  return apiRequest<OpenRouterLoginResult>('/v1/ai/openrouter/complete', { method: 'POST', body: input });
}

export function usePersonalConnections() {
  return useQuery({
    queryKey: queryKeys.personalAiConnections,
    queryFn: ({ signal }) => listPersonalConnections(signal),
    // 登入回來後帶著編號打開編輯對話框：一定要從伺服器讀最新的，不能拿快取裡還沒有那個連線的舊清單。
    staleTime: 0,
    gcTime: 0,
  });
}

/** 個人連線改了：自己的清單與「成員能選的 AI」（浮動對話框的模型選單）一起重讀。 */
function useInvalidatePersonal() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.personalAiConnections }),
      queryClient.invalidateQueries({ queryKey: queryKeys.aiOptions }),
    ]);
}

export function useSavePersonalConnection() {
  const invalidate = useInvalidatePersonal();
  return useMutation({
    mutationFn: ({ id, input }: { readonly id: string | null; readonly input: AiConnectionInput }) =>
      id === null ? createPersonalConnection(input) : updatePersonalConnection(id, input),
    onSuccess: () => invalidate(),
  });
}

export function useRefreshPersonalConnection() {
  const invalidate = useInvalidatePersonal();
  return useMutation({ mutationFn: refreshPersonalConnection, onSuccess: () => invalidate() });
}

export function useDeletePersonalConnection() {
  const invalidate = useInvalidatePersonal();
  return useMutation({ mutationFn: deletePersonalConnection, onSuccess: () => invalidate() });
}

export function useDiscoverPersonalModels() {
  return useMutation({ mutationFn: discoverPersonalModels });
}

export function useStartOpenRouterLogin() {
  return useMutation({ mutationFn: startOpenRouterLogin });
}

/** 登入完成建了連線：組織的設定頁、自己的清單、成員能選的 AI 全部要重讀。 */
export function useCompleteOpenRouterLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: completeOpenRouterLogin,
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ['ai'] }),
        queryClient.invalidateQueries({ queryKey: queryKeys.personalAiConnections }),
      ]),
  });
}

/** 本機代理（internal/HANDOFF-local-agent.md 第 4.4 節）：自己登記過的電腦；只能用 Cookie。 */
const LOCAL_AGENTS = '/v1/account/local-agents';
/** 本機代理每 15 秒重讀一次清單，「在線」才跟得上（分頁在背景時 TanStack Query 自己暫停）。 */
export const LOCAL_AGENTS_REFRESH_MS = 15_000;

export function listLocalAgents(signal?: AbortSignal): Promise<readonly LocalAgentMachine[]> {
  return apiRequest<readonly LocalAgentMachine[]>(LOCAL_AGENTS, { signal });
}

/** 404：已經不在了（別的分頁先移除）。 */
export function removeLocalAgent(id: string): Promise<null> {
  return apiRequest<null>(`${LOCAL_AGENTS}/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export function useLocalAgents(pollMs: number = LOCAL_AGENTS_REFRESH_MS) {
  return useQuery({
    queryKey: queryKeys.localAgents,
    queryFn: ({ signal }) => listLocalAgents(signal),
    refetchInterval: pollMs,
  });
}

/** 移除一台電腦：清單與「成員能選的 AI」（它的引擎從模型選單消失）一起重讀。成功或失敗都重讀，讓畫面和伺服器對上。 */
export function useRemoveLocalAgent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: removeLocalAgent,
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.localAgents }),
        queryClient.invalidateQueries({ queryKey: queryKeys.aiOptions }),
      ]),
  });
}
