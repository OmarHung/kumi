import type { Permission } from '../api/blueprint';

/**
 * 看得到這個專案的項目：全部（item.read）或只有自己負責、建立的（item.read_own，例如執行者）。
 * 進得了專案頁、規劃頁、已刪除的項目都看這個；看得到哪些由後端決定，網頁不用再濾。
 */
export function canReadItems(permissions: readonly Permission[]): boolean {
  return permissions.includes('item.read') || permissions.includes('item.read_own');
}

/** 看得到所有項目（不是只看自己的）：週期與里程碑這類整個專案的事，只有這樣的人能改。 */
export function readsAllItems(permissions: readonly Permission[]): boolean {
  return permissions.includes('item.read');
}
