import type { ProjectBlueprint, Workflow, WorkflowTransition } from '../api/blueprint';
import type { Change } from '../api/types';
import { nameIn, roleName, type ChangeSetBlueprints } from './changeSetNames';
import { CORE_FIELD_LABELS } from './fields';

/**
 * 變更集裡和轉換有關的句子：轉換的限制（允許的角色、要先填的欄位）、新增的轉換、從不限制切成限制時列出的每一條。
 * 伺服器在流程從不限制切成限制時只給「流程的 transitions 改了」，不逐條列出（逐條說成新增會誤導），
 * 核准的人要看得到切成限制之後能走哪些，所以從變更集的藍圖裡列出來。
 */

/** 切成限制的轉換不超過這麼多條時寫在同一句裡；再多就一條一行。 */
const INLINE_TRANSITION_LIMIT = 4;

export function quotedList(names: readonly string[]): string {
  return names.map((name) => `「${name}」`).join('、');
}

export function transitionIn(blueprint: ProjectBlueprint, change: Change): WorkflowTransition | undefined {
  const [from, to] = change.key.split('->');
  const workflow = blueprint.workflows.find((candidate) => candidate.key === change.parentKey);
  return workflow?.transitions?.find((transition) => transition.from === from && transition.to === to);
}

/** 欄位的名稱：核心欄位用中文，自訂欄位先看改之後的藍圖。 */
export function fieldName(context: ChangeSetBlueprints, key: string): string {
  return CORE_FIELD_LABELS[key] ?? nameIn(context.after, context.before, 'field', key);
}

/** 「限「編輯者」；要先填「說明」」；沒有限制時是空字串。 */
export function transitionLimitsText(transition: WorkflowTransition, context: ChangeSetBlueprints): string {
  const roles = (transition.allowedRoles ?? []).map((role) => roleName(context, role));
  const fields = (transition.requireFields ?? []).map((field) => fieldName(context, field));
  const parts = [roles.length > 0 ? `限${quotedList(roles)}` : '', fields.length > 0 ? `要先填${quotedList(fields)}` : ''];
  return parts.filter((part) => part !== '').join('；');
}

/** 新增的轉換後面接的限制：「，限「專案經理」；要先填「說明」」；沒有限制時是空字串。 */
export function addedTransitionSuffix(change: Change, context: ChangeSetBlueprints): string {
  const transition = transitionIn(context.after, change);
  const limits = transition === undefined ? '' : transitionLimitsText(transition, context);
  return limits === '' ? '' : `，${limits}`;
}

function workflowIn(blueprint: ProjectBlueprint, key: string): Workflow | undefined {
  return blueprint.workflows.find((workflow) => workflow.key === key);
}

/** 「「待辦」→「進行中」（限「編輯者」）」：狀態的名稱先看改之後的藍圖（可能是新的狀態）。 */
function transitionLine(transition: WorkflowTransition, workflowKey: string, context: ChangeSetBlueprints): string {
  const state = (key: string) => nameIn(context.after, context.before, 'state', key, workflowKey);
  const limits = transitionLimitsText(transition, context);
  const pair = `「${state(transition.from)}」→「${state(transition.to)}」`;
  return limits === '' ? pair : `${pair}（${limits}）`;
}

/** 流程從不限制切成限制時，改之後能走的每一條；不是這種修改時是 undefined。 */
function restrictionLines(change: Change, context: ChangeSetBlueprints): readonly string[] | undefined {
  const isStart = workflowIn(context.before, change.key)?.transitions === undefined;
  const transitions = workflowIn(context.after, change.key)?.transitions;
  if (!isStart || transitions === undefined) {
    return undefined;
  }
  return transitions.map((transition) => transitionLine(transition, change.key, context));
}

/** 流程的 transitions 改了：開始限制（列出能走的）、不再限制，或兩版都有列、只是換了順序。 */
export function workflowTransitionsText(change: Change, context: ChangeSetBlueprints): string {
  const had = workflowIn(context.before, change.key)?.transitions !== undefined;
  const has = workflowIn(context.after, change.key)?.transitions !== undefined;
  if (had === has) return '調整了轉換的順序';
  const lines = restrictionLines(change, context);
  if (lines === undefined) return '不再限制轉換';
  if (lines.length === 0) return '開始限制轉換，一條轉換都沒有';
  return lines.length > INLINE_TRANSITION_LIMIT
    ? `開始限制轉換，只能走下面這 ${String(lines.length)} 條`
    : `開始限制轉換，只能走${lines.join('、')}`;
}

/** 句子底下另外一行一條的內容：切成限制的轉換太多、寫在同一句裡讀不下去時。其他的修改沒有。 */
export function restrictionDetails(change: Change, context: ChangeSetBlueprints): readonly string[] {
  if (change.kind !== 'modified' || change.resource !== 'workflow' || !change.changedProperties.includes('transitions')) {
    return [];
  }
  const lines = restrictionLines(change, context) ?? [];
  return lines.length > INLINE_TRANSITION_LIMIT ? lines : [];
}
