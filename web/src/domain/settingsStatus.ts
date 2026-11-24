import type { FieldDefinition, ProjectBlueprint, WorkflowState } from '../api/blueprint';
import { sameName, sameSet, type DraftStatus, type FieldDraft, type SettingsDraft } from './settingsDraft';
import { removedFields } from './settingsChangeSet';
import { removedRoles, roleStatus } from './settingsRoles';
import { transitionChangeCount } from './settingsTransitions';
import { removedItemTypes } from './settingsTypeRemoval';
import { itemTypeStatus } from './settingsTypes';

/** 草稿和目前的藍圖比起來，每一列是什麼狀況：畫面要清楚標出「還沒送出的修改」。 */
export type { DraftStatus } from './settingsDraft';

export interface RemovedState {
  readonly state: WorkflowState;
  /** 項目要搬去的狀態；還沒選是空字串。 */
  readonly to: string;
}

function baseStates(base: ProjectBlueprint, workflowKey: string): readonly WorkflowState[] {
  return base.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
}

function draftStates(draft: SettingsDraft, workflowKey: string): readonly WorkflowState[] {
  return draft.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
}

/** 被刪除的狀態，照目前藍圖的順序。 */
export function removedStates(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string): readonly RemovedState[] {
  const workflow = draft.workflows.find((candidate) => candidate.key === workflowKey);
  return baseStates(base, workflowKey).flatMap((state) => {
    const move = workflow?.moves.find((candidate) => candidate.from === state.key);
    return move === undefined ? [] : [{ state, to: move.to }];
  });
}

export function stateStatus(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, stateKey: string): DraftStatus {
  const before = baseStates(base, workflowKey);
  const after = draftStates(draft, workflowKey);
  const original = before.find((state) => state.key === stateKey);
  const current = after.find((state) => state.key === stateKey);
  if (original === undefined) {
    return 'added';
  }
  if (current !== undefined && (!sameName(current.name, original.name) || current.category !== original.category)) {
    return 'modified';
  }
  const beforeKeys = before.map((state) => state.key).filter((key) => after.some((state) => state.key === key));
  const afterKeys = after.map((state) => state.key).filter((key) => before.some((state) => state.key === key));
  return beforeKeys.indexOf(stateKey) === afterKeys.indexOf(stateKey) ? 'unchanged' : 'moved';
}

function baseField(base: ProjectBlueprint, fieldKey: string): FieldDefinition | undefined {
  return base.fields.find((field) => field.key === fieldKey);
}

function baseTypes(base: ProjectBlueprint, fieldKey: string): readonly string[] {
  return base.itemTypes.filter((type) => type.fields?.includes(fieldKey)).map((type) => type.key);
}

function sameOptions(original: FieldDefinition, field: FieldDraft): boolean {
  const before = original.options ?? [];
  const after = field.options ?? [];
  return before.length === after.length && before.every((option, index) => sameName(option.name, after[index]?.name ?? ''));
}

export function fieldStatus(base: ProjectBlueprint, draft: SettingsDraft, fieldKey: string): DraftStatus {
  const original = baseField(base, fieldKey);
  const field = draft.fields.find((candidate) => candidate.key === fieldKey);
  if (original === undefined) {
    return 'added';
  }
  if (field === undefined) {
    return 'unchanged';
  }
  // 新類型套用了這個欄位算在新類型那一項，欄位本身不算修改。
  const existingTypes = field.appliesTo.filter((type) => base.itemTypes.some((candidate) => candidate.key === type));
  const isSame = sameName(field.name, original.name) && sameSet(existingTypes, baseTypes(base, fieldKey)) && sameOptions(original, field);
  return isSame ? 'unchanged' : 'modified';
}

export function optionStatus(base: ProjectBlueprint, fieldKey: string, optionKey: string, name: string): DraftStatus {
  const original = baseField(base, fieldKey)?.options?.find((option) => option.key === optionKey);
  if (original === undefined) {
    return 'added';
  }
  return sameName(original.name, name) ? 'unchanged' : 'modified';
}

/**
 * 還沒送出的修改有幾項：每個新增、改過、移動、要刪除的狀態、欄位與角色，每個新增、改過或要刪除的類型各算一項（選項的修改算在它的欄位）；
 * 轉換見 transitionChangeCount。類型換流程的搬移目標算在那個類型。
 */
export function pendingChangeCount(base: ProjectBlueprint, draft: SettingsDraft): number {
  const states = draft.workflows.reduce((count, workflow) => {
    const changed = workflow.states.filter((state) => stateStatus(base, draft, workflow.key, state.key) !== 'unchanged');
    return count + changed.length + removedStates(base, draft, workflow.key).length;
  }, 0);
  const fields = draft.fields.filter((field) => fieldStatus(base, draft, field.key) !== 'unchanged').length;
  const types =
    draft.itemTypes.filter((type) => itemTypeStatus(base, draft, type.key) !== 'unchanged').length + removedItemTypes(base, draft).length;
  const roles = draft.roles.filter((role) => roleStatus(base, draft, role.key) !== 'unchanged').length + removedRoles(base, draft).length;
  return states + transitionChangeCount(base, draft) + fields + removedFields(base, draft).length + types + roles;
}
