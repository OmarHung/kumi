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
    // 表頭寫一行的短名（「需要注意」底下）：兩行的表頭讓第一筆內容超過 6.2 節的高度預算；完整的天數在 title 與下面的說明。
    head: ['快到期'],
    description: `還沒結束、到期日在今天到 ${String(DUE_SOON_DAYS - 1)} 天後的項目`,
    valueOf: (p) => p.dueSoon,
    linkOf: overviewLinks.dueSoon,
  },
  {
    id: 'stale',
    label: '停滯',
    head: ['停滯'],
    description: `還沒結束、${String(STALE_DAYS)} 天沒有更新的項目`,
    valueOf: (p) => p.stale,
    linkOf: overviewLinks.stale,
  },
  { id: 'unassigned', label: '沒有負責人', head: ['未指派'], description: '還沒結束、沒有負責人的項目', valueOf: (p) => p.unassigned, linkOf: overviewLinks.unassigned },
  {
    id: 'doneRecently',
    label: `近 ${String(DONE_RECENTLY_DAYS)} 天完成`,
    head: [`近 ${String(DONE_RECENTLY_DAYS)} 天`, '完成'],
    description: `現在是完成、${String(DONE_RECENTLY_DAYS)} 天內有更新的項目（沒有記完成的時間，是近似值，所以不連到清單）`,
    valueOf: (p) => p.doneRecently,
    linkOf: null,
  },
];

function numberOf(id: string): OverviewNumber {
  const found = OVERVIEW_NUMBERS.find((number) => number.id === id);
  if (found === undefined) throw new Error(`沒有這個數字：${id}`);
  return found;
}

/** 表格的「需要注意」一組（兩層表頭的第一列跨這四欄）：照要緊的程度排，過期的在最前面。 */
export const ATTENTION_NUMBERS: readonly OverviewNumber[] = ['overdue', 'dueSoon', 'stale', 'unassigned'].map(numberOf);

/** 「還沒結束」不另佔一欄：寫在狀態橫條下面的註腳。 */
export const OPEN_NUMBER: OverviewNumber = numberOf('open');

export const DONE_RECENTLY_NUMBER: OverviewNumber = numberOf('doneRecently');

/** 「需要注意」的組名。 */
export const ATTENTION_LABEL = '需要注意';
