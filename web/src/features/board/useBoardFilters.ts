import { useId, useRef, type RefObject } from 'react';
import { useSearchParams } from 'react-router';
import type { Workflow } from '../../api/blueprint';
import type { ProjectDetail } from '../../api/types';
import {
  dateViewFiltersFromSearchParams,
  boardFiltersToSearchParams,
  DATE_VIEW_PARAMS,
  viewFromSearchParams,
  type DateViewFilters,
} from '../../domain/projectView';
import { knownBoardGroup } from '../../domain/boardGroup';
import { knownBoardFields } from '../../domain/knownFilters';
import { boardWorkflows } from '../../domain/workflow';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { keepPanelItem } from '../work-items/itemLinks';

/**
 * 看板（行事曆、時間軸也用同一套）的條件：頁面的工具列（流程、搜尋、負責人）與看板本身（欄）都要用，所以放在兩者的上一層（ProjectWorkspace）。
 * 這個檔案不匯入拖曳的套件：工具列不是 lazy 載入的，看板的其他部分才是。
 */
export interface BoardFilterState {
  /** 看板只用 BoardFilters 那一部分；類型與狀態只有行事曆與時間軸有（看板的網址上讀不到，一律 null）。 */
  readonly filters: DateViewFilters;
  readonly workflows: readonly Workflow[];
  /** 目前看的流程：網址上的，不認得或沒給時是預設的（第一個類型用的流程）；專案沒有流程時是 undefined。 */
  readonly workflow: Workflow | undefined;
  readonly setFilters: (next: DateViewFilters) => void;
  /** 清掉類型、狀態、搜尋、負責人、到期、優先度、週期、里程碑與自訂欄位的條件（流程留著），焦點放到搜尋框；窄螢幕搜尋框收在浮層裡，放到「篩選」按鈕。 */
  readonly clearFilters: () => void;
  /** 寬螢幕工具列上的搜尋框。 */
  readonly searchRef: RefObject<HTMLInputElement | null>;
  /** 窄螢幕的「篩選」按鈕的 id。 */
  readonly filterButtonId: string;
}

export function useBoardFilters(detail: ProjectDetail): BoardFilterState {
  const [searchParams, setSearchParams] = useSearchParams();
  // 網址可能是舊的（欄位被刪、型別被改）：去掉藍圖裡不在的欄位條件，才不會整張清單被 422 擋掉。
  const withKnownFields = knownBoardFields(dateViewFiltersFromSearchParams(searchParams), detail.blueprint);
  // 分組的欄位被刪了或不是單選了也一樣：回到依狀態。
  const filters = { ...withKnownFields, group: knownBoardGroup(withKnownFields.group, detail.blueprint) };
  const workflows = boardWorkflows(detail.blueprint);
  const workflow = workflows.find((candidate) => candidate.key === filters.workflow) ?? workflows[0];
  const searchRef = useRef<HTMLInputElement>(null);
  const filterButtonId = useId();

  // 換條件不留歷史紀錄，和表格一樣：上一頁應該回到上一個畫面，不是上一個篩選。開著的項目面板留著。
  function setFilters(next: DateViewFilters) {
    const view = viewFromSearchParams(searchParams);
    const written = boardFiltersToSearchParams(next, workflows[0]?.key, view === 'table' ? 'board' : view);
    // 行事曆的月份、時間軸的刻度與起點留著（domain/projectView.ts 的 DATE_VIEW_PARAMS）。
    DATE_VIEW_PARAMS.forEach((name) => {
      const value = searchParams.get(name);
      if (view !== 'board' && view !== 'table' && value !== null) written.set(name, value);
    });
    setSearchParams(keepPanelItem(written, searchParams), { replace: true });
  }

  // 「清除條件」按了就跟著不見：焦點不接手會掉回頁面本體（lib/updateThenFocus.ts）。
  function clearFilters() {
    updateThenFocus(() => {
      setFilters({ ...filters, type: null, state: null, assignee: null, createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, q: '' });
    }, [searchRef, filterButtonId]);
  }

  return { filters, workflows, workflow, setFilters, clearFilters, searchRef, filterButtonId };
}
