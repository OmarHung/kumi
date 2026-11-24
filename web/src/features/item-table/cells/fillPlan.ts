import { isApiError } from '../../../api/http';
import type { Item, ItemPatch } from '../../../api/types';
import { mapApiErrorToFields } from '../../../lib/formErrors';
import { TransitionErrorCodes } from './TransitionProblem';
import type { CellCommand } from './useItemCommand';

/** 補填對話框裡的一個欄位：鍵是後端回的 path（核心欄位 due_date、自訂欄位的鍵），errorPaths 是 PATCH 失敗時對到它的 path。 */
export interface FillFieldRef {
  readonly key: string;
  readonly label: string;
  readonly errorPaths: readonly string[];
}

export type FieldMessages = Readonly<Record<string, string>>;

export type FillPlan =
  | { readonly kind: 'invalid'; readonly errors: FieldMessages }
  | { readonly kind: 'ready'; readonly patch: ItemPatch | null };

/**
 * 改狀態因為「要先填欄位」被擋下時，後端在 details 列出缺的欄位（path）。不是這種錯誤時回 null。
 * 規則以後端為準：要填哪些，只看這份回應，前端不從藍圖另外猜。
 */
export function missingFieldPaths(error: unknown): readonly string[] | null {
  if (!isApiError(error) || error.code !== TransitionErrorCodes.fieldsRequired) {
    return null;
  }
  const paths = error.details.flatMap((detail) => (detail.path === null ? [] : [detail.path]));
  return [...new Set(paths)];
}

/** 每個欄位的修改合成一個 PATCH：核心欄位在外層，自訂欄位都放在 fields。沒有修改時是 null。 */
export function mergePatches(patches: readonly ItemPatch[]): ItemPatch | null {
  if (patches.length === 0) {
    return null;
  }
  const merged = patches.reduce<ItemPatch>((all, patch) => ({ ...all, ...patch, fields: { ...all.fields, ...patch.fields } }), {});
  const { fields, ...core } = merged;
  return fields === undefined || Object.keys(fields).length === 0 ? core : merged;
}

/**
 * 按下「填好並移到…」時要做什麼。這些欄位是後端說還沒填的：沒有修改、這次也還沒存過的就是還空著，
 * 先說要填，不必送一個一定會被擋下的請求（看板上的卡片也不會先移過去又彈回來）。
 * 存過了、之後沒再改的不必重送：只重送改狀態。
 */
export function planFill(
  fields: readonly FillFieldRef[],
  commands: ReadonlyMap<string, CellCommand>,
  saved: ReadonlySet<string>,
): FillPlan {
  const errors: [string, string][] = [];
  const patches: ItemPatch[] = [];
  for (const field of fields) {
    const command = commands.get(field.key) ?? { kind: 'unchanged' };
    if (command.kind === 'invalid') {
      errors.push([field.key, command.message]);
    } else if (command.kind === 'patch') {
      patches.push(command.patch);
    } else if (command.kind === 'unchanged' && !saved.has(field.key)) {
      errors.push([field.key, `請填「${field.label}」。`]);
    }
  }
  return errors.length > 0
    ? { kind: 'invalid', errors: Object.fromEntries(errors) }
    : { kind: 'ready', patch: mergePatches(patches) };
}

/** PATCH 失敗：和 EditableCell 一樣，有對到欄位的細項就放在那個欄位，對不到的放在對話框上方。 */
export function fieldErrorsOf(
  error: unknown,
  fields: readonly FillFieldRef[],
): { readonly fieldErrors: FieldMessages; readonly formError: string | null } {
  const mapped = mapApiErrorToFields(error, { fields: fields.flatMap((field) => field.errorPaths) });
  const entries = fields.flatMap((field) => {
    const message = field.errorPaths.map((path) => mapped.fieldErrors[path]).find((candidate) => candidate !== undefined);
    return message === undefined ? [] : [[field.key, message] as const];
  });
  return { fieldErrors: Object.fromEntries(entries), formError: mapped.formError };
}

/** 核心欄位「沒有值」的樣子（和後端 TransitionGuard 判斷「沒填」的方式一樣：優先度「無」算沒填）。 */
const EMPTY_CORE: Readonly<Partial<Record<string, Partial<Item>>>> = {
  assignee: { assignee: null },
  priority: { priority: 'none' },
  start_date: { startDate: null },
  due_date: { dueDate: null },
  estimate: { estimate: null },
  parent: { parentKey: null },
};

/**
 * 後端說這些欄位還是空的（例如剛存好又被要求填）：以後端為準，把它們當成空的。對話框拿它當比對的基準，
 * 那幾格清空重填、再按一次會重送 PATCH，不會卡在「沒有修改、又說要填」。
 */
export function clearedFields(item: Item, paths: readonly string[]): Item {
  const core = paths.reduce<Partial<Item>>((all, path) => ({ ...all, ...EMPTY_CORE[path] }), {});
  const fields = Object.fromEntries(Object.entries(item.fields).filter(([key]) => !paths.includes(key)));
  return { ...item, ...core, fields };
}
