import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';
import type { DescriptionTemplate, ProjectDescription } from './types';

function descriptionPath(projectKey: string): string {
  return `/v1/projects/${encodeURIComponent(projectKey)}/description`;
}

/** 錯誤代碼：404 project.not_found。 */
export function getProjectDescription(projectKey: string, signal?: AbortSignal): Promise<ProjectDescription> {
  return apiRequest<ProjectDescription>(descriptionPath(projectKey), { signal });
}

/**
 * text 是 null 或空白就是清掉。錯誤代碼：404 project.not_found、403 work.forbidden、
 * 422（path 是 text）project.description_too_long、project.description_invalid、project.description_secret。
 */
export function replaceProjectDescription(projectKey: string, text: string | null): Promise<ProjectDescription> {
  return apiRequest<ProjectDescription>(descriptionPath(projectKey), { method: 'PUT', body: { text } });
}

export function listDescriptionTemplates(signal?: AbortSignal): Promise<readonly DescriptionTemplate[]> {
  return apiRequest<readonly DescriptionTemplate[]>('/v1/description-templates', { signal });
}

export function useProjectDescription(projectKey: string) {
  return useQuery({
    queryKey: queryKeys.projectDescription(projectKey),
    queryFn: ({ signal }) => getProjectDescription(projectKey, signal),
  });
}

export function useDescriptionTemplates() {
  return useQuery({
    queryKey: queryKeys.descriptionTemplates,
    queryFn: ({ signal }) => listDescriptionTemplates(signal),
    // 讀不到不重試：新增專案的對話框要等它，重試的間隔會讓「建立」卡好幾秒；它讀不到時畫面本來就能不用它（只剩「不套用」「空白」）。
    retry: false,
  });
}

/** 改說明：成功後把伺服器回來的說明（含更新的時刻與人）直接寫進快取，不必再重抓。 */
export function useReplaceProjectDescription(projectKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (text: string | null) => replaceProjectDescription(projectKey, text),
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.projectDescription(projectKey), saved);
    },
  });
}
