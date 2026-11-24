import type {
  FieldDefinition,
  FieldOption,
  ItemTypeDefinition,
  ProjectBlueprint,
  RoleDefinition,
  ViewDefinition,
  Workflow,
  WorkflowState,
  WorkflowTransition,
} from '../api/blueprint';
import type { ChangeSetRequest, StateMove } from '../api/types';
import { sameName, sameSet, type FieldDraft, type ItemTypeDraft, type SettingsDraft, type WorkflowDraft } from './settingsDraft';
import { removedRoles } from './settingsRoles';
import { removedItemTypes } from './settingsTypeRemoval';
import { findTransition, sameLimits, visibleTransitions } from './settingsTransitions';
import { requiredTypeMoves } from './settingsTypes';

/**
 * 把設定頁的草稿變成要送出的變更集：想要的藍圖全文加上搬移與封存的指示。
 * 設定頁改不了的部分（檢視、自動化、整合）照目前的帶過去；被刪除的狀態、欄位、角色、類型在別處的引用要一起拿掉
 * （轉換、類型的 fields 與上層、轉換的必填欄位與允許的角色、檢視），否則伺服器會因為引用不存在而擋下。
 * 自動化不改：用到被刪除的東西時只在那一列提醒，伺服器會擋下。
 * 沒有被動到的物件原樣帶過去，伺服器算出來的差異才只有真的改了的地方。
 */

function isSelect(field: FieldDefinition): boolean {
  return field.type === 'single_select' || field.type === 'multi_select';
}

/** 名稱送出前去掉前後的空白（伺服器原樣存下來，畫面上看不出差別）。 */
function trimmedOption(option: FieldOption): FieldOption {
  const name = option.name.trim();
  return name === option.name ? option : { ...option, name };
}

/** 送出的欄位定義：不帶草稿用的 appliesTo；不是選項型就不帶 options、不是金額就不帶 currency（寫了空的也算寫了）。 */
function toDefinition(field: FieldDraft): FieldDefinition {
  const { key, name, type, description, options, currency } = field;
  return {
    key,
    name: name.trim(),
    type,
    ...(description !== undefined && { description }),
    ...(isSelect(field) && { options: (options ?? []).map(trimmedOption) }),
    ...(type === 'currency' && currency !== undefined && { currency }),
  };
}

/** 草稿裡的狀態：名稱去掉前後的空白；和原本的只差空白時送原本的那一個，伺服器才不會算成修改。 */
function normalizedState(state: WorkflowState, original: readonly WorkflowState[]): WorkflowState {
  const before = original.find((candidate) => candidate.key === state.key);
  if (before !== undefined && sameName(before.name, state.name) && before.category === state.category) {
    return before;
  }
  const name = state.name.trim();
  return name === state.name ? state : { ...state, name };
}

function keysOf(list: readonly { readonly key: string }[]): ReadonlySet<string> {
  return new Set(list.map((entry) => entry.key));
}

/** 被刪除的自訂欄位、自訂角色與類型：別處對它們的引用要一起拿掉。 */
interface Removed {
  readonly fields: ReadonlySet<string>;
  readonly roles: ReadonlySet<string>;
  readonly types: ReadonlySet<string>;
}

function pruneTransition(transition: WorkflowTransition, removed: Removed): WorkflowTransition {
  const roles = transition.allowedRoles ?? [];
  const fields = transition.requireFields ?? [];
  const hitsRoles = roles.some((role) => removed.roles.has(role));
  const hitsFields = fields.some((field) => removed.fields.has(field));
  if (!hitsRoles && !hitsFields) {
    return transition;
  }
  return {
    ...transition,
    ...(hitsRoles && { allowedRoles: roles.filter((role) => !removed.roles.has(role)) }),
    ...(hitsFields && { requireFields: fields.filter((field) => !removed.fields.has(field)) }),
  };
}

/**
 * 每個清單和原本同一組（只是勾選的先後不同）就送原本的陣列，兩個都是就送原本的物件：
 * 只改了其中一個時，另一個被重新排序的話伺服器會多算一個「改了」。
 */
function buildTransition(transition: WorkflowTransition, original: WorkflowTransition | undefined, removed: Removed): WorkflowTransition {
  if (original === undefined) {
    return pruneTransition(transition, removed);
  }
  if (sameLimits(transition, original)) {
    return pruneTransition(original, removed);
  }
  const chosen = {
    ...transition,
    ...(sameSet(transition.allowedRoles, original.allowedRoles) && { allowedRoles: original.allowedRoles }),
    ...(sameSet(transition.requireFields, original.requireFields) && { requireFields: original.requireFields }),
  };
  return pruneTransition(chosen, removed);
}

function sameList<T>(left: readonly T[], right: readonly T[]): boolean {
  return left.length === right.length && left.every((entry, index) => entry === right[index]);
}

function buildWorkflow(workflow: Workflow, draft: WorkflowDraft | undefined, removed: Removed): Workflow {
  if (draft === undefined) {
    return workflow;
  }
  const { transitions: original, ...rest } = workflow;
  const states = draft.states.map((state) => normalizedState(state, workflow.states));
  const transitions = visibleTransitions(draft)?.map((transition) =>
    buildTransition(transition, findTransition(original, transition.from, transition.to), removed),
  );
  const isSameTransitions =
    transitions === undefined || original === undefined ? transitions === original : sameList(transitions, original);
  if (sameList(states, workflow.states) && isSameTransitions) {
    return workflow;
  }
  return { ...rest, states, ...(transitions !== undefined && { transitions }) };
}

/** 新類型的 fields 照欄位的順序；既有的：原本有、而且還套用的照原本的順序，再接上新套用的（照欄位的順序）。 */
function typeFields(original: readonly string[], typeKey: string, fields: readonly FieldDraft[]): readonly string[] {
  const applies = (key: string) => fields.find((field) => field.key === key)?.appliesTo.includes(typeKey) ?? false;
  const added = fields.filter((field) => field.appliesTo.includes(typeKey) && !original.includes(field.key));
  return [...original.filter(applies), ...added.map((field) => field.key)];
}

/**
 * 使用者沒動到的類型原樣送出；動到的只換掉改了的屬性，其他（例如 icon）照原本的。新類型的形狀和 API 回的一樣。
 * 上層拿掉要刪除的類型（草稿裡留著，復原時跟著回來）。
 */
function buildItemType(type: ItemTypeDraft, original: ItemTypeDefinition | undefined, fields: readonly FieldDraft[], removed: Removed): ItemTypeDefinition {
  const parents = type.parents.filter((parent) => !removed.types.has(parent));
  if (original === undefined) {
    return { key: type.key, name: type.name.trim(), workflow: type.workflow, fields: typeFields([], type.key, fields), parents };
  }
  const next = typeFields(original.fields ?? [], type.key, fields);
  const changes = {
    ...(!sameName(type.name, original.name) && { name: type.name.trim() }),
    ...(type.workflow !== original.workflow && { workflow: type.workflow }),
    ...(!sameList(next, original.fields ?? []) && { fields: next }),
    ...(!sameSet(parents, original.parents) && { parents }),
  };
  return Object.keys(changes).length === 0 ? original : { ...original, ...changes };
}

/** 名稱與權限分開比：只改了名稱時權限送原本的陣列（草稿照 PERMISSION_ORDER 排，原本的順序可能不同）。 */
function buildRole(role: RoleDefinition, original: RoleDefinition | undefined): RoleDefinition {
  const isSamePermissions = original !== undefined && sameSet(role.permissions, original.permissions);
  if (original !== undefined && sameName(role.name, original.name) && isSamePermissions) {
    return original;
  }
  const permissions = isSamePermissions ? original.permissions : role.permissions;
  return { ...original, key: role.key, name: role.name.trim(), permissions };
}

/** 原本沒有 roles 這個屬性、也沒有自訂角色時不送，免得多出一個空的清單。 */
function buildRoles(base: ProjectBlueprint, draft: SettingsDraft): readonly RoleDefinition[] | undefined {
  const roles = draft.roles.map((role) => buildRole(role, base.roles?.find((candidate) => candidate.key === role.key)));
  return base.roles === undefined && roles.length === 0 ? undefined : roles;
}

function pruneViewFields(view: ViewDefinition, removed: ReadonlySet<string>): ViewDefinition {
  const touches =
    (view.groupBy !== undefined && removed.has(view.groupBy)) ||
    (view.columns ?? []).some((column) => removed.has(column)) ||
    (view.sort ?? []).some((sort) => removed.has(sort.field));
  if (!touches) {
    return view;
  }
  const { groupBy, ...rest } = view;
  return {
    ...rest,
    ...(groupBy !== undefined && !removed.has(groupBy) && { groupBy }),
    ...(view.columns && { columns: view.columns.filter((column) => !removed.has(column)) }),
    ...(view.sort && { sort: view.sort.filter((sort) => !removed.has(sort.field)) }),
  };
}

/**
 * 檢視篩選的狀態只要還在任何一個流程裡就算數。全部不在了就換成項目搬去的狀態（去重）：
 * 直接拿掉的話，篩選只剩被刪除的那一個狀態時清單會變空，空的篩選是「不篩選」，檢視反而變寬。
 */
function pruneViewStates(view: ViewDefinition, remainingStates: ReadonlySet<string>, draft: SettingsDraft): ViewDefinition {
  const states = view.filter?.states;
  if (view.filter === undefined || states === undefined || states.every((state) => remainingStates.has(state))) {
    return view;
  }
  const targetsOf = (state: string) =>
    draft.workflows.flatMap((workflow) => workflow.moves.filter((move) => move.from === state).map((move) => move.to));
  const replaced = states.flatMap((state) => (remainingStates.has(state) ? [state] : targetsOf(state).filter((to) => remainingStates.has(to))));
  return { ...view, filter: { ...view.filter, states: [...new Set(replaced)] } };
}

/**
 * 檢視的類型篩選拿掉被刪除的類型。變空就是不篩選類型（送空陣列，和 API 回的形狀一樣）：範圍變寬，設定頁會提醒。
 */
function pruneViewTypes(view: ViewDefinition, removed: ReadonlySet<string>): ViewDefinition {
  const types = view.filter?.itemTypes;
  if (view.filter === undefined || types === undefined || !types.some((type) => removed.has(type))) {
    return view;
  }
  return { ...view, filter: { ...view.filter, itemTypes: types.filter((type) => !removed.has(type)) } };
}

/** 檢視的 visibleTo 拿掉被刪除的角色；變空時整個不送（省略是所有成員都看得到，和 API 回的形狀一樣）。 */
function pruneViewRoles(view: ViewDefinition, removed: ReadonlySet<string>): ViewDefinition {
  const { visibleTo, ...rest } = view;
  if (visibleTo === undefined || !visibleTo.some((role) => removed.has(role))) {
    return view;
  }
  const remaining = visibleTo.filter((role) => !removed.has(role));
  return remaining.length === 0 ? rest : { ...rest, visibleTo: remaining };
}

function buildBlueprint(base: ProjectBlueprint, draft: SettingsDraft, removed: Removed): ProjectBlueprint {
  const workflows = base.workflows.map((workflow) =>
    buildWorkflow(workflow, draft.workflows.find((candidate) => candidate.key === workflow.key), removed),
  );
  const remainingStates = new Set(workflows.flatMap((workflow) => workflow.states.map((state) => state.key)));
  const views = base.views?.map((view) =>
    pruneViewTypes(pruneViewRoles(pruneViewStates(pruneViewFields(view, removed.fields), remainingStates, draft), removed.roles), removed.types),
  );
  const roles = buildRoles(base, draft);
  return {
    ...base,
    fields: draft.fields.map(toDefinition),
    workflows,
    itemTypes: draft.itemTypes.map((type) =>
      buildItemType(type, base.itemTypes.find((candidate) => candidate.key === type.key), draft.fields, removed),
    ),
    ...(views !== undefined && { views }),
    ...(roles !== undefined && { roles }),
  };
}

/** 目前藍圖裡有、草稿裡沒有的欄位（要封存），照目前藍圖的順序。 */
export function removedFields(base: ProjectBlueprint, draft: SettingsDraft): readonly FieldDefinition[] {
  const remaining = keysOf(draft.fields);
  return base.fields.filter((field) => !remaining.has(field.key));
}

/** 刪除狀態的搬移，照流程、再照目前藍圖裡狀態的順序。 */
function deletionMoves(base: ProjectBlueprint, draft: SettingsDraft): readonly StateMove[] {
  return draft.workflows.flatMap((workflow) => {
    const original = base.workflows.find((candidate) => candidate.key === workflow.key)?.states ?? [];
    return original.flatMap((state) => {
      const move = workflow.moves.find((candidate) => candidate.from === state.key);
      return move === undefined ? [] : [{ workflow: workflow.key, from: move.from, to: move.to }];
    });
  });
}

/** 刪除狀態的搬移，再接上類型換流程的搬移；伺服器每個（流程, 狀態）只認一條，重複的以刪除狀態的為準。 */
function moveStates(base: ProjectBlueprint, draft: SettingsDraft): readonly StateMove[] {
  const deletions = deletionMoves(base, draft);
  const typeMoves = requiredTypeMoves(base, draft)
    .map((move) => ({ workflow: move.workflow, from: move.from.key, to: move.to }))
    .filter((move) => !deletions.some((deletion) => deletion.workflow === move.workflow && deletion.from === move.from));
  return [...deletions, ...typeMoves];
}

/** summary 只有填了才送；前後的空白拿掉。 */
export function buildChangeSet(
  base: ProjectBlueprint,
  baseVersion: number,
  draft: SettingsDraft,
  summary: string,
): ChangeSetRequest {
  const dropFields = removedFields(base, draft).map((field) => field.key);
  const removed = {
    fields: new Set(dropFields),
    roles: new Set(removedRoles(base, draft).map((role) => role.key)),
    types: new Set(removedItemTypes(base, draft).map((type) => type.key)),
  };
  const trimmed = summary.trim();
  return {
    baseVersion,
    ...(trimmed !== '' && { summary: trimmed }),
    blueprint: buildBlueprint(base, draft, removed),
    migrations: { moveStates: moveStates(base, draft), dropFields },
  };
}

/** 刪除這個欄位時，除了類型之外還會一起拿掉的引用，給設定頁說明用：「轉換的必填欄位」「檢視「需求清單」」。 */
export function fieldReferences(base: ProjectBlueprint, draft: SettingsDraft, fieldKey: string): readonly string[] {
  const inTransitions = draft.workflows.some((workflow) =>
    (visibleTransitions(workflow) ?? []).some((transition) => transition.requireFields?.includes(fieldKey)),
  );
  const views = (base.views ?? []).filter(
    (view) =>
      view.groupBy === fieldKey ||
      (view.columns ?? []).includes(fieldKey) ||
      (view.sort ?? []).some((sort) => sort.field === fieldKey),
  );
  return [...(inTransitions ? ['轉換的必填欄位'] : []), ...views.map((view) => `檢視「${view.name}」`)];
}

/** 欄位取消套用到某個類型，而那個類型用的流程有轉換要求先填它。 */
export interface DetachConflict {
  readonly itemType: string;
  readonly workflow: string;
}

/**
 * 這個欄位沒有套用、但用的流程有轉換要求先填它的類型（照草稿裡類型的順序）。
 * 類型沒有這個欄位就永遠填不了，伺服器一定擋下（transition.field_missing_on_type）。
 */
export function detachConflicts(draft: SettingsDraft, field: FieldDraft): readonly DetachConflict[] {
  const requiring = new Set(
    draft.workflows
      .filter((workflow) => (visibleTransitions(workflow) ?? []).some((transition) => transition.requireFields?.includes(field.key)))
      .map((workflow) => workflow.key),
  );
  return draft.itemTypes
    .filter((type) => requiring.has(type.workflow) && !field.appliesTo.includes(type.key))
    .map((type) => ({ itemType: type.key, workflow: type.workflow }));
}
