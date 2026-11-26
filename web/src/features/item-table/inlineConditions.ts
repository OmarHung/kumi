import { useRef, type RefObject } from 'react';
import { remInPixels, useElementWidth } from '../../lib/useElementWidth';
import { filterableFields } from '../../domain/fieldFilters';
import { usePlanningVisibility } from './PlanningFilterFields';
import { useProjectTable } from './ProjectTableContext';

/**
 * 寬螢幕的工具列上，每個欄位（搜尋、類型、狀態、負責人、到期、優先度）最窄是這麼寬，欄位之間隔這麼遠：
 * Select 在工具列上的下限是 8rem（Select.module.css 的 --select-content-min），搜尋框從 8rem 開始排（ItemToolbar.module.css）。
 */
const FIELD_MIN_REM = 8;
const FIELD_GAP_REM = 0.5;

/**
 * 「清除條件」的位置一律預留（實測 78px），不管它現在有沒有出現：有條件時才出現的按鈕不算的話，
 * 套用第一個條件時它擠不下、整列折成兩列，下面的內容整個往下推（實測 1440 寬的 AGY 從 165 跳到 190）。
 * 預留之後，判斷不隨有沒有條件改變，它出現時一定排得下。
 */
const CLEAR_BUTTON_REM = 5;

/**
 * 「更多條件」按鈕（實測帶數字時約 102px，約 6.4rem，加上和前一個欄位的間距 0.5rem 取 7rem）：
 * 有任何條件收起來、或專案有週期與里程碑（它們一律在這裡面）時它在列上，要算進去；兩者都沒有時它不出現，不算。
 */
const MORE_BUTTON_REM = 7;

/** 列上可以收起來的條件（搜尋與看板的流程不收）。建立者排在負責人後面，是專案頁的條件，「我的工作」沒有。 */
export type CollapsibleCondition = 'type' | 'state' | 'assignee' | 'createdBy' | 'due' | 'priority';

/** 一律在「更多條件」裡、不在列上的條件：週期、里程碑，以及專案的自訂欄位（每個欄位一個下拉，數量不定，擠不進列）。 */
export type MoreOnlyCondition = 'cycle' | 'milestone' | 'fields';

/**
 * 放不下時收起來的先後：比較少用的先收。建立者最少用，最先收；接著優先度、到期，之後是類型、狀態、負責人；搜尋一律留在列上。
 * 整組一起收的話，週期與里程碑一直占著「更多條件」的 SPR 在 1440 寬、篩選中（多出清除條件）還是差約 40px 排不下
 * （contentStart 190，預算 176）；依序收，放得下的欄位就留在列上，不必為了擠進去多點一下。
 */
export const COLLAPSE_ORDER: readonly CollapsibleCondition[] = ['createdBy', 'priority', 'due', 'type', 'state', 'assignee'];

/**
 * 需要的寬度（rem）：每個欄位加一個間距，再加預留的「清除條件」，有「更多條件」時再加它的 7rem。
 * fieldCount 是留在列上的欄位數，含搜尋（與看板的流程），不含週期與里程碑。
 * 例：搜尋加五個條件（多種類型的表格，不含建立者）全部留在列上：6 × 8rem + 6 × 0.5rem + 5rem = 56rem；
 * 收起優先度之後：5 × 8.5 + 5 + 7 = 54.5rem。建立者是第六個條件，全部留在列上要再加 8.5rem，也最先收。
 * 實測（2026-11-18，演練環境、側欄展開）欄位那一組（flex: 1，寬度是工具列扣掉分頁與「檢視」後剩下的）：
 * 1280 寬約 683px（42.7rem）、1200 寬 603px、1024 寬 427px。依序收之後的 contentStart 見 docs/DESIGN.md 第 14 節
 * 的「PM-6」（1440、1024 寬的表格 165、看板 164）。
 */
export function inlineBarMinRem(fieldCount: number, hasMore: boolean): number {
  return fieldCount * (FIELD_MIN_REM + FIELD_GAP_REM) + CLEAR_BUTTON_REM + (hasMore ? MORE_BUTTON_REM : 0);
}

export interface InlineLayout {
  /** 這個版面有的、可以收的條件（順序不拘；收的先後看 COLLAPSE_ORDER）。 */
  readonly present: readonly CollapsibleCondition[];
  /** 一律在列上的欄位數：搜尋，看板再加流程。 */
  readonly fixedCount: number;
  /** 專案有週期、里程碑或能篩的自訂欄位：「更多條件」一直在列上。 */
  readonly hasPlanning: boolean;
}

/**
 * 寬度（rem）下，留在列上的條件：從最多的開始試，放不下就依 COLLAPSE_ORDER 收一個，直到放得下或只剩搜尋。
 * 量不到寬度（null）當成放得下。
 */
export function inlineConditionKeys(widthRem: number | null, layout: InlineLayout): ReadonlySet<CollapsibleCondition> {
  const { present, fixedCount, hasPlanning } = layout;
  const collapseOrder = COLLAPSE_ORDER.filter((key) => present.includes(key));
  const inline = new Set(present);
  if (widthRem === null) return inline;
  for (const key of collapseOrder) {
    const hasMore = hasPlanning || inline.size < present.length;
    if (inlineBarMinRem(fixedCount + inline.size, hasMore) <= widthRem) break;
    inline.delete(key);
  }
  return inline;
}

export interface GroupPlacement {
  readonly inline: ReadonlySet<CollapsibleCondition>;
  /** 看板的「分組」留在列上；放不下時收進「更多條件」。 */
  readonly isGroupInline: boolean;
}

/**
 * 看板多了「分組」：它不是篩選，但比篩選常換，所以篩選先收，全部收完還放不下才把它也收進「更多條件」。
 * 只算一個固定的欄位不夠：1024 寬、側欄展開時欄位那一組約 26.7rem，搜尋、流程、分組三個固定的就要 25.5rem，
 * 加上預留的「清除條件」與「更多條件」一定折成兩列（實測接案專案的看板 contentStart 從 164 變成 189）。
 * layout.fixedCount 不含分組。量不到寬度時當成放得下。
 */
export function groupPlacement(widthRem: number | null, layout: InlineLayout): GroupPlacement {
  const inline = inlineConditionKeys(widthRem, { ...layout, fixedCount: layout.fixedCount + 1 });
  if (widthRem === null || inline.size > 0) return { inline, isGroupInline: true };
  return { inline, isGroupInline: inlineBarMinRem(layout.fixedCount + 1, true) <= widthRem };
}

export interface InlineConditions {
  readonly barRef: RefObject<HTMLDivElement | null>;
  /** 留在列上的條件。 */
  readonly inlineKeys: ReadonlySet<CollapsibleCondition>;
  /** 收進「更多條件」的條件，照原本列上的順序，後面接週期與里程碑。 */
  readonly moreKeys: ReadonlySet<CollapsibleCondition | MoreOnlyCondition>;
  /** 列上有沒有「更多條件」：有條件收起來、或專案有週期、里程碑與能篩的自訂欄位。 */
  readonly hasMore: boolean;
  /** 看板的「分組」留在列上（沒有分組的版面一律是 true）。 */
  readonly isGroupInline: boolean;
}

/**
 * 依列實際的寬度（側欄收合、面板開著、視窗寬窄都會變）決定哪些條件收進「更多條件」，不是依視窗寬度的斷點。
 * 量不到寬度（jsdom）當成放得下。收起來的條件是比較少用的：比起搜尋框被壓窄或欄位排成兩列
 * （docs/DESIGN.md 第 14 節的 contentStart 預算），多點一下的代價比較小。
 * 週期、里程碑與自訂欄位是後來加的條件，不能擠掉常用的條件：寬螢幕上一律在「更多條件」裡，常用的組合可以存成檢視補回來。
 * 專案沒有這些、而且沒有條件被收起來時，「更多條件」不出現也不預留位置。
 */
export function useInlineConditions(
  layout: Omit<InlineLayout, 'hasPlanning'> & { readonly hasGroup?: boolean },
  filters: { readonly cycle: string | null; readonly milestone: string | null },
): InlineConditions {
  const barRef = useRef<HTMLDivElement>(null);
  const width = useElementWidth(barRef);
  const { hasCycles, hasMilestones } = usePlanningVisibility(filters.cycle, filters.milestone);
  const { detail } = useProjectTable();
  // 自訂欄位和週期、里程碑一樣一律在「更多條件」裡：欄位數不定，不能擠掉常用的條件。
  const hasFields = filterableFields(detail.blueprint).length > 0;
  const hasPlanning = hasCycles || hasMilestones || hasFields;
  const widthRem = width === null ? null : width / remInPixels();
  const { hasGroup = false, ...barLayout } = layout;
  const placement = hasGroup
    ? groupPlacement(widthRem, { ...barLayout, hasPlanning })
    : { inline: inlineConditionKeys(widthRem, { ...barLayout, hasPlanning }), isGroupInline: true };
  const inlineKeys = placement.inline;
  const collapsed = COLLAPSE_ORDER.filter((key) => layout.present.includes(key) && !inlineKeys.has(key));
  const moreKeys = new Set<CollapsibleCondition | MoreOnlyCondition>([...collapsed, 'cycle', 'milestone', 'fields']);
  const hasMore = hasPlanning || collapsed.length > 0 || !placement.isGroupInline;
  return { barRef, inlineKeys, moreKeys, hasMore, isGroupInline: placement.isGroupInline };
}
