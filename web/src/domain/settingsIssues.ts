import type { ProjectBlueprint } from '../api/blueprint';
import type { ChangeSetRequest } from '../api/types';
import type { SettingsDraft } from './settingsDraft';
import { requiredTypeMoves } from './settingsTypes';

/**
 * 設定頁上可以標出問題的地方：一個流程、一個狀態（包含要刪除的）、一條轉換、一個欄位（包含要刪除的）、一個選項、
 * 一個類型、類型換流程的一條搬移（新流程, 舊狀態；共用它的每個類型列上的選單都標）、一個角色（包含要刪除的）。
 */
export type SettingsTarget =
  | { readonly kind: 'workflow'; readonly workflow: string }
  | { readonly kind: 'state'; readonly workflow: string; readonly state: string }
  | { readonly kind: 'transition'; readonly workflow: string; readonly from: string; readonly to: string }
  | { readonly kind: 'field'; readonly field: string }
  | { readonly kind: 'option'; readonly field: string; readonly option: string }
  | { readonly kind: 'itemType'; readonly itemType: string }
  | { readonly kind: 'typeMove'; readonly workflow: string; readonly from: string }
  | { readonly kind: 'role'; readonly role: string };

export interface SettingsIssue {
  /** null 是對不到設定頁上任何一列的問題（例如自動化裡的引用），放在送出列的總表。 */
  readonly target: SettingsTarget | null;
  readonly message: string;
}

/** 同一個位置的固定寫法：拿來比對、也拿來組元素的 id（key 只有小寫字母、數字與底線）。 */
export function targetId(target: SettingsTarget): string {
  switch (target.kind) {
    case 'workflow':
      return `workflow-${target.workflow}`;
    case 'state':
      return `state-${target.workflow}-${target.state}`;
    case 'transition':
      return `transition-${target.workflow}-${target.from}-${target.to}`;
    case 'field':
      return `field-${target.field}`;
    case 'option':
      return `option-${target.field}-${target.option}`;
    case 'itemType':
      return `itemType-${target.itemType}`;
    case 'typeMove':
      return `typeMove-${target.workflow}-${target.from}`;
    case 'role':
      return `role-${target.role}`;
  }
}

export function issuesAt(issues: readonly SettingsIssue[], target: SettingsTarget): readonly string[] {
  const id = targetId(target);
  return issues.flatMap((issue) => (issue.target !== null && targetId(issue.target) === id ? [issue.message] : []));
}

const INDEX = /^\d+$/;

function at<T>(list: readonly T[] | undefined, segment: string | undefined): T | undefined {
  return segment !== undefined && INDEX.test(segment) ? list?.[Number(segment)] : undefined;
}

function locateWorkflow(request: ChangeSetRequest, rest: readonly string[]): SettingsTarget | null {
  const [index, list, item] = rest;
  const workflow = at(request.blueprint.workflows, index);
  if (workflow === undefined) return null;
  const transition = list === 'transitions' ? at(workflow.transitions, item) : undefined;
  if (transition !== undefined) {
    return { kind: 'transition', workflow: workflow.key, from: transition.from, to: transition.to };
  }
  const state = list === 'states' ? at(workflow.states, item) : undefined;
  return state === undefined
    ? { kind: 'workflow', workflow: workflow.key }
    : { kind: 'state', workflow: workflow.key, state: state.key };
}

function locateField(request: ChangeSetRequest, rest: readonly string[]): SettingsTarget | null {
  const [index, list, item] = rest;
  const field = at(request.blueprint.fields, index);
  if (field === undefined) return null;
  const option = list === 'options' ? at(field.options, item) : undefined;
  return option === undefined ? { kind: 'field', field: field.key } : { kind: 'option', field: field.key, option: option.key };
}

/** 類型的 fields 標在那個欄位（欄位那一列才改得了套用的類型）；其他的（名稱、流程、上層）標在類型。 */
function locateItemType(request: ChangeSetRequest, rest: readonly string[]): SettingsTarget | null {
  const [index, list, item] = rest;
  const type = at(request.blueprint.itemTypes, index);
  if (type === undefined) return null;
  const field = list === 'fields' ? at(type.fields, item) : undefined;
  return field === undefined ? { kind: 'itemType', itemType: type.key } : { kind: 'field', field };
}

function locateRole(request: ChangeSetRequest, rest: readonly string[]): SettingsTarget | null {
  const role = at(request.blueprint.roles, rest[0]);
  return role === undefined ? null : { kind: 'role', role: role.key };
}

/** 目前藍圖裡的類型（包含要刪除的）才標得上：要刪除的類型那一列也會顯示問題。 */
function typeTargetOf(typeKey: string | undefined, context: IssueContext | undefined): SettingsTarget | null {
  const isKnown = typeKey !== undefined && context?.base.itemTypes.some((type) => type.key === typeKey) === true;
  return isKnown ? { kind: 'itemType', itemType: typeKey } : null;
}

/** 自動化的條件用到不存在的類型（automation.unknown_item_type，路徑 …/conditions/C/in/V）：標在那個類型，通常是要刪除的那一個。 */
function locateAutomation(request: ChangeSetRequest, rest: readonly string[], context: IssueContext | undefined): SettingsTarget | null {
  const [index, list, conditionIndex, values, valueIndex] = rest;
  const automation = at(request.blueprint.automations, index);
  const condition = list === 'conditions' ? at(automation?.conditions, conditionIndex) : undefined;
  if (condition?.field !== 'item_type' || values !== 'in') return null;
  return typeTargetOf(at(condition.in, valueIndex), context);
}

/** 檢視篩選的類型（…/filter/itemTypes/K）標在那個類型；檢視的其他問題設定頁改不了，列在「其他問題」。 */
function locateView(request: ChangeSetRequest, rest: readonly string[], context: IssueContext | undefined): SettingsTarget | null {
  const [index, section, list, item] = rest;
  const view = at(request.blueprint.views, index);
  if (section !== 'filter' || list !== 'itemTypes') return null;
  return typeTargetOf(at(view?.filter?.itemTypes, item), context);
}

/** 草稿與它的基準：分辨一條搬移是刪除狀態的、還是類型換流程的。 */
export interface IssueContext {
  readonly base: ProjectBlueprint;
  readonly draft: SettingsDraft;
}

/** 刪除狀態的搬移標在那個狀態；類型換流程的標在那條搬移（每個共用它的類型列上的選單）。 */
function locateMove(workflow: string, from: string, context: IssueContext | undefined): SettingsTarget {
  const deleted = context?.draft.workflows.find((candidate) => candidate.key === workflow)?.moves.some((move) => move.from === from) ?? true;
  const isTypeMove =
    !deleted &&
    context !== undefined &&
    requiredTypeMoves(context.base, context.draft).some((move) => move.workflow === workflow && move.from.key === from);
  return isTypeMove ? { kind: 'typeMove', workflow, from } : { kind: 'state', workflow, state: from };
}

function locateMigration(request: ChangeSetRequest, rest: readonly string[], context: IssueContext | undefined): SettingsTarget | null {
  const [list, index] = rest;
  if (list === 'moveStates') {
    const move = at(request.migrations?.moveStates, index);
    return move === undefined ? null : locateMove(move.workflow, move.from, context);
  }
  const field = list === 'dropFields' ? at(request.migrations?.dropFields, index) : undefined;
  return field === undefined ? null : { kind: 'field', field };
}

/**
 * 伺服器回的問題（changeset.invalid 的 details）指向送出的文件（JSON Pointer，例如 /blueprint/workflows/0/states/2）。
 * 送出的文件是由草稿組出來的，照著路徑在文件裡找到那個狀態、轉換、欄位、類型或角色的 key，就能標在設定頁對應的那一列。
 * 轉換的問題（包含要先填的欄位、允許的角色）標在那條轉換上：轉換在設定頁改得了。
 * 自動化與檢視裡用到的類型標在那個類型（刪除類型時最常見）；其他自動化、檢視的問題對不到，列在「其他問題」。
 */
export function locateIssue(path: string | null, request: ChangeSetRequest, context?: IssueContext): SettingsTarget | null {
  const [root, section, ...rest] = (path ?? '').split('/').slice(1);
  if (root === 'migrations' && section !== undefined) {
    return locateMigration(request, [section, ...rest], context);
  }
  if (root !== 'blueprint') {
    return null;
  }
  switch (section) {
    case 'workflows':
      return locateWorkflow(request, rest);
    case 'fields':
      return locateField(request, rest);
    case 'itemTypes':
      return locateItemType(request, rest);
    case 'roles':
      return locateRole(request, rest);
    case 'automations':
      return locateAutomation(request, rest, context);
    case 'views':
      return locateView(request, rest, context);
    default:
      return null;
  }
}
