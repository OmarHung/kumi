import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  AiConnection,
  AiConnectionInput,
  AiDiscoveredModel,
  AiDiscoveryInput,
  AiSettings,
  AiSettingsInput,
} from './aiTypes';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';

/** 前綴：設定與成員能選的選項一起失效（存了任何東西之後）。 */
const AI_PREFIX = ['ai'] as const;

/** 只有組織的擁有者與管理員。403 ai.forbidden。 */
export function getAiSettings(signal?: AbortSignal): Promise<AiSettings> {
  return apiRequest<AiSettings>('/v1/ai/settings', { signal });
}

/** 403；422 ai.settings_invalid。 */
export function saveAiSettings(input: AiSettingsInput): Promise<AiSettings> {
  return apiRequest<AiSettings>('/v1/ai/settings', { method: 'PUT', body: input });
}

/** 403；422 ai.settings_invalid（細項路徑是欄位名稱）、ai.provider_failed（訊息是原因）；429 每人每分鐘最多 10 次。 */
export function createAiConnection(input: AiConnectionInput): Promise<AiConnection> {
  return apiRequest<AiConnection>('/v1/ai/connections', { method: 'POST', body: input });
}

/** apiKey 是 null 或省略：沿用存的；空字串：清掉。404 ai.connection_not_found。 */
export function updateAiConnection(id: string, input: AiConnectionInput): Promise<AiConnection> {
  return apiRequest<AiConnection>(`/v1/ai/connections/${encodeURIComponent(id)}`, { method: 'PUT', body: input });
}

/** 用存著的設定重新讀模型清單。422 ai.provider_failed。 */
export function refreshAiConnection(id: string): Promise<AiConnection> {
  return apiRequest<AiConnection>(`/v1/ai/connections/${encodeURIComponent(id)}/refresh`, { method: 'POST' });
}

export function deleteAiConnection(id: string): Promise<null> {
  return apiRequest<null>(`/v1/ai/connections/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

/** 用還沒存的設定讀供應商的模型清單。404 ai.connection_not_found（connectionId 找不到）。 */
export function discoverAiModels(input: AiDiscoveryInput): Promise<{ readonly models: readonly AiDiscoveredModel[] }> {
  return apiRequest<{ readonly models: readonly AiDiscoveredModel[] }>('/v1/ai/models/discover', { method: 'POST', body: input });
}

export function useAiSettings() {
  return useQuery({
    queryKey: queryKeys.aiSettings,
    queryFn: ({ signal }) => getAiSettings(signal),
    // 沒權限（403）重試也一樣；打開頁面時一定要拿最新的，不用快取的舊設定當表單的起點。
    retry: false,
    staleTime: 0,
    gcTime: 0,
  });
}

/** 存了就讓整個 ai 前綴失效：設定頁與成員的選項都要重讀。 */
function useInvalidateAi() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: AI_PREFIX });
}

export function useSaveAiSettings() {
  const queryClient = useQueryClient();
  const invalidate = useInvalidateAi();
  return useMutation({
    mutationFn: saveAiSettings,
    onSuccess: (saved) => {
      // 先放進快取：表單的「有沒有改過」拿它當起點，重抓回來之前不會被當成還沒存。
      queryClient.setQueryData(queryKeys.aiSettings, saved);
      void invalidate();
    },
  });
}

export function useSaveAiConnection() {
  const invalidate = useInvalidateAi();
  return useMutation({
    mutationFn: ({ id, input }: { readonly id: string | null; readonly input: AiConnectionInput }) =>
      id === null ? createAiConnection(input) : updateAiConnection(id, input),
    onSuccess: () => invalidate(),
  });
}

export function useRefreshAiConnection() {
  const invalidate = useInvalidateAi();
  return useMutation({ mutationFn: refreshAiConnection, onSuccess: () => invalidate() });
}

export function useDeleteAiConnection() {
  const invalidate = useInvalidateAi();
  return useMutation({ mutationFn: deleteAiConnection, onSuccess: () => invalidate() });
}

export function useDiscoverAiModels() {
  return useMutation({ mutationFn: discoverAiModels });
}
