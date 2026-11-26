import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { archiveProject, renameProject, unarchiveProject } from '../../api/projects';
import { queryKeys } from '../../api/queryKeys';

/** 專案清單、這個專案（含它底下的項目）與我的工作：封存與取消封存會改變它們看到的內容（權限、列出的項目）。 */
function invalidateAfterArchiveChange(queryClient: QueryClient, projectKey: string): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
  void queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
  // 專案總覽列的是沒封存的專案：封存、取消封存會讓列出現或消失。
  void queryClient.invalidateQueries({ queryKey: queryKeys.overviews });
}

/** 封存：可以復原，所以直接做、不確認。成功後專案換成唯讀（重抓專案之後 archivedAt 有值、權限只剩讀取）。 */
export function useArchiveProject(projectKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => archiveProject(projectKey),
    onSuccess: () => {
      invalidateAfterArchiveChange(queryClient, projectKey);
    },
  });
}

export function useUnarchiveProject(projectKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unarchiveProject(projectKey),
    onSuccess: () => {
      invalidateAfterArchiveChange(queryClient, projectKey);
    },
  });
}

/** 改名：只換名稱，專案（頁列、麵包屑）與專案清單（側欄）重抓；專案底下的項目不受影響，不必重抓。 */
export function useRenameProject(projectKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => renameProject(projectKey, name),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
      void queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey), exact: true });
      void queryClient.invalidateQueries({ queryKey: queryKeys.overviews });
    },
  });
}
