import type { Cycle, Milestone } from '../api/planning';
import type { CalendarDate, Item, ItemQuery } from '../api/types';

/** 項目篩選的「週期」：`current`（目前的週期）、`none`（沒有週期）或週期的編號。 */
export const CURRENT_CYCLE = 'current';
export const NO_PLANNING = 'none';

export const DELETED_CYCLE_TEXT = '已刪除的週期';
export const DELETED_MILESTONE_TEXT = '已刪除的里程碑';

/** 清單讀不到時的說法：不知道它還在不在，不能說成「已刪除」。 */
export const UNKNOWN_CYCLE_TEXT = '某個週期';
export const UNKNOWN_MILESTONE_TEXT = '某個里程碑';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 網址可能是手打的：不是編號的略過，不要讓後端回 query.invalid 把整張表擋掉。
function parseIdOr(value: string | null, keywords: readonly string[]): string | null {
  // 伺服器回的編號是小寫：網址裡大寫的編號轉成小寫，才比對得上清單與項目上的編號。
  const text = value?.trim().toLowerCase() ?? '';
  return keywords.includes(text) || GUID.test(text) ? text : null;
}

export function parseCycleFilter(value: string | null): string | null {
  return parseIdOr(value, [CURRENT_CYCLE, NO_PLANNING]);
}

export function parseMilestoneFilter(value: string | null): string | null {
  return parseIdOr(value, [NO_PLANNING]);
}

/** 開始日與結束日當天都算；週期不重疊，最多一個。 */
export function currentCycle(cycles: readonly Cycle[], todayDate: CalendarDate): Cycle | undefined {
  return cycles.find((cycle) => cycle.startDate <= todayDate && todayDate <= cycle.endDate);
}

type CycleQuery = Pick<ItemQuery, 'cycleId' | 'noCycle'>;
type MilestoneQuery = Pick<ItemQuery, 'milestoneId' | 'noMilestone'>;

/** 篩了「目前的週期」、而今天不在任何一個週期裡：結果一定是空的，呼叫端不送請求（不送假編號），直接顯示空白。 */
export function hasNoCurrentCycle(cycle: string | null, cycles: readonly Cycle[], todayDate: CalendarDate): boolean {
  return cycle === CURRENT_CYCLE && currentCycle(cycles, todayDate) === undefined;
}

/**
 * 換成 API 的參數：伺服器不知道使用者的時區，「目前的週期」在這裡用今天換成編號。
 * 沒有目前的週期時是空的條件：呼叫端要先用 hasNoCurrentCycle 判斷，不然會變成「全部的項目」。
 */
export function cycleQuery(cycle: string | null, cycles: readonly Cycle[], todayDate: CalendarDate): CycleQuery {
  if (cycle === null) return {};
  if (cycle === NO_PLANNING) return { noCycle: true };
  if (cycle === CURRENT_CYCLE) {
    const current = currentCycle(cycles, todayDate);
    return current === undefined ? {} : { cycleId: current.id };
  }
  return { cycleId: cycle };
}

export function milestoneQuery(milestone: string | null): MilestoneQuery {
  if (milestone === null) return {};
  return milestone === NO_PLANNING ? { noMilestone: true } : { milestoneId: milestone };
}

/**
 * 新增時帶的週期：篩了某個週期（含目前的）就帶那一個；沒有週期、沒有目前的週期不帶。
 * 編號要在已載入的清單裡：網址指著已刪除的（或清單讀不到），帶了只會被後端退回。
 */
export function cycleDefault(cycle: string | null, cycles: readonly Cycle[], todayDate: CalendarDate): string | undefined {
  const { cycleId } = cycleQuery(cycle, cycles, todayDate);
  return cycleId !== undefined && cycles.some((candidate) => candidate.id === cycleId) ? cycleId : undefined;
}

export function milestoneDefault(milestone: string | null, milestones: readonly Milestone[]): string | undefined {
  const { milestoneId } = milestoneQuery(milestone);
  return milestoneId !== undefined && milestones.some((candidate) => candidate.id === milestoneId) ? milestoneId : undefined;
}

/** 項目符不符合週期的條件，和 cycleQuery 送給後端的是同一個意思（只用在還沒進伺服器結果的卡片）。 */
export function matchesCycle(item: Pick<Item, 'cycleId'>, cycle: string | null, cycles: readonly Cycle[], todayDate: CalendarDate): boolean {
  if (hasNoCurrentCycle(cycle, cycles, todayDate)) return false;
  const { cycleId, noCycle } = cycleQuery(cycle, cycles, todayDate);
  if (noCycle === true) return item.cycleId === null;
  return cycleId === undefined || item.cycleId === cycleId;
}

export function matchesMilestone(item: Pick<Item, 'milestoneId'>, milestone: string | null): boolean {
  const { milestoneId, noMilestone } = milestoneQuery(milestone);
  if (noMilestone === true) return item.milestoneId === null;
  return milestoneId === undefined || item.milestoneId === milestoneId;
}

/** 目前的名稱（週期改名之後用新的）；查不到是刪掉的；清單讀不到（null）時不知道，用中性的說法。 */
export function displayCycleName(cycleId: string, cycles: readonly Cycle[] | null): string {
  if (cycles === null) return UNKNOWN_CYCLE_TEXT;
  return cycles.find((cycle) => cycle.id === cycleId)?.name ?? DELETED_CYCLE_TEXT;
}

export function displayMilestoneName(milestoneId: string, milestones: readonly Milestone[] | null): string {
  if (milestones === null) return UNKNOWN_MILESTONE_TEXT;
  return milestones.find((milestone) => milestone.id === milestoneId)?.name ?? DELETED_MILESTONE_TEXT;
}
