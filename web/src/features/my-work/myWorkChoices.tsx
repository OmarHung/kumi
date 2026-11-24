import type { ItemTypeSummary, Project, ProjectDetail } from '../../api/types';
import type { SelectOption } from '../../components/Select/Select';
import { STATE_CATEGORIES, type MyWorkFilters, type MyWorkStatus } from '../../domain/myWorkView';
import { filterableStates } from '../../domain/workflow';
import { AnyStateMark, CATEGORY_OPTIONS } from '../work-items/selectOptionMarks';
import { stateFilterChoices, stateKeyOfValue } from '../work-items/stateChoices';

/*
 * 「我的工作」工具列上各個下拉的選項（internal/HANDOFF-my-work-views.md 5.1）。選項的文字帶出意思
 * （「所有類型」「未結束的狀態」），工具列上的標籤才能視覺上隱藏。
 */

const OPEN_VALUE = 'open';
const ALL_VALUE = 'all';
const CATEGORY_PREFIX = 'category:';
const STATE_PREFIX = 'state:';

/** 狀態的條件在下拉裡的值：類別與個別的狀態各用一個前綴，不會和 key 撞在一起。 */
export function statusValueOf(status: MyWorkStatus): string {
  switch (status.kind) {
    case 'open':
      return OPEN_VALUE;
    case 'all':
      return ALL_VALUE;
    case 'category':
      return `${CATEGORY_PREFIX}${status.category}`;
    case 'state':
      return `${STATE_PREFIX}${status.state}`;
  }
}

export function statusFromValue(value: string): MyWorkStatus {
  if (value === ALL_VALUE) return { kind: 'all' };
  const category = STATE_CATEGORIES.find((candidate) => `${CATEGORY_PREFIX}${candidate}` === value);
  if (category !== undefined) return { kind: 'category', category };
  return value.startsWith(STATE_PREFIX) ? { kind: 'state', state: stateKeyOfValue(value.slice(STATE_PREFIX.length)) } : { kind: 'open' };
}

/** 類別、各專案的狀態前面各一條分隔線（規範 7.3 的分組）；狀態依流程分組時第一項帶組名，組名自己有線（兩者擇一）。 */
function startingGroup(options: readonly SelectOption[], prefix: string): SelectOption[] {
  return options.map((option, index) => ({ ...option, value: `${prefix}${option.value}`, startsGroup: index === 0 && option.groupLabel === undefined }));
}

/**
 * 狀態的選項：「未結束的狀態」（預設）、「所有狀態」，五個類別；選了專案時再加那個專案的狀態
 * （選了類型就照那個類型的流程；沒選而專案有不只一個流程時依流程分組，和專案頁的篩選一樣）。專案內容還沒讀到、而網址上已經有狀態時，先用它的 key 當名稱，不讓下拉空著。
 */
export function statusOptions(filters: MyWorkFilters, detail: ProjectDetail | undefined): SelectOption[] {
  const states = detail === undefined ? [] : stateFilterChoices(detail.blueprint, filters.type).options;
  const options: SelectOption[] = [
    { value: OPEN_VALUE, label: '未結束的狀態', leading: <AnyStateMark /> },
    { value: ALL_VALUE, label: '所有狀態', leading: <AnyStateMark /> },
    ...startingGroup(CATEGORY_OPTIONS, CATEGORY_PREFIX),
    ...startingGroup(states, STATE_PREFIX),
  ];
  const current = statusValueOf(filters.status);
  return options.some((option) => option.value === current) ? options : [...options, { value: current, label: current.slice(STATE_PREFIX.length) }];
}

/**
 * 類型的選項。跨專案時是 /v1/item-types 合併好的（名稱不同時用「／」接起來）；選了專案時是那個專案的類型。
 * 網址上的類型不在清單裡時補上它，下拉才不會空著。
 */
export function typeOptions(
  summaries: readonly ItemTypeSummary[] | undefined,
  detail: ProjectDetail | undefined,
  filters: MyWorkFilters,
): SelectOption[] {
  const choices =
    filters.project === null
      ? (summaries ?? []).map((summary) => ({ value: summary.key, label: summary.names.join('／') }))
      : (detail?.blueprint.itemTypes ?? []).map((itemType) => ({ value: itemType.key, label: itemType.name }));
  const options = [{ value: '', label: '所有類型' }, ...choices];
  const { type } = filters;
  return type === null || options.some((option) => option.value === type) ? options : [...options, { value: type, label: type }];
}

/** 類型下拉要不要放：只有一種（或沒有）時不放；但網址已經選了類型的話留著，才改得掉。 */
export function hasTypeChoice(options: readonly SelectOption[], filters: MyWorkFilters): boolean {
  return options.length > 2 || filters.type !== null;
}

/** 專案的選項：「所有專案」，再列沒有封存的專案；網址上指定的專案不在清單裡（封存、看不到）時補上代號。 */
export function projectOptions(projects: readonly Project[] | undefined, current: string | null): SelectOption[] {
  const choices = (projects ?? [])
    .filter((project) => project.archivedAt === null)
    .map((project) => ({ value: project.key, label: project.name, keywords: [project.key] }));
  const options = [{ value: '', label: '所有專案' }, ...choices];
  return current === null || options.some((option) => option.value === current) ? options : [...options, { value: current, label: current }];
}

/**
 * 選了專案時，類型、狀態要在那個專案裡：不在的就拿掉（和專案頁換類型時拿掉狀態一樣）。
 * 專案內容還沒讀到（或讀不到）、沒有選專案時照原樣。沒有變動時回傳同一個物件。
 */
export function pruneToProject(filters: MyWorkFilters, detail: ProjectDetail | undefined): MyWorkFilters {
  if (filters.project === null || detail === undefined) return filters;
  const { blueprint } = detail;
  const type = filters.type !== null && blueprint.itemTypes.some((itemType) => itemType.key === filters.type) ? filters.type : null;
  const { status } = filters;
  const isStateKnown = status.kind !== 'state' || filterableStates(blueprint, type).some((state) => state.key === status.state);
  if (type === filters.type && isStateKnown) return filters;
  return { ...filters, type, status: isStateKnown ? status : { kind: 'open' } };
}
