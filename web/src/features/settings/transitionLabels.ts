import type { ProjectBlueprint, WorkflowState, WorkflowTransition } from '../../api/blueprint';
import { CORE_FIELD_LABELS } from '../../domain/fields';
import type { SettingsDraft } from '../../domain/settingsDraft';
import { displayName } from '../../domain/settingsNames';
import { assignableRoles, canTransitionItems } from '../../domain/settingsRoles';
import { REQUIRABLE_CORE_FIELDS } from '../../domain/settingsTransitions';
import { CORE_FIELD_KEYS } from '../../domain/settingsValidation';
import type { ChecklistOption } from './Checklist';

/**
 * 轉換在畫面上的說法。只列草稿裡還在的角色與欄位：被刪除的送出時會拿掉，畫面上也不顯示。
 * 顯示的文字（表頭、問題）用草稿的名稱；操作的名稱（勾選框、按鈕）用目前藍圖裡的原名，和狀態那一列一樣，打字時不跟著變。
 */

/** 顯示用：草稿裡的名稱，空的時候用 key（不然表頭是空白，看不出是哪一個）。 */
export function stateLabel(states: readonly WorkflowState[], key: string): string {
  const state = states.find((candidate) => candidate.key === key);
  return state === undefined ? key : displayName(state);
}

/** 操作的名稱：目前藍圖裡的原名；新的狀態用 key。 */
export function originalStateName(base: ProjectBlueprint, workflowKey: string, stateKey: string): string {
  const workflow = base.workflows.find((candidate) => candidate.key === workflowKey);
  return workflow?.states.find((state) => state.key === stateKey)?.name ?? stateKey;
}

/** 專案有兩個以上的流程時，操作的名稱後面帶「（流程名）」，才分得出是哪一張表格的。 */
export function workflowNameSuffix(base: ProjectBlueprint, workflowKey: string): string {
  if (base.workflows.length < 2) return '';
  return `（${base.workflows.find((workflow) => workflow.key === workflowKey)?.name ?? workflowKey}）`;
}

/** 「從「待辦」到「進行中」」，給勾選框；「設定」按鈕在中間另外加字，所以流程的名稱分開給。 */
export function pairName(base: ProjectBlueprint, workflowKey: string, from: string, to: string): string {
  return `從「${originalStateName(base, workflowKey, from)}」到「${originalStateName(base, workflowKey, to)}」`;
}

/** 限制對話框的角色：沒有「改變狀態」權限的在名稱後面標出來（只允許它們的話，沒有人走得了這條轉換）。 */
export function roleOptions(draft: SettingsDraft, marksBlocked = false): readonly ChecklistOption[] {
  return assignableRoles(draft).map((role) => ({
    key: role.key,
    label: marksBlocked && !canTransitionItems(draft, role.key) ? `${displayName(role)}（不能改變狀態）` : displayName(role),
  }));
}

/**
 * 要先填的欄位能選的：說明、上層項目，加上 required 裡的其他核心欄位（用變更集文件寫的「到期日」等，要看得到才拿得掉），
 * 再接草稿裡還在的自訂欄位。
 */
export function requirableFieldOptions(draft: SettingsDraft, required: readonly string[] = []): readonly ChecklistOption[] {
  const others = [...CORE_FIELD_KEYS].filter((key) => !REQUIRABLE_CORE_FIELDS.includes(key) && required.includes(key));
  const core = [...REQUIRABLE_CORE_FIELDS, ...others].map((key) => ({ key, label: CORE_FIELD_LABELS[key] ?? key }));
  return [...core, ...draft.fields.map((field) => ({ key: field.key, label: displayName(field) }))];
}

function visible(options: readonly ChecklistOption[], keys: readonly string[] | undefined): readonly ChecklistOption[] {
  return options.filter((option) => keys?.includes(option.key));
}

export interface TransitionLimits {
  readonly roles: readonly ChecklistOption[];
  readonly fields: readonly ChecklistOption[];
}

export function transitionLimits(draft: SettingsDraft, transition: WorkflowTransition): TransitionLimits {
  return {
    roles: visible(roleOptions(draft), transition.allowedRoles),
    fields: visible(requirableFieldOptions(draft, transition.requireFields), transition.requireFields),
  };
}

/** 「限「專案經理」；要先填「客戶驗收日」」；沒有限制時是空字串。 */
export function limitsText({ roles, fields }: TransitionLimits): string {
  const quoted = (options: readonly ChecklistOption[]) => options.map((option) => `「${option.label}」`).join('、');
  const parts = [roles.length > 0 ? `限${quoted(roles)}` : '', fields.length > 0 ? `要先填${quoted(fields)}` : ''];
  return parts.filter((part) => part !== '').join('；');
}
