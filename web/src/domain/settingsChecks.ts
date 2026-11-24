import type { ProjectBlueprint, WorkflowTransition } from '../api/blueprint';
import { CORE_FIELD_LABELS } from './fields';
import { isBuiltInProjectRole, PERMISSION_LABELS } from './labels';
import type { ItemTypeDraft, SettingsDraft, WorkflowDraft } from './settingsDraft';
import type { SettingsIssue, SettingsTarget } from './settingsIssues';
import { displayName, keyFormatError, nameError } from './settingsNames';
import { assignableRoles, canTransitionItems, draftRolePermissions } from './settingsRoles';
import { visibleTransitions } from './settingsTransitions';
import { requiredTypeMoves } from './settingsTypes';

/**
 * 轉換、類型、角色的送出前檢查，和伺服器的語意規則一樣（WorkflowRules、TransitionRules、ItemTypeRules、ParentCycles、RoleRules）。
 * 自己轉到自己、重複的轉換在畫面上做不出來，不必檢查。
 */

const PARENT = 'parent';
const TERMINAL = new Set(['done', 'canceled']);

function stateTarget(workflow: string, state: string): SettingsTarget {
  return { kind: 'state', workflow, state };
}

/** 從起始狀態沿著轉換走得到的狀態（廣度優先，走過的不再走，繞圈也會停）。 */
function reachableFrom(start: string, transitions: readonly WorkflowTransition[]): ReadonlySet<string> {
  const reached = new Set([start]);
  const pending = [start];
  for (let state = pending.shift(); state !== undefined; state = pending.shift()) {
    for (const transition of transitions) {
      if (transition.from === state && !reached.has(transition.to)) {
        reached.add(transition.to);
        pending.push(transition.to);
      }
    }
  }
  return reached;
}

/** 有限制的流程：每個狀態都要從起始狀態走得到，完成與已取消以外的要有出路。標在那個狀態上，照狀態的順序。 */
export function reachabilityIssues(workflow: WorkflowDraft): readonly SettingsIssue[] {
  const transitions = visibleTransitions(workflow);
  const [first] = workflow.states;
  if (transitions === undefined || first === undefined) {
    return [];
  }
  const reached = reachableFrom(first.key, transitions);
  return workflow.states.flatMap((state) => {
    const target = stateTarget(workflow.key, state.key);
    const name = displayName(state);
    const unreachable = reached.has(state.key)
      ? []
      : [{ target, message: `從起始狀態「${displayName(first)}」走不到「${name}」：請在「轉換」勾一條轉進來的路。` }];
    const hasExit = transitions.some((transition) => transition.from === state.key);
    const deadEnd =
      TERMINAL.has(state.category) || hasExit ? [] : [{ target, message: `「${name}」不是完成或已取消類，卻沒有可以轉出去的轉換。` }];
    return [...unreachable, ...deadEnd];
  });
}

/** 一條轉換要求先填的一個欄位，用這個流程的哪些類型填不了。被刪除的自訂欄位（送出時拿掉）與上層以外的核心欄位都不必檢查。 */
function missingMessages(draft: SettingsDraft, types: readonly ItemTypeDraft[], field: string, workflowName: string): readonly string[] {
  if (field === PARENT) {
    const label = CORE_FIELD_LABELS[PARENT] ?? PARENT;
    // 上層只看草稿裡還在的類型：要刪除的類型送出時會從上層拿掉。
    const hasParent = (type: ItemTypeDraft) => type.parents.some((parent) => draft.itemTypes.some((candidate) => candidate.key === parent));
    return types
      .filter((type) => !hasParent(type))
      .map((type) => `「${displayName(type)}」不能掛在任何類型底下：這條轉換要求先填「${label}」，「${displayName(type)}」永遠走不過去。`);
  }
  const definition = draft.fields.find((candidate) => candidate.key === field);
  if (definition === undefined) {
    return [];
  }
  return types
    .filter((type) => !definition.appliesTo.includes(type.key))
    .map((type) => `「${displayName(type)}」沒有套用「${displayName(definition)}」：用「${workflowName}」的類型都要有這個欄位，這條轉換才走得過去。`);
}

/** 轉換要求先填的欄位，每個用這個流程的類型都要填得了（類型換流程、欄位取消套用造成的都算）。標在那條轉換上。 */
export function requiredFieldIssues(base: ProjectBlueprint, draft: SettingsDraft, workflow: WorkflowDraft): readonly SettingsIssue[] {
  const types = draft.itemTypes.filter((type) => type.workflow === workflow.key);
  const workflowName = base.workflows.find((candidate) => candidate.key === workflow.key)?.name ?? workflow.key;
  return (visibleTransitions(workflow) ?? []).flatMap((transition) => {
    const target = { kind: 'transition', workflow: workflow.key, from: transition.from, to: transition.to } as const;
    return (transition.requireFields ?? []).flatMap((field) =>
      missingMessages(draft, types, field, workflowName).map((message) => ({ target, message })),
    );
  });
}

function quotedNames(names: readonly string[]): string {
  return names.map((name) => `「${name}」`).join('、');
}

/**
 * 轉換限了角色，而每個允許的角色都沒有「改變狀態」：伺服器改狀態先看 item.transition、再看 allowedRoles，
 * 這條轉換沒有人走得了。被刪除的角色送出時會拿掉，不算；拿掉之後不限角色就不擋。
 */
export function transitionRoleIssues(draft: SettingsDraft, workflow: WorkflowDraft): readonly SettingsIssue[] {
  const roleName = (key: string) => {
    const role = assignableRoles(draft).find((candidate) => candidate.key === key);
    return role === undefined ? key : displayName(role);
  };
  return (visibleTransitions(workflow) ?? []).flatMap((transition) => {
    const roles = (transition.allowedRoles ?? []).filter((role) => draftRolePermissions(draft, role) !== undefined);
    if (roles.length === 0 || roles.some((role) => canTransitionItems(draft, role))) {
      return [];
    }
    // 轉換的問題列在表格下方時前面已經有「「A」→「B」：」，訊息裡不再重複兩端的名稱。
    const who = roles.length === 1 ? '這個角色沒有' : '這些角色都沒有';
    const message = `只允許${quotedNames(roles.map(roleName))}，但${who}「${PERMISSION_LABELS['item.transition']}」的權限，沒有人走得了這條轉換。`;
    return [{ target: { kind: 'transition', workflow: workflow.key, from: transition.from, to: transition.to } as const, message }];
  });
}

const UNVISITED = 0;
const ON_PATH = 1;
const FINISHED = 2;

/**
 * 把上層關係閉合成一圈的那幾條邊（類型 key → 上層 key），和伺服器的 ParentCycles 一樣：
 * 照類型的順序深度優先走，指回目前路徑上的類型就是它把圈閉合。拿掉這些邊圖就沒有圈了。
 */
function closingEdges(types: readonly ItemTypeDraft[]): readonly (readonly [string, string])[] {
  const positions = new Map(types.map((type, index) => [type.key, index]));
  const marks = types.map(() => UNVISITED);
  const closing: (readonly [string, string])[] = [];
  const visit = (position: number) => {
    const type = types[position];
    marks[position] = ON_PATH;
    for (const parent of type?.parents ?? []) {
      const target = positions.get(parent);
      if (target !== undefined && marks[target] === ON_PATH) closing.push([type?.key ?? '', parent]);
      else if (target !== undefined && marks[target] === UNVISITED) visit(target);
    }
    marks[position] = FINISHED;
  };
  types.forEach((_, position) => {
    if (marks[position] === UNVISITED) visit(position);
  });
  return closing;
}

/** 類型：至少一個、名稱、上層繞成一圈、換流程的搬移還沒有目標（標在那條搬移）。上層的問題照類型的順序。 */
export function itemTypeIssues(base: ProjectBlueprint, draft: SettingsDraft): readonly SettingsIssue[] {
  const target = (itemType: string) => ({ kind: 'itemType', itemType }) as const;
  const nameOf = (key: string) => {
    const type = draft.itemTypes.find((candidate) => candidate.key === key);
    return type === undefined ? key : displayName(type);
  };
  const names = draft.itemTypes.flatMap((type) => {
    const message = nameError(type.name);
    return message === undefined ? [] : [{ target: target(type.key), message }];
  });
  const edges = closingEdges(draft.itemTypes);
  const cycles = draft.itemTypes.flatMap((type) =>
    edges
      .filter(([from]) => from === type.key)
      .map(([, parent]) => ({ target: target(type.key), message: `「${nameOf(type.key)}」把「${nameOf(parent)}」當成上層，上層關係因此繞成一圈。` })),
  );
  const moves = requiredTypeMoves(base, draft).flatMap((move) => {
    const [first] = move.itemTypes;
    const at = { kind: 'typeMove', workflow: move.workflow, from: move.from.key } as const;
    return move.to !== '' || first === undefined
      ? []
      : [{ target: at, message: `請選擇「${nameOf(first)}」停在「${move.from.name}」的項目要搬到哪個狀態。` }];
  });
  // 一個類型都沒有的專案新增不了任何項目。伺服器沒有這條規則，前端先擋。
  const none = draft.itemTypes.length === 0 ? [{ target: null, message: '至少要留一個類型。' }] : [];
  return [...none, ...names, ...cycles, ...moves];
}

export function roleIssues(draft: SettingsDraft): readonly SettingsIssue[] {
  return draft.roles.flatMap((role) => {
    const message = nameError(role.name);
    return message === undefined ? [] : [{ target: { kind: 'role', role: role.key } as const, message }];
  });
}

/** 新類型的 key：和目前藍圖與草稿裡的類型都不重複。 */
export function itemTypeKeyError(base: ProjectBlueprint, draft: SettingsDraft, key: string): string | undefined {
  const format = keyFormatError(key);
  if (format !== undefined) return format;
  const taken = [...base.itemTypes, ...draft.itemTypes].some((type) => type.key === key);
  return taken ? `已經有 key 是 ${key} 的類型。` : undefined;
}

/** 新角色的 key：不能是內建角色；要刪除的角色的 key 也算用過（和狀態一樣）。 */
export function roleKeyError(base: ProjectBlueprint, draft: SettingsDraft, key: string): string | undefined {
  const format = keyFormatError(key);
  if (format !== undefined) return format;
  if (isBuiltInProjectRole(key)) return `「${key}」是內建角色的 key，自訂角色不能用。`;
  const taken = [...(base.roles ?? []), ...draft.roles].some((role) => role.key === key);
  return taken ? `已經有 key 是 ${key} 的角色。` : undefined;
}
