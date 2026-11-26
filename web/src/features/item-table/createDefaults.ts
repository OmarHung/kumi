import type { ProjectBlueprint } from '../../api/blueprint';
import type { Cycle, Milestone } from '../../api/planning';
import type { CalendarDate, FieldValue, Item, Member, Priority, ProjectDetail } from '../../api/types';
import type { WeekStart } from '../../lib/weekStart';
import type { DueFilter } from '../../domain/dueFilter';
import { OPEN_CATEGORIES } from '../../domain/dueFilter';
import { staleCutoff, type ItemFilters } from '../../domain/itemFilters';
import { cycleDefault, milestoneDefault } from '../../domain/planningFilter';
import { matchesBoardFilters } from '../../domain/projectView';
import { STATE_GROUP } from '../../domain/boardGroup';
import { findItemType } from '../../domain/blueprint';
import { FIELD_NONE, type FieldFilters } from '../../domain/fieldFilters';

/**
 * 新增沿用當下的情境（docs/DESIGN.md 8.1 第 7 點）：在篩選過的清單裡新增，新的項目預設帶著那些條件，
 * 不會一新增就從眼前消失。這裡只有純邏輯，表格上方的快速新增、窄螢幕的對話框與看板共用。
 */

/** 篩選只選了一種類型、而且是這個專案有的類型時，預設是它；否則是第一個類型（專案還沒有類型時是空字串）。 */
export function defaultItemType(detail: ProjectDetail, filterType: string | null): string {
  const { itemTypes } = detail.blueprint;
  const filtered = itemTypes.find((itemType) => itemType.key === filterType);
  return (filtered ?? itemTypes[0])?.key ?? '';
}

/**
 * 篩選的負責人（「我」或某個成員）。沒有篩選、篩「沒有負責人」、不是這個專案的成員（例如網址手打、
 * 自己是沒加入專案的組織管理員）時不帶：後端只接受成員，帶了只會被擋下。
 */
export function assigneeFromFilter(detail: ProjectDetail, assignee: string | null, me: string): string | undefined {
  if (assignee === null || assignee === 'unassigned') return undefined;
  const personId = assignee === 'me' ? me : assignee;
  return detail.members.some((member) => member.personId === personId) ? personId : undefined;
}

/**
 * 新的項目符不符合表格目前的條件：類型、狀態，再加上負責人、到期、優先度、週期、里程碑與搜尋（和看板、後端同一套比對）。
 * 不符合時新增的訊息要說一聲：它先放在清單最上面，重抓之後就不在這份清單裡了。
 */
export function matchesTableFilters(
  item: Item,
  filters: ItemFilters,
  me: string,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[] = [],
  now: Date = new Date(),
): boolean {
  const { assignee, createdBy, due, priority, cycle, milestone, fields, q } = filters;
  return (
    (filters.type === null || item.type === filters.type) &&
    (filters.state === null || item.state === filters.state) &&
    // 新增的項目是剛更新的，不會落在「停滯」裡：新增之後的訊息會說它不在這份清單。
    (!(filters.open || filters.stale) || OPEN_CATEGORIES.includes(item.stateCategory)) &&
    (!filters.stale || Date.parse(item.updatedAt) < Date.parse(staleCutoff(now))) &&
    matchesBoardFilters(item, { workflow: null, assignee, createdBy, due, priority, cycle, milestone, fields, group: STATE_GROUP, q }, me, todayDate, weekStart, cycles)
  );
}

/** 新增時帶的優先度與到期日。 */
export interface ItemDefaults {
  readonly priority?: Priority;
  readonly dueDate?: CalendarDate;
  readonly cycleId?: string;
  readonly milestoneId?: string;
  /**
   * 自訂欄位的預設值（篩選的欄位、看板那一欄的值），依送出時選的類型給：新增的表單可以換類型，
   * 類型沒有的欄位帶了後端會把整個新增擋下。
   */
  readonly fieldsFor?: (type: string) => Readonly<Record<string, FieldValue>>;
}

function fieldDefault(blueprint: ProjectBlueprint, key: string, wanted: string, me: string, members: readonly Member[]): FieldValue | undefined {
  const definition = blueprint.fields.find((field) => field.key === key);
  const isOption = definition?.options?.some((option) => option.key === wanted) ?? false;
  switch (definition?.type) {
    case 'single_select':
      return isOption ? wanted : undefined;
    case 'multi_select':
      return isOption ? [wanted] : undefined;
    case 'person': {
      // 和負責人一樣只收專案成員：後端不收別人，帶了新增會被整個擋下。
      const personId = wanted === 'me' ? me : wanted;
      return members.some((member) => member.personId === personId) ? personId : undefined;
    }
    case 'checkbox':
      return wanted === 'true' ? true : undefined;
    default:
      return undefined;
  }
}

/**
 * 篩了自訂欄位的值，新的項目就帶著它（和負責人、優先度一樣，不會一新增就從眼前消失）。
 * 「沒有填」「沒勾選」本來就是新項目的樣子，不帶；藍圖裡已經沒有的選項、不是專案成員的人不帶；這個類型沒有的欄位不帶。
 */
export function fieldDefaultsFromFilters(
  blueprint: ProjectBlueprint,
  fields: FieldFilters,
  type: string,
  me: string,
  members: readonly Member[],
): Readonly<Record<string, FieldValue>> {
  const typeFields = new Set(findItemType(blueprint, type)?.fields ?? []);
  const entries = Object.entries(fields).flatMap(([key, wanted]): [string, FieldValue][] => {
    if (!typeFields.has(key) || wanted === FIELD_NONE) return [];
    const value = fieldDefault(blueprint, key, wanted, me, members);
    return value === undefined ? [] : [[key, value]];
  });
  return Object.fromEntries(entries);
}

/**
 * 篩了優先度就帶那個優先度（「未設定」本來就是預設，不帶）；篩「今天到期」就帶今天。
 * 「已過期」「本週」「沒有到期日」沒有唯一的一天，不預設：新的項目不符合時由「已新增」的訊息說明。
 * 篩了某個週期（含「目前的週期」，換成今天所在的那一個）或里程碑就排進去；「沒有週期」「沒有里程碑」不帶，它本來就是預設。
 */
export function itemDefaultsFromFilters(
  filters: { readonly due: DueFilter | null; readonly priority: Priority | null; readonly cycle: string | null; readonly milestone: string | null },
  todayDate: CalendarDate,
  cycles: readonly Cycle[],
  milestones: readonly Milestone[] = [],
): ItemDefaults {
  const cycleId = cycleDefault(filters.cycle, cycles, todayDate);
  const milestoneId = milestoneDefault(filters.milestone, milestones);
  return {
    ...(filters.priority !== null && filters.priority !== 'none' && { priority: filters.priority }),
    ...(filters.due === 'today' && { dueDate: todayDate }),
    ...(cycleId !== undefined && { cycleId }),
    ...(milestoneId !== undefined && { milestoneId }),
  };
}
