/**
 * 專案總覽的門檻。數字要和後端的 `ProjectOverviewRules`（IProjectOverviewService.cs）一樣：
 * 總覽的數字與點下去的清單筆數靠這幾個數字對得上，改的時候兩邊一起改。
 */

/** 「快到期」：今天起算幾天（含今天），即今天到 DUE_SOON_DAYS − 1 天後。對應 `ProjectOverviewRules.DueSoonDays`。 */
export const DUE_SOON_DAYS = 7;

/** 「停滯」：還沒結束、幾天沒有更新。對應 `ProjectOverviewRules.StaleDays`。 */
export const STALE_DAYS = 14;

/** 「最近完成」：幾天內。對應 `ProjectOverviewRules.DoneRecentlyDays`。 */
export const DONE_RECENTLY_DAYS = 7;
