import { apiRequest } from './http';
import type { ItemTypeSummary } from './types';

/** 看得到、沒有封存的專案用到的項目類型，依 key 合併、照 key 排：跨專案的「類型」篩選的選項。 */
export function listItemTypes(signal?: AbortSignal): Promise<readonly ItemTypeSummary[]> {
  return apiRequest<readonly ItemTypeSummary[]>('/v1/item-types', { signal });
}
