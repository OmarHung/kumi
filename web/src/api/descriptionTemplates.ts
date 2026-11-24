import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';
import type { DescriptionTemplate, DescriptionTemplateInput } from './types';

function templatePath(id: string): string {
  return `/v1/description-templates/${encodeURIComponent(id)}`;
}

/**
 * 只有組織的擁有者與管理員能用，也不能用權杖。錯誤代碼：403 work.forbidden、
 * 409 description_template.name_taken、description_template.limit_reached、
 * 422（path 是 name 或 body）。
 */
export function createDescriptionTemplate(input: DescriptionTemplateInput): Promise<DescriptionTemplate> {
  return apiRequest<DescriptionTemplate>('/v1/description-templates', { method: 'POST', body: input });
}

/** 錯誤代碼同新增，另有 404 description_template.not_found（內建的與別的組織的都是 404）。 */
export function updateDescriptionTemplate(id: string, input: DescriptionTemplateInput): Promise<DescriptionTemplate> {
  return apiRequest<DescriptionTemplate>(templatePath(id), { method: 'PUT', body: input });
}

/** 回傳刪掉之前的格式。錯誤代碼：403、404 description_template.not_found。 */
export function deleteDescriptionTemplate(id: string): Promise<DescriptionTemplate> {
  return apiRequest<DescriptionTemplate>(templatePath(id), { method: 'DELETE' });
}

/** 預設的那一份會變（新增或修改時設成預設、刪掉預設），所以每次成功或失敗都重抓整份清單。 */
function useRefreshingTemplates<TVariables>(mutationFn: (variables: TVariables) => Promise<DescriptionTemplate>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    // 失敗也重抓：被擋下來多半是畫面上的清單舊了（別人已經刪掉、改過名）。
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.descriptionTemplates }),
  });
}

export function useCreateDescriptionTemplate() {
  return useRefreshingTemplates(createDescriptionTemplate);
}

export function useUpdateDescriptionTemplate() {
  return useRefreshingTemplates(({ id, input }: { readonly id: string; readonly input: DescriptionTemplateInput }) =>
    updateDescriptionTemplate(id, input),
  );
}

export function useDeleteDescriptionTemplate() {
  return useRefreshingTemplates(deleteDescriptionTemplate);
}
