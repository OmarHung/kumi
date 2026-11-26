import type { FieldDefinition, ProjectBlueprint } from '../api/blueprint';
import type { Cycle, Milestone } from '../api/planning';
import type { Member, Priority, SavedView, SavedViewLayout, SavedViewQuery } from '../api/types';
import { itemTypeName } from './blueprint';
import { DUE_FILTER_LABELS, PRIORITY_FILTER_LABELS, parseDueFilter, parsePriority } from './dueFilter';
import { CURRENT_CYCLE, displayCycleName, displayMilestoneName, NO_PLANNING } from './planningFilter';
import { filtersFromSearchParams, filtersToSearchParams, type ItemFilters } from './itemFilters';
import { boardFiltersToSearchParams, type BoardFilters, type DateViewTypeState } from './projectView';
import { boardGroupParam, knownBoardGroup, parseBoardGroup, STATE_GROUP } from './boardGroup';
import { FIELD_NONE, fieldFilterEntries, filterableFields, hasFieldFilters, knownFieldFilters, type FieldFilters } from './fieldFilters';
import { filterableStates } from './workflow';

/** 檢視的名稱最多這麼多字，和後端的驗證一樣。 */
export const SAVED_VIEW_NAME_MAX_LENGTH = 50;

/** 目前畫面上的版面與條件，和存起來的檢視比對、另存都用這個形狀。 */
export interface ViewState {
  readonly layout: SavedViewLayout;
  readonly query: SavedViewQuery;
}

/** 名稱的長度算「字」（一個中文字、一個表情符號都是一個），和後端的驗證一樣。 */
export function nameLength(name: string): number {
  return Array.from(name).length;
}

function orNull(value: string): string | null {
  return value === '' ? null : value;
}

function fieldsOrNull(fields: FieldFilters): FieldFilters | null {
  return hasFieldFilters(fields) ? fields : null;
}

export function queryFromTableFilters(filters: ItemFilters): SavedViewQuery {
  return {
    type: filters.type,
    state: filters.state,
    assignee: filters.assignee,
    createdBy: filters.createdBy,
    mine: null,
    q: orNull(filters.q),
    sort: filters.sort === null ? null : `${filters.sort.field}:${filters.sort.direction}`,
    due: filters.due,
    priority: filters.priority,
    cycle: filters.cycle,
    milestone: filters.milestone,
    fields: fieldsOrNull(filters.fields),
    category: null,
    project: null,
    group: null,
    // 沒開時不放進去：舊的檢視與伺服器回來的形狀都沒有這兩個，比對「是不是同一個檢視」才不會因為 false 與缺少不同。
    ...(filters.open && { open: true }),
    ...(filters.stale && { stale: true }),
  };
}

/**
 * 看板（與行事曆、時間軸）的排序不是條件（照編號排、依日期排），存 null。流程也不存：它是「看哪一個看板」。
 * 類型與狀態只有行事曆與時間軸有（看板依狀態分欄，那邊的 filters 沒有這兩個，存 null）。
 */
export function queryFromBoardFilters(filters: BoardFilters & Partial<DateViewTypeState>): SavedViewQuery {
  return {
    type: filters.type ?? null,
    state: filters.state ?? null,
    assignee: filters.assignee,
    createdBy: filters.createdBy,
    mine: null,
    q: orNull(filters.q),
    sort: null,
    due: filters.due,
    priority: filters.priority,
    cycle: filters.cycle,
    milestone: filters.milestone,
    fields: fieldsOrNull(filters.fields),
    group: boardGroupParam(filters.group),
    category: null,
    project: null,
  };
}

function paramsOfQuery(query: SavedViewQuery): URLSearchParams {
  const entries: [string, string][] = [];
  // 伺服器存的 JSON 可能是舊的、沒有後來新增的欄位（undefined）：和 null 一樣不寫，不然網址會冒出 createdBy=undefined。
  const add = (name: string, value: string | null | undefined) => {
    if (value != null) entries.push([name, value]);
  };
  add('type', query.type);
  add('state', query.state);
  add('assignee', query.assignee);
  add('createdBy', query.createdBy);
  add('due', query.due);
  add('priority', query.priority);
  add('cycle', query.cycle);
  add('milestone', query.milestone);
  if (query.open === true) entries.push(['open', '1']);
  if (query.stale === true) entries.push(['stale', '1']);
  entries.push(...fieldFilterEntries(query.fields ?? {}));
  add('q', query.q);
  add('sort', query.sort);
  return new URLSearchParams(entries);
}

// 藍圖之後可能改：檢視裡的類型、狀態現在沒有了就略過，不要套用出一張空的表。
function knownFilters(filters: ItemFilters, blueprint: ProjectBlueprint): ItemFilters {
  const type = blueprint.itemTypes.some((itemType) => itemType.key === filters.type) ? filters.type : null;
  const state = filterableStates(blueprint, type).some((candidate) => candidate.key === filters.state) ? filters.state : null;
  return { ...filters, type, state, fields: knownFieldFilters(filters.fields, blueprint) };
}

// 「編號遞增」是沒給排序時的預設：存起來的 number:asc 和網址上沒有 sort 是同一件事。
function withoutDefaultSort(filters: ItemFilters): ItemFilters {
  const isDefault = filters.sort?.field === 'number' && filters.sort.direction === 'asc';
  return isDefault ? { ...filters, sort: null } : filters;
}

/**
 * 套用檢視要導去的查詢字串：和網址上的條件同一套寫法，所以「目前的網址是不是這個檢視」直接比這個字串。
 * 不認得的值（排序、到期、藍圖已經沒有的類型與狀態）和網址手打時一樣略過。
 */
export function paramsOfViewState(state: ViewState, blueprint: ProjectBlueprint): URLSearchParams {
  const filters = knownFilters(withoutDefaultSort(filtersFromSearchParams(paramsOfQuery(state.query))), blueprint);
  if (state.layout === 'table') {
    return filtersToSearchParams(filters);
  }
  const { type, state: itemState, assignee, createdBy, due, priority, cycle, milestone, fields, q } = filters;
  const dateView = state.layout === 'board' ? {} : { type, state: itemState };
  const group = state.layout === 'board' ? knownBoardGroup(parseBoardGroup(state.query.group), blueprint) : STATE_GROUP;
  return boardFiltersToSearchParams(
    { workflow: null, ...dateView, assignee, createdBy, due, priority, cycle, milestone, fields, group, q },
    undefined,
    state.layout,
  );
}

export function savedViewParams(view: SavedView, blueprint: ProjectBlueprint): URLSearchParams {
  return paramsOfViewState(view, blueprint);
}

/** 目前有沒有任何條件（篩選、搜尋、非預設的排序）：套用之後的網址上有沒有東西。 */
export function hasViewConditions(state: ViewState, blueprint: ProjectBlueprint): boolean {
  // 看板、行事曆與時間軸的網址一定帶 view，那是版面不是條件。
  return [...paramsOfViewState(state, blueprint).keys()].some((name) => name !== 'view');
}

/** 目前的版面與條件和這個檢視完全一樣。 */
export function viewMatches(view: SavedView, current: ViewState, blueprint: ProjectBlueprint): boolean {
  return view.layout === current.layout && paramsOfViewState(view, blueprint).toString() === paramsOfViewState(current, blueprint).toString();
}

interface NameContext {
  readonly blueprint: ProjectBlueprint;
  /** null：清單讀不到，名稱用中性的說法（不說已刪除）。 */
  readonly cycles: readonly Cycle[] | null;
  readonly milestones: readonly Milestone[] | null;
  readonly members: readonly Member[];
  /** 目前使用者的人員編號：負責人是自己時寫「我」。 */
  readonly me: string;
}

// 「我建立的」「某某建立的」：單獨一個「我」分不出是負責人還是建立者。
function createdByName(createdBy: string, context: NameContext): string {
  const { members, me } = context;
  const name = createdBy === 'me' || createdBy === me ? '我' : (members.find((member) => member.personId === createdBy)?.displayName ?? '某位成員');
  return `${name}建立的`;
}

function assigneeName(assignee: string, { members, me }: NameContext): string {
  if (assignee === 'me' || assignee === me) return '我';
  if (assignee === 'unassigned') return '沒有負責人';
  return members.find((member) => member.personId === assignee)?.displayName ?? '某位成員';
}

// 名稱裡單獨一個「未設定」看不出是什麼；「優先度未設定」和下拉、項目上的「未設定」是同一個詞。
function priorityName(priority: Priority): string {
  return priority === 'none' ? '優先度未設定' : PRIORITY_FILTER_LABELS[priority];
}

function stateName(query: SavedViewQuery, blueprint: ProjectBlueprint): string | null {
  if (query.state === null) return null;
  const states = filterableStates(blueprint, query.type);
  return states.find((state) => state.key === query.state)?.name ?? query.state;
}

// 目前的週期寫「目前的週期」（檢視存的是 current，不是哪一個週期）；其他寫名稱，刪掉的寫「已刪除的週期」。
function cycleName(cycle: string, { cycles }: NameContext): string {
  if (cycle === CURRENT_CYCLE) return '目前的週期';
  return cycle === NO_PLANNING ? '沒有週期' : displayCycleName(cycle, cycles);
}

function milestoneName(milestone: string, { milestones }: NameContext): string {
  return milestone === NO_PLANNING ? '沒有里程碑' : displayMilestoneName(milestone, milestones);
}

// 「客戶：ACME」「審查：我」「要收費：已勾選」：欄位名稱加上值的說法；藍圖裡不在的欄位略過（套用時也會被拿掉）。
function fieldFilterNames(fields: FieldFilters | null, context: NameContext): string[] {
  if (fields === null) return [];
  return filterableFields(context.blueprint).flatMap((definition) => {
    const wanted = fields[definition.key];
    if (wanted === undefined) return [];
    return [`${definition.name}：${fieldValueName(definition, wanted, context)}`];
  });
}

function fieldValueName(definition: FieldDefinition, wanted: string, context: NameContext): string {
  if (wanted === FIELD_NONE) return '沒有填';
  switch (definition.type) {
    case 'checkbox':
      return wanted === 'true' ? '已勾選' : '沒勾選';
    case 'person':
      return assigneeName(wanted, context);
    default:
      return definition.options?.find((option) => option.key === wanted)?.name ?? wanted;
  }
}

export const LAYOUT_NAMES: Readonly<Record<SavedViewLayout, string>> = {
  table: '表格',
  board: '看板',
  calendar: '行事曆',
  timeline: '時間軸',
};

/**
 * 另存時名稱的預設：目前條件的簡短說法，例如「本週到期・高」。沒有條件時是版面的名稱（「表格」「看板」「行事曆」「時間軸」）；
 * 看板、行事曆與時間軸有條件時前面加版面的名稱，和表格的分得開。
 */
export function defaultViewName(layout: SavedViewLayout, query: SavedViewQuery, context: NameContext): string {
  const due = parseDueFilter(query.due);
  const priority = parsePriority(query.priority);
  const parts = [
    query.type === null ? null : itemTypeName(context.blueprint, query.type),
    stateName(query, context.blueprint),
    query.assignee === null ? null : assigneeName(query.assignee, context),
    query.createdBy === null ? null : createdByName(query.createdBy, context),
    due === null ? null : DUE_FILTER_LABELS[due],
    priority === null ? null : priorityName(priority),
    query.cycle === null ? null : cycleName(query.cycle, context),
    query.milestone === null ? null : milestoneName(query.milestone, context),
    ...fieldFilterNames(query.fields, context),
    query.q === null ? null : `「${query.q}」`,
  ].filter((part): part is string => part !== null);
  const named = layout !== 'table' || parts.length === 0 ? [LAYOUT_NAMES[layout], ...parts] : parts;
  return Array.from(named.join('・')).slice(0, SAVED_VIEW_NAME_MAX_LENGTH).join('');
}
