import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';
import type { CalendarDate } from './types';

/** 一群項目的完成情形。已刪除的項目不算；沒有填估計的項目不計入估計的總和（一個都沒填時是 null）。 */
export interface ItemProgress {
  readonly total: number;
  readonly done: number;
  readonly canceled: number;
  readonly estimateTotal: number | null;
  readonly estimateDone: number | null;
}

export interface Cycle {
  readonly id: string;
  readonly name: string;
  readonly startDate: CalendarDate;
  readonly endDate: CalendarDate;
  readonly progress: ItemProgress;
}

export interface CycleDraft {
  readonly name: string;
  readonly startDate: CalendarDate;
  readonly endDate: CalendarDate;
}

export interface Milestone {
  readonly id: string;
  readonly name: string;
  readonly dueDate: CalendarDate;
  readonly description: string | null;
  readonly progress: ItemProgress;
}

export interface MilestoneDraft {
  readonly name: string;
  readonly dueDate: CalendarDate;
  readonly description: string | null;
}

/** 單一項目的查詢（queryKeys.item）共同的開頭。 */
const ITEM_QUERIES = ['item'] as const;

function planningPath(projectKey: string, kind: 'cycles' | 'milestones'): string {
  return `/v1/projects/${encodeURIComponent(projectKey)}/${kind}`;
}

/**
 * 錯誤代碼：404 project.not_found、cycle.not_found、milestone.not_found；
 * 409 cycle.name_taken、cycle.overlaps、cycle.limit_reached、milestone.name_taken、milestone.limit_reached；
 * 422 planning.invalid（欄位的問題在 details，path 是 name、startDate、endDate、dueDate、description）。
 */
export function listCycles(projectKey: string, signal?: AbortSignal): Promise<readonly Cycle[]> {
  return apiRequest<readonly Cycle[]>(planningPath(projectKey, 'cycles'), { signal });
}

export function createCycle(projectKey: string, draft: CycleDraft): Promise<Cycle> {
  return apiRequest<Cycle>(planningPath(projectKey, 'cycles'), { method: 'POST', body: draft });
}

export function replaceCycle(projectKey: string, id: string, draft: CycleDraft): Promise<Cycle> {
  return apiRequest<Cycle>(`${planningPath(projectKey, 'cycles')}/${encodeURIComponent(id)}`, { method: 'PUT', body: draft });
}

export function deleteCycle(projectKey: string, id: string): Promise<unknown> {
  return apiRequest<unknown>(`${planningPath(projectKey, 'cycles')}/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export function listMilestones(projectKey: string, signal?: AbortSignal): Promise<readonly Milestone[]> {
  return apiRequest<readonly Milestone[]>(planningPath(projectKey, 'milestones'), { signal });
}

export function createMilestone(projectKey: string, draft: MilestoneDraft): Promise<Milestone> {
  return apiRequest<Milestone>(planningPath(projectKey, 'milestones'), { method: 'POST', body: draft });
}

export function replaceMilestone(projectKey: string, id: string, draft: MilestoneDraft): Promise<Milestone> {
  return apiRequest<Milestone>(`${planningPath(projectKey, 'milestones')}/${encodeURIComponent(id)}`, { method: 'PUT', body: draft });
}

export function deleteMilestone(projectKey: string, id: string): Promise<unknown> {
  return apiRequest<unknown>(`${planningPath(projectKey, 'milestones')}/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

/** 專案的週期，依開始日排序。 */
export function useCycles(projectKey: string) {
  return useQuery({
    queryKey: queryKeys.cycles(projectKey),
    queryFn: ({ signal }) => listCycles(projectKey, signal),
  });
}

/** 專案的里程碑，依到期日排序。 */
export function useMilestones(projectKey: string) {
  return useQuery({
    queryKey: queryKeys.milestones(projectKey),
    queryFn: ({ signal }) => listMilestones(projectKey, signal),
  });
}

/**
 * 週期與里程碑的新增、整個換掉與刪除。結束後（成功或失敗）都重抓：進度由伺服器算；失敗（404、409）代表別處已經改過，清單是舊的。
 * 新增與改名只動清單：項目的歸屬沒變，不必重抓項目。刪除時項目會變成沒有週期（里程碑），
 * 所以專案的項目清單與所有單一項目的查詢（開著的項目頁、面板）也失效，才不會指著已刪除的編號。
 */
export function usePlanningMutations(projectKey: string) {
  const queryClient = useQueryClient();
  const refreshList = (listKey: readonly unknown[]) => async () => {
    await queryClient.invalidateQueries({ queryKey: listKey });
  };
  const refreshAfterDelete = (listKey: readonly unknown[]) => async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: listKey }),
      queryClient.invalidateQueries({ queryKey: queryKeys.items(projectKey) }),
      queryClient.invalidateQueries({ queryKey: ITEM_QUERIES }),
    ]);
  };
  const cycleKey = queryKeys.cycles(projectKey);
  const milestoneKey = queryKeys.milestones(projectKey);
  return {
    createCycle: useMutation({ mutationFn: (draft: CycleDraft) => createCycle(projectKey, draft), onSettled: refreshList(cycleKey) }),
    replaceCycle: useMutation({
      mutationFn: ({ id, draft }: { readonly id: string; readonly draft: CycleDraft }) => replaceCycle(projectKey, id, draft),
      onSettled: refreshList(cycleKey),
    }),
    deleteCycle: useMutation({ mutationFn: (id: string) => deleteCycle(projectKey, id), onSettled: refreshAfterDelete(cycleKey) }),
    createMilestone: useMutation({
      mutationFn: (draft: MilestoneDraft) => createMilestone(projectKey, draft),
      onSettled: refreshList(milestoneKey),
    }),
    replaceMilestone: useMutation({
      mutationFn: ({ id, draft }: { readonly id: string; readonly draft: MilestoneDraft }) => replaceMilestone(projectKey, id, draft),
      onSettled: refreshList(milestoneKey),
    }),
    deleteMilestone: useMutation({
      mutationFn: (id: string) => deleteMilestone(projectKey, id),
      onSettled: refreshAfterDelete(milestoneKey),
    }),
  };
}

export type PlanningMutations = ReturnType<typeof usePlanningMutations>;
