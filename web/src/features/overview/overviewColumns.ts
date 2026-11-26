import type { ProjectOverview } from '../../api/overview';
import { DONE_RECENTLY_DAYS, DUE_SOON_DAYS, STALE_DAYS } from '../../domain/overviewRules';
import { overviewLinks } from './overviewLinks';

/** 總覽的一個數字：表格的一欄、窄螢幕卡片的一格。連結是 null 的只顯示（最近完成是近似值，不連到篩選）。 */
export interface OverviewNumber {
  readonly id: string;
  readonly label: string;
  /**
   * 表頭的文字，一個元素一行：自己決定斷在哪裡（「7 天內／到期」）。
   * 讓瀏覽器換行時中文會斷在字中間（「7 天／內到／期」），不換行時十欄在 1280 寬加展開的側欄放不下。
   */
  readonly head: readonly string[];
  /** 滑過時與說明段落用的定義。 */
  readonly description: string;
  readonly valueOf: (project: ProjectOverview) => number;
  readonly linkOf: ((projectKey: string) => string) | null;
}

export const OVERVIEW_NUMBERS: readonly OverviewNumber[] = [
  { id: 'open', label: '還沒結束', head: ['還沒結束'], description: '還沒結束（待規劃、未開始、進行中）的項目', valueOf: (p) => p.open, linkOf: overviewLinks.open },
  { id: 'overdue', label: '已過期', head: ['已過期'], description: '還沒結束、到期日早於今天的項目', valueOf: (p) => p.overdue, linkOf: overviewLinks.overdue },
  {
    id: 'dueSoon',
    label: `${String(DUE_SOON_DAYS)} 天內到期`,
    head: [`${String(DUE_SOON_DAYS)} 天內`, '到期'],
    description: `還沒結束、到期日在今天到 ${String(DUE_SOON_DAYS - 1)} 天後的項目`,
    valueOf: (p) => p.dueSoon,
    linkOf: overviewLinks.dueSoon,
  },
  { id: 'unassigned', label: '沒有負責人', head: ['沒有', '負責人'], description: '還沒結束、沒有負責人的項目', valueOf: (p) => p.unassigned, linkOf: overviewLinks.unassigned },
  {
    id: 'stale',
    label: '停滯',
    head: ['停滯'],
    description: `還沒結束、${String(STALE_DAYS)} 天沒有更新的項目`,
    valueOf: (p) => p.stale,
    linkOf: overviewLinks.stale,
  },
  {
    id: 'doneRecently',
    label: `近 ${String(DONE_RECENTLY_DAYS)} 天完成`,
    head: [`近 ${String(DONE_RECENTLY_DAYS)} 天`, '完成'],
    description: `現在是完成、${String(DONE_RECENTLY_DAYS)} 天內有更新的項目（沒有記完成的時間，是近似值，所以不連到清單）`,
    valueOf: (p) => p.doneRecently,
    linkOf: null,
  },
];
