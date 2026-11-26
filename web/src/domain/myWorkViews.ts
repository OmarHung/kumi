import type { ItemTypeSummary, SavedViewLayout, SavedViewQuery } from '../api/types';
import type { MyWorkGrouping } from '../features/my-work/myWorkGroups';
import { effectiveMyWorkSort, myWorkSortValue, parseMyWorkSort, SORT_PARAM, type MyWorkSort } from '../features/my-work/myWorkSort';
import { DUE_FILTER_LABELS, PRIORITY_FILTER_LABELS } from './dueFilter';
import { STATE_CATEGORY_LABELS } from './labels';
import {
  myWorkFiltersFromParams,
  myWorkSearchParams,
  type MyWorkFilters,
  type MyWorkLayout,
  type MyWorkStatus,
} from './myWorkView';
import { LAYOUT_NAMES, SAVED_VIEW_NAME_MAX_LENGTH } from './savedViews';

/**
 * 「我的工作」的檢視（internal/HANDOFF-my-work-views.md 第 4.3、5.5 節）：目前畫面的版面、條件與分組，
 * 和伺服器存的 SavedViewQuery 互換。條件的意思和專案的檢視不同：沒有負責人的條件（一律是我）、狀態用類別。
 */
export interface MyWorkViewState {
  readonly layout: MyWorkLayout;
  readonly filters: MyWorkFilters;
  /** 只有表格看分組。 */
  readonly grouping: MyWorkGrouping;
  /** 只有表格看排序；null 是預設（到期日遞增）。 */
  readonly sort: MyWorkSort | null;
}

/** 存起來的檢視（或要存的草稿）裡和畫面有關的兩樣。 */
export interface StoredMyWorkView {
  readonly layout: SavedViewLayout;
  readonly query: SavedViewQuery;
}

const GROUP_BY_PROJECT = 'project';
const CATEGORY_ALL = 'all';

function orNull(value: string): string | null {
  return value === '' ? null : value;
}

function categoryOf(status: MyWorkStatus): string | null {
  switch (status.kind) {
    case 'all':
      return CATEGORY_ALL;
    case 'category':
      return status.category;
    default:
      return null;
  }
}

// 跨專案時各專案的狀態 key 意思不同：沒有選專案的狀態條件不存（網址讀回來時也會略過）。
function stateOf(filters: MyWorkFilters): string | null {
  return filters.status.kind === 'state' && filters.project !== null ? filters.status.state : null;
}

/**
 * 目前的條件存成檢視的條件。週期、里程碑、自訂欄位「我的工作」沒有，一律 null；分組與排序只有表格存
 * （排序是「欄位:方向」，和專案的檢視同一種寫法，多一個只有我的工作有的 project）。
 */
export function myWorkQueryOf({ layout, filters, grouping, sort }: MyWorkViewState): SavedViewQuery {
  const hasState = stateOf(filters) !== null;
  const isTable = layout === 'table';
  return {
    type: filters.type,
    state: stateOf(filters),
    assignee: null,
    createdBy: null,
    mine: filters.mine === 'created' ? 'created' : null,
    q: orNull(filters.q.trim()),
    sort: isTable ? sortValueOf(effectiveMyWorkSort(sort, grouping, filters.mine)) : null,
    due: filters.due,
    priority: filters.priority,
    cycle: null,
    milestone: null,
    fields: null,
    category: hasState ? null : categoryOf(filters.status),
    project: filters.project,
    group: isTable && grouping === 'project' ? GROUP_BY_PROJECT : null,
  };
}

function sortValueOf(sort: MyWorkSort | null): string | null {
  return sort === null ? null : myWorkSortValue(sort);
}

function paramsOfQuery(query: SavedViewQuery): URLSearchParams {
  const entries: [string, string][] = [];
  // 伺服器存的 JSON 可能是舊的、沒有後來新增的欄位（undefined）：和 null 一樣不寫，不然網址會冒出 createdBy=undefined。
  const add = (name: string, value: string | null | undefined) => {
    if (value != null) entries.push([name, value]);
  };
  add('q', query.q);
  add('mine', query.mine);
  add('type', query.type);
  add('category', query.category);
  add('state', query.state);
  add('due', query.due);
  add('priority', query.priority);
  add('project', query.project);
  return new URLSearchParams(entries);
}

/**
 * 套用檢視要導去的查詢字串：和網址上的條件同一套寫法（先讀成條件再寫回，不認得的值和手打時一樣略過），
 * 所以「目前是不是這個檢視」直接比這個字串。開著的面板不在裡面。
 */
export function myWorkViewParams(stored: StoredMyWorkView): URLSearchParams {
  const params = myWorkSearchParams(stored.layout, myWorkFiltersFromParams(paramsOfQuery(stored.query)));
  if (stored.layout !== 'table') return params;
  if (stored.query.group === GROUP_BY_PROJECT) params.append('group', GROUP_BY_PROJECT);
  // 預設（到期日遞增）、不認得的與依專案分組時依專案的排序不寫：和網址上沒有 sort 是同一件事。
  const grouping = stored.query.group === GROUP_BY_PROJECT ? 'project' : 'urgency';
  const sort = effectiveMyWorkSort(parseMyWorkSort(stored.query.sort), grouping, stored.query.mine === 'created' ? 'created' : 'assigned');
  if (sort !== null) params.append(SORT_PARAM, myWorkSortValue(sort));
  return params;
}

/** 目前的版面與條件和這個檢視完全一樣。 */
export function myWorkViewMatches(view: StoredMyWorkView, current: MyWorkViewState): boolean {
  const now = { layout: current.layout, query: myWorkQueryOf(current) };
  return view.layout === now.layout && myWorkViewParams(view).toString() === myWorkViewParams(now).toString();
}

/** 目前有沒有任何條件（和預設不同的條件，或依專案分組）：沒有時不給「更新成目前的條件」。 */
export function hasMyWorkViewConditions(current: MyWorkViewState): boolean {
  const params = myWorkViewParams({ layout: current.layout, query: myWorkQueryOf(current) });
  return [...params.keys()].some((name) => name !== 'view');
}

export interface MyWorkNameContext {
  readonly types: readonly ItemTypeSummary[];
  /** 選了專案時那個專案的狀態；null：讀不到，名稱用狀態的 key。 */
  readonly projectStates: readonly { readonly key: string; readonly name: string }[] | null;
}

function typeName(key: string, { types }: MyWorkNameContext): string {
  return types.find((type) => type.key === key)?.names.join('／') ?? key;
}

function statusName(filters: MyWorkFilters, { projectStates }: MyWorkNameContext): string | null {
  const { status } = filters;
  switch (status.kind) {
    case 'open':
      return null;
    case 'all':
      return '所有狀態';
    case 'category':
      return STATE_CATEGORY_LABELS[status.category];
    case 'state':
      return projectStates?.find((candidate) => candidate.key === status.state)?.name ?? status.state;
  }
}

/**
 * 另存時名稱的預設：目前條件的簡短說法，例如「CM・完成」「CM・等客戶」。專案寫代號（名稱可能很長）。
 * 沒有條件時是版面的名稱；看板、行事曆與時間軸有條件時前面加版面的名稱，和表格的分得開。
 */
export function defaultMyWorkViewName(current: MyWorkViewState, context: MyWorkNameContext): string {
  const { layout, filters, grouping } = current;
  const parts = [
    filters.mine === 'created' ? '我建立的' : null,
    filters.type === null ? null : typeName(filters.type, context),
    filters.project,
    statusName(filters, context),
    filters.due === null ? null : DUE_FILTER_LABELS[filters.due],
    filters.priority === null ? null : filters.priority === 'none' ? '優先度未設定' : PRIORITY_FILTER_LABELS[filters.priority],
    filters.q.trim() === '' ? null : `「${filters.q.trim()}」`,
    layout === 'table' && grouping === 'project' ? '依專案' : null,
  ].filter((part): part is string => part !== null);
  const named = layout !== 'table' || parts.length === 0 ? [LAYOUT_NAMES[layout], ...parts] : parts;
  return Array.from(named.join('・')).slice(0, SAVED_VIEW_NAME_MAX_LENGTH).join('');
}
