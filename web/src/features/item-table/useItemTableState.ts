import { useId, useMemo, useRef, type RefObject } from 'react';
import { useSearchParams } from 'react-router';
import type { ProjectDetail } from '../../api/types';
import { createColumnWidthStore, type ColumnWidthStore } from '../../components/Table/columnWidths';
import {
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  type ItemFilters,
} from '../../domain/itemFilters';
import { knownItemFilters } from '../../domain/knownFilters';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { useBulkEdit, type BulkEdit } from '../bulk-edit/useBulkEdit';
import { useItemSelection, type ItemSelection } from '../bulk-edit/useItemSelection';
import { keepPanelItem } from '../work-items/itemLinks';
import { abilitiesOf } from './ProjectTableContext';
import { buildColumns, type ItemColumn } from './columns';
import { useColumnVisibility, type ColumnVisibility } from './columnVisibility';
import { widthStorageKey } from './itemColumnWidths';
import { usePlanning } from './usePlanning';
import { useStaleAnchor } from './useStaleAnchor';
import { useQuickAddState, type QuickAddState } from './useQuickAdd';

/**
 * 表格檢視的狀態：工具列（篩選、顯示的欄位、窄螢幕的「新增項目」）與內容（快速新增、表格）都要用，
 * 所以放在兩者的上一層（ProjectWorkspace），也不隨寬窄換掉。
 */
export interface ItemTableState {
  readonly filters: ItemFilters;
  readonly setFilters: (next: ItemFilters) => void;
  /** 清掉篩選（排序留著），焦點放到搜尋框；窄螢幕搜尋框收在浮層裡，放到「篩選」按鈕。 */
  readonly clearFilters: () => void;
  readonly columns: readonly ItemColumn[];
  readonly visibility: ColumnVisibility;
  readonly setVisibility: (visibility: ColumnVisibility) => void;
  /** 使用者拉的欄寬（docs/DESIGN.md 7.1「欄寬」）：表格調、「顯示的欄位」裡重設。 */
  readonly widths: ColumnWidthStore;
  readonly quickAdd: QuickAddState;
  /** 「停滯」的分界用的「現在」：條件換了才重取，清單與快速新增共用（useStaleAnchor）。 */
  readonly staleNow: Date;
  /** 寬螢幕工具列上的搜尋框。 */
  readonly searchRef: RefObject<HTMLInputElement | null>;
  /** 窄螢幕的「篩選」按鈕的 id。 */
  readonly filterButtonId: string;
  /** 表格的多選：工具列換成批次動作列、表格的勾選框都用同一份。 */
  readonly selection: ItemSelection;
  /** 批次操作與它的結果提示（表格上方）。 */
  readonly bulk: BulkEdit;
}

/** 篩選與排序放在網址的查詢字串裡；顯示哪些欄、欄寬記在這個瀏覽器（每個專案一份）。 */
export function useItemTableState(detail: ProjectDetail): ItemTableState {
  const [searchParams, setSearchParams] = useSearchParams();
  // 網址可能是舊的（欄位被刪、型別被改）：去掉藍圖裡不在的欄位條件與排序，才不會整張表被 422 擋掉。
  const filters = useMemo(() => knownItemFilters(filtersFromSearchParams(searchParams), detail.blueprint), [searchParams, detail.blueprint]);
  const [visibility, setVisibility] = useColumnVisibility(detail.project.key);
  const widths = useMemo(() => createColumnWidthStore(widthStorageKey(detail.project.key)), [detail.project.key]);
  const { cycles, milestones } = usePlanning(detail.project.key);
  const hasCycles = cycles.length > 0;
  const hasMilestones = milestones.length > 0;
  // 有動作可做的人才有勾選框欄（改狀態、改欄位、刪除其中一種）。
  const abilities = abilitiesOf(detail);
  const canSelect = abilities.update || abilities.transition || abilities.delete;
  const columns = useMemo(
    () => buildColumns(detail.blueprint, { hasCycles, hasMilestones }, canSelect),
    [detail.blueprint, hasCycles, hasMilestones, canSelect],
  );
  // 快速新增的類型跟著篩選的類型（新增沿用當下的條件）。
  const staleNow = useStaleAnchor(filters);
  const quickAdd = useQuickAddState(detail, filters, staleNow);
  const searchRef = useRef<HTMLInputElement>(null);
  const filterButtonId = useId();
  // 換專案、條件、排序就清空選取：選的是「這一份清單裡的這幾個」。
  const selection = useItemSelection(`${detail.project.key}?${filtersToSearchParams(filters).toString()}`);
  const bulk = useBulkEdit(detail.project.key, selection);

  // 換條件不留歷史紀錄：上一頁應該回到上一個畫面，不是上一個篩選。
  function setFilters(next: ItemFilters) {
    // 開著的項目面板（item）留著：換條件不該把正在看的項目關掉。
    setSearchParams(keepPanelItem(filtersToSearchParams(next), searchParams), { replace: true });
  }

  // 「清除條件」按了就跟著不見：焦點不接手會掉回頁面本體（lib/updateThenFocus.ts）。
  function clearFilters() {
    updateThenFocus(() => {
      setFilters({ ...EMPTY_FILTERS, sort: filters.sort });
    }, [searchRef, filterButtonId]);
  }

  return { filters, setFilters, clearFilters, columns, visibility, setVisibility, widths, quickAdd, staleNow, searchRef, filterButtonId, selection, bulk };
}
