import type { Automation, ProjectBlueprint } from '../api/blueprint';
import type { SettingsDraft } from './settingsDraft';

/**
 * 自動化用到的狀態、欄位與類型。設定頁還不能改自動化，刪除它用到的狀態、欄位或類型時伺服器一定擋下，
 * 所以在那一列先提醒（不擋送出，以伺服器為準）。
 */

const STATE = 'state';
const ITEM_TYPE = 'item_type';

/** 用到這個狀態的自動化：要轉去的狀態（transition 的 to），或條件看的狀態（field 是 state 的 in）。 */
export function automationsUsingState(base: ProjectBlueprint, stateKey: string): readonly Automation[] {
  return (base.automations ?? []).filter(
    (automation) =>
      automation.actions.some((action) => action.type === 'transition' && action.to === stateKey) ||
      (automation.conditions ?? []).some((condition) => condition.field === STATE && condition.in.includes(stateKey)),
  );
}

/** 用到這個欄位的自動化：要設定的欄位（set_field 的 field），或條件看的欄位。 */
export function automationsUsingField(base: ProjectBlueprint, fieldKey: string): readonly Automation[] {
  return (base.automations ?? []).filter(
    (automation) =>
      automation.actions.some((action) => action.type === 'set_field' && action.field === fieldKey) ||
      (automation.conditions ?? []).some((condition) => condition.field === fieldKey),
  );
}

/** 自動化看的是所有流程的狀態：別的流程還有同一個 key 的狀態時，刪掉這一個伺服器不會擋。 */
export function isStateKeyInDraft(draft: SettingsDraft, stateKey: string): boolean {
  return draft.workflows.some((workflow) => workflow.states.some((state) => state.key === stateKey));
}

function reminder(automations: readonly Automation[], subject: string): string {
  if (automations.length === 0) {
    return '';
  }
  const names = automations.map((automation) => `「${automation.name}」`).join('、');
  return `自動化${names}用到這個${subject}：刪除之後，送出會被伺服器擋下。自動化目前只能用變更集文件修改。`;
}

/** 要刪除的狀態的提醒；沒有就是空字串。 */
export function stateAutomationReminder(base: ProjectBlueprint, draft: SettingsDraft, stateKey: string): string {
  return isStateKeyInDraft(draft, stateKey) ? '' : reminder(automationsUsingState(base, stateKey), '狀態');
}

/** 要刪除的欄位的提醒；沒有就是空字串。 */
export function fieldAutomationReminder(base: ProjectBlueprint, fieldKey: string): string {
  return reminder(automationsUsingField(base, fieldKey), '欄位');
}

/** 用到這個類型的自動化：條件看的類型（field 是 item_type 的 in）。 */
export function automationsUsingItemType(base: ProjectBlueprint, typeKey: string): readonly Automation[] {
  return (base.automations ?? []).filter((automation) =>
    (automation.conditions ?? []).some((condition) => condition.field === ITEM_TYPE && condition.in.includes(typeKey)),
  );
}

/** 要刪除的類型的提醒；沒有就是空字串。伺服器會擋 automation.unknown_item_type。 */
export function itemTypeAutomationReminder(base: ProjectBlueprint, typeKey: string): string {
  return reminder(automationsUsingItemType(base, typeKey), '類型');
}
