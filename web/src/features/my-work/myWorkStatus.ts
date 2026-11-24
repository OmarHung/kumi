import type { Item } from '../../api/types';
import { isTerminal } from '../../domain/dates';
import type { StateAnnouncement } from '../item-table/cells/stateAnnouncement';
import type { MyWorkRow } from './useMyWorkRows';

const LEAVES_LIST = '重新整理後會離開清單。';

function timeOf(item: Item): number {
  return Date.parse(item.updatedAt);
}

/** 項目改成完成、取消之後會不會離開清單：預設是會（條件是還沒結束的）；條件是「所有狀態」這類它還符合的就不會。 */
export type IsLeaving = (item: Item) => boolean;

const ENDED_LEAVES: IsLeaving = (item) => isTerminal(item.stateCategory);

/** 最近一個改成完成或取消、而且真的會離開清單的（看伺服器給的更新時間）。 */
function latestSettled(rows: readonly MyWorkRow[], isLeaving: IsLeaving): Item | undefined {
  return rows
    .map((row) => row.item)
    .filter(isLeaving)
    .reduce<Item | undefined>((found, item) => (found === undefined || timeOf(item) > timeOf(found) ? item : found), undefined);
}

function settledText(item: Item): string {
  const outcome = item.stateCategory === 'done' ? '完成' : '取消';
  return `${item.key} 已經${outcome}，${LEAVES_LIST}`;
}

/**
 * 補填的那一句是不是最新的事：那一列之後沒有再改過（例如改回進行中），也沒有更晚改成完成、取消的別的項目。
 * 列還沒換上移好的那一份（快取晚一步）時，列的時間比較早，照樣算最新。
 */
function isLatest(fill: StateAnnouncement, row: MyWorkRow | undefined, settled: Item | undefined): boolean {
  const at = timeOf(fill.item);
  if (row !== undefined && timeOf(row.item) > at) return false;
  return settled === undefined || timeOf(settled) <= at;
}

/**
 * 工具列上一直掛著的 status（docs/DESIGN.md 8.3「我的工作」）：最近一個改成完成或取消的「AGY-3 已經完成，重新整理後會離開清單。」。
 * 狀態格補填完成時把它的那一句交過來（StateAnnouncementContext），是最新的事就說它；補填之後就移到完成或取消的，
 * 合成一句「已填好「到期日」，AGY-4 已移到「結案」，重新整理後會離開清單。」——不是格子一句、工具列又一句。
 * 只有真的不再符合條件的才說會離開清單（isLeaving，見 MyWorkPage）。
 * 補填的項目不在清單上（面板裡換到了別的項目）時只說補填的結果。
 */
export function myWorkStatus(rows: readonly MyWorkRow[], fill: StateAnnouncement | null, isLeaving: IsLeaving = ENDED_LEAVES): string {
  const settled = latestSettled(rows, isLeaving);
  const row = fill === null ? undefined : rows.find((candidate) => candidate.key === fill.item.key);
  if (fill !== null && isLatest(fill, row, settled)) {
    const leaves = row !== undefined && isLeaving(fill.item);
    return leaves ? `${fill.message.replace(/。$/u, '')}，${LEAVES_LIST}` : fill.message;
  }
  return settled === undefined ? '' : settledText(settled);
}
