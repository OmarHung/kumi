import type { ProjectBlueprint } from '../../api/blueprint';
import type { DeletedItemSummary } from '../../api/itemRecovery';
import { findItemType } from '../../domain/blueprint';

export const UNKNOWN_DELETER = '未記錄';

/** 類型的名稱；藍圖已經沒有這個類型時是 key 加「（已移除的類型）」（復原會被擋下，這裡先說清楚）。 */
export function deletedItemTypeLabel(blueprint: ProjectBlueprint, item: DeletedItemSummary): string {
  const itemType = findItemType(blueprint, item.type);
  return itemType === undefined ? `${item.type}（已移除的類型）` : itemType.name;
}

/** 復原按鈕的名稱：帶編號，同一頁好幾顆才分得出來。 */
export function restoreLabel(item: DeletedItemSummary): string {
  return `復原 ${item.key}`;
}
