import type { CalendarDate, Instant } from './types';
import type { StateCategory } from './blueprint';
import type { ItemProgress } from './planning';
import { apiRequest } from './http';

/** 總覽的範圍：呼叫者管理的專案（預設）、或看得到的所有專案。 */
export type OverviewScope = 'managed' | 'visible';

/** 一個狀態的項目數；依藍圖裡流程與狀態的順序，數量是 0 的伺服器不列。 */
export interface OverviewStateCount {
  readonly key: string;
  readonly name: string;
  readonly category: StateCategory;
  readonly count: number;
}

export interface OverviewCycle {
  readonly id: string;
  readonly name: string;
  readonly startDate: CalendarDate;
  readonly endDate: CalendarDate;
  readonly progress: ItemProgress;
}

export interface OverviewMilestone {
  readonly id: string;
  readonly name: string;
  readonly dueDate: CalendarDate;
  readonly progress: ItemProgress;
}

/** 一個專案的總覽（GET /v1/overview/projects 的一筆）。數字的定義在後端的 IProjectOverviewService.cs。 */
export interface ProjectOverview {
  readonly projectKey: string;
  readonly name: string;
  /** 呼叫者在這個專案只看得到自己的項目：數字只算那些。 */
  readonly ownItemsOnly: boolean;
  readonly states: readonly OverviewStateCount[];
  readonly open: number;
  readonly overdue: number;
  readonly dueSoon: number;
  readonly unassigned: number;
  readonly stale: number;
  /** 近似值（沒有記完成的時間）：畫面只顯示，不連到篩選。 */
  readonly doneRecently: number;
  readonly currentCycle: OverviewCycle | null;
  readonly nextMilestone: OverviewMilestone | null;
  readonly lastUpdatedAt: Instant | null;
}

export function listProjectOverviews(scope: OverviewScope, signal?: AbortSignal): Promise<readonly ProjectOverview[]> {
  return apiRequest<readonly ProjectOverview[]>(`/v1/overview/projects?scope=${scope}`, { signal });
}
