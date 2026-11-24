import { useCycles, useMilestones, type Cycle, type Milestone } from '../../api/planning';

const NO_CYCLES: readonly Cycle[] = [];
const NO_MILESTONES: readonly Milestone[] = [];

interface ListState {
  readonly data: unknown;
  readonly isPending: boolean;
  readonly errorUpdateCount: number;
}

/**
 * 第一次就失敗過、手上沒有資料。不能用 isLoadingError：TanStack Query 重抓一個還沒有資料的失敗查詢時，
 * 狀態會回到 pending（錯誤被清掉）——元件重新掛上就會重抓，用 isPending 擋住畫面的話，
 * 畫面一卸下又掛上，無限循環。errorUpdateCount 在重抓時不會歸零，用它分辨「還沒有結果」與「失敗了、正在重抓」。
 */
function isFailed(query: ListState): boolean {
  return query.data === undefined && query.errorUpdateCount > 0;
}

function isLoadingFirstTime(query: ListState): boolean {
  return query.isPending && query.errorUpdateCount === 0;
}

/**
 * 專案的週期與里程碑（篩選、欄位、活動的名稱都要用）。同一份查詢到處用，TanStack Query 只打一次。
 * 專案頁與項目的欄位區等它載入完才畫（isPending）：沒有週期（里程碑）的欄位與篩選是不畫的，載入後才出現會把版面推動。
 * 讀不到（isCyclesFailed、isMilestonesFailed）時清單是空的，但那是「不知道」，不是「沒有」：
 * 帶著週期（里程碑）條件的表格與看板說讀取失敗（PlanningLoadError），名稱用中性的說法；沒有條件就當成沒有這兩樣東西。
 * 重抓失敗但手上還有資料時不算讀不到。
 */
export function usePlanning(projectKey: string) {
  const cycles = useCycles(projectKey);
  const milestones = useMilestones(projectKey);
  const isCyclesPending = isLoadingFirstTime(cycles);
  const isMilestonesPending = isLoadingFirstTime(milestones);
  const isCyclesFailed = isFailed(cycles);
  const isMilestonesFailed = isFailed(milestones);
  return {
    cycles: cycles.data ?? NO_CYCLES,
    milestones: milestones.data ?? NO_MILESTONES,
    isPending: isCyclesPending || isMilestonesPending,
    isCyclesPending,
    isMilestonesPending,
    isCyclesFailed,
    isMilestonesFailed,
    /** 讀不到的原因（給錯誤的說明）。 */
    error: cycles.error ?? milestones.error,
    /** 只重抓讀不到的那一份。 */
    retry: () => {
      if (isCyclesFailed) void cycles.refetch();
      if (isMilestonesFailed) void milestones.refetch();
    },
  };
}

export type Planning = ReturnType<typeof usePlanning>;

/** 週期與里程碑的條件（篩選）要用的清單讀不到：說不出結果是什麼，不能當成「沒有」。 */
export function isPlanningFilterBlocked(planning: Planning, filters: { readonly cycle: string | null; readonly milestone: string | null }): boolean {
  return (filters.cycle !== null && planning.isCyclesFailed) || (filters.milestone !== null && planning.isMilestonesFailed);
}
