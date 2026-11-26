import type { ProjectOverview } from '../../api/overview';

export interface OverviewListProps {
  readonly projects: readonly ProjectOverview[];
  /** 現在（每分鐘更新，useNow）：「幾分鐘前」以它為準，同一頁每一列的「現在」一樣。 */
  readonly now: number;
}
