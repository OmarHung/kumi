import type { Activity } from '../../api/types';
import { groupAttachmentRuns } from '../../domain/activity';

/** 時間軸上的一筆最上層活動，和收在它底下的回覆（依時間）。 */
export interface Thread {
  readonly activity: Activity;
  /** 畫成這一筆的活動：連續附加的檔案併成一行時有好幾筆（第一筆就是 activity），其他只有 activity 自己。 */
  readonly group: readonly Activity[];
  readonly replies: readonly Activity[];
  /** 這一筆是回覆、但它回覆的那一則不在已載入的活動裡（不應該發生）：當成一般留言放著，前面註明。 */
  readonly isDetached: boolean;
}

/**
 * 把照時間排好的活動分成「串」：回覆只有一層，歸到它的 replyToId 指的最上層活動底下。
 * 載入更多時新的一頁裡的回覆，照樣歸到已經載入的那一則底下（整份活動一起算，不分頁）。
 */
export function buildThreads(entries: readonly Activity[]): readonly Thread[] {
  const topIds = new Set(entries.filter((entry) => entry.replyToId === null).map((entry) => entry.id));
  const repliesOf = new Map<string, Activity[]>();
  entries.forEach((entry) => {
    if (entry.replyToId === null || !topIds.has(entry.replyToId)) return;
    repliesOf.set(entry.replyToId, [...(repliesOf.get(entry.replyToId) ?? []), entry]);
  });
  const tops = entries.filter((entry) => entry.replyToId === null || !topIds.has(entry.replyToId));
  // 併行只在畫的時候做：資料與 API 照舊一筆一筆。
  return groupAttachmentRuns(tops).flatMap((group) => {
    const [activity] = group;
    return activity === undefined ? [] : [{ activity, group, replies: repliesOf.get(activity.id) ?? [], isDetached: activity.replyToId !== null }];
  });
}
