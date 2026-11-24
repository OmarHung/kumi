import type { ItemTypeDefinition, ProjectBlueprint } from '../api/blueprint';
import { byKey, restoreInOrder, typeDraftOf, type SettingsDraft } from './settingsDraft';

/**
 * 設定頁刪除類型。目前藍圖裡就有的類型，引用（其他類型的上層、欄位的套用）留在草稿裡，組變更集時才拿掉，
 * 復原時跟著回來；剛新增、還沒送出的連引用一起拿掉（之後用同一個 key 新增，舊的引用不會跑回來）。
 * 類型的 fields 跟著類型消失；檢視的類型篩選在 settingsChangeSet.ts 拿掉；自動化不改，只提醒（settingsAutomations.ts）。
 * 還有這種項目時伺服器的預覽會擋下（item_type_removal），設定頁不知道數量。
 */

export function removeItemType(base: ProjectBlueprint, draft: SettingsDraft, typeKey: string): SettingsDraft {
  const removed = { ...draft, itemTypes: draft.itemTypes.filter((type) => type.key !== typeKey) };
  if (base.itemTypes.some((type) => type.key === typeKey)) {
    return removed;
  }
  return {
    ...removed,
    itemTypes: removed.itemTypes.map((type) =>
      type.parents.includes(typeKey) ? { ...type, parents: type.parents.filter((parent) => parent !== typeKey) } : type,
    ),
    fields: removed.fields.map((field) =>
      field.appliesTo.includes(typeKey) ? { ...field, appliesTo: field.appliesTo.filter((key) => key !== typeKey) } : field,
    ),
  };
}

/** 復原刪除的類型：名稱、流程與上層回到目前藍圖裡的樣子，放回原本的位置。 */
export function restoreItemType(base: ProjectBlueprint, draft: SettingsDraft, typeKey: string): SettingsDraft {
  const original = base.itemTypes.map(typeDraftOf);
  const type = original.find((candidate) => candidate.key === typeKey);
  return type === undefined ? draft : { ...draft, itemTypes: restoreInOrder(draft.itemTypes, original, type, byKey) };
}

/** 目前藍圖裡有、草稿裡沒有的類型，照目前藍圖的順序。 */
export function removedItemTypes(base: ProjectBlueprint, draft: SettingsDraft): readonly ItemTypeDefinition[] {
  return base.itemTypes.filter((type) => !draft.itemTypes.some((candidate) => candidate.key === type.key));
}

/** 草稿裡還在的類型 key：要刪除的類型不算。 */
export function remainingTypeKeys(draft: SettingsDraft): ReadonlySet<string> {
  return new Set(draft.itemTypes.map((type) => type.key));
}

/**
 * 刪除類型時會一起拿掉的引用。becomesEmpty：拿掉要刪除的類型之後清單變空
 * （那個類型不能再掛在任何類型底下；檢視不再篩選類型，範圍變寬）。
 */
export type ItemTypeReference =
  | { readonly kind: 'parent'; readonly itemType: string; readonly becomesEmpty: boolean }
  | { readonly kind: 'view'; readonly view: string; readonly becomesEmpty: boolean };

/** 用到這個類型的其他類型（草稿裡還在的，照草稿的順序）與檢視的類型篩選（照目前藍圖的順序）。 */
export function itemTypeReferences(base: ProjectBlueprint, draft: SettingsDraft, typeKey: string): readonly ItemTypeReference[] {
  const remaining = remainingTypeKeys(draft);
  const parents = draft.itemTypes
    .filter((type) => type.parents.includes(typeKey))
    .map((type) => ({ kind: 'parent' as const, itemType: type.key, becomesEmpty: !type.parents.some((parent) => remaining.has(parent)) }));
  const views = (base.views ?? [])
    .filter((view) => view.filter?.itemTypes?.includes(typeKey))
    .map((view) => ({
      kind: 'view' as const,
      view: view.key,
      becomesEmpty: !(view.filter?.itemTypes ?? []).some((key) => remaining.has(key)),
    }));
  return [...parents, ...views];
}
