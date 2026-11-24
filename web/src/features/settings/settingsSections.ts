import type { ProjectBlueprint } from '../../api/blueprint';
import { removedFields } from '../../domain/settingsChangeSet';
import type { SettingsDraft } from '../../domain/settingsDraft';
import type { SettingsIssue, SettingsTarget } from '../../domain/settingsIssues';
import { removedRoles, roleStatus } from '../../domain/settingsRoles';
import { fieldStatus, removedStates, stateStatus } from '../../domain/settingsStatus';
import { transitionChangeCount } from '../../domain/settingsTransitions';
import { removedItemTypes } from '../../domain/settingsTypeRemoval';
import { itemTypeStatus } from '../../domain/settingsTypes';
import { SETTINGS_SECTIONS, type SettingsSection } from '../work-items/itemLinks';

/** 分頁上的名稱，也是送出列「流程 2 個」的說法。 */
export const SECTION_LABELS: Readonly<Record<SettingsSection, string>> = {
  workflows: '流程',
  types: '類型',
  fields: '欄位',
  roles: '角色',
};

export type SectionCounts = Readonly<Record<SettingsSection, number>>;

/** 區塊標題（h2）的 id：區塊用 aria-labelledby 連過去；從送出列連過來時焦點放在這裡。 */
export function sectionHeadingId(idPrefix: string, section: SettingsSection): string {
  return `${idPrefix}-section-${section}`;
}

function workflowChanges(base: ProjectBlueprint, draft: SettingsDraft): number {
  const states = draft.workflows.reduce((count, workflow) => {
    const changed = workflow.states.filter((state) => stateStatus(base, draft, workflow.key, state.key) !== 'unchanged');
    return count + changed.length + removedStates(base, draft, workflow.key).length;
  }, 0);
  return states + transitionChangeCount(base, draft);
}

/**
 * 每個區塊還沒送出的修改有幾項。計法和送出列的總數（domain/settingsStatus.ts 的 pendingChangeCount）一樣，
 * 只是照區塊分開：狀態與轉換在流程，類型換流程的搬移算在類型。四個加起來就是總數（有測試看著）。
 */
export function sectionChangeCounts(base: ProjectBlueprint, draft: SettingsDraft): SectionCounts {
  const types = draft.itemTypes.filter((type) => itemTypeStatus(base, draft, type.key) !== 'unchanged').length;
  const fields = draft.fields.filter((field) => fieldStatus(base, draft, field.key) !== 'unchanged').length;
  const roles = draft.roles.filter((role) => roleStatus(base, draft, role.key) !== 'unchanged').length;
  return {
    workflows: workflowChanges(base, draft),
    types: types + removedItemTypes(base, draft).length,
    fields: fields + removedFields(base, draft).length,
    roles: roles + removedRoles(base, draft).length,
  };
}

/** 問題標在哪一列，那一列就在哪個區塊：類型換流程的搬移顯示在類型那一列，選項在欄位那一列。 */
export function issueSection(target: SettingsTarget): SettingsSection {
  switch (target.kind) {
    case 'workflow':
    case 'state':
    case 'transition':
      return 'workflows';
    case 'itemType':
    case 'typeMove':
      return 'types';
    case 'field':
    case 'option':
      return 'fields';
    case 'role':
      return 'roles';
  }
}

/**
 * 問題分布在哪裡，接在送出列的說明（alert）後面：「流程 1 個、角色 1 個」。問題可能全在目前沒有畫出來的區塊，
 * 報讀軟體只聽到「請看標示的地方」是找不到的。照分頁的順序，再接摘要與「其他問題」（對不到任何一列的）。
 */
export function issueBreakdown(issues: readonly SettingsIssue[], hasSummaryProblem: boolean): string {
  const counts = sectionIssueCounts(issues);
  const unplaced = issues.filter((issue) => issue.target === null).length;
  const parts = [
    ...SETTINGS_SECTIONS.filter((section) => counts[section] > 0).map((section) => `${SECTION_LABELS[section]} ${String(counts[section])} 個`),
    ...(hasSummaryProblem ? ['摘要 1 個'] : []),
    ...(unplaced > 0 ? [`其他問題 ${String(unplaced)} 個`] : []),
  ];
  return parts.join('、');
}

/** 每個區塊要修正的問題有幾個。對不到任何一列的（送出列的「其他問題」）不算在任何區塊。 */
export function sectionIssueCounts(issues: readonly SettingsIssue[]): SectionCounts {
  const counts = { workflows: 0, types: 0, fields: 0, roles: 0 };
  return issues.reduce<SectionCounts>((current, issue) => {
    if (issue.target === null) return current;
    const section = issueSection(issue.target);
    return { ...current, [section]: current[section] + 1 };
  }, counts);
}
