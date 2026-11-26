import type { Item } from '../api/types';

/** 假後端的 createdBy 條件：沒帶是不限，`me` 換成目前登入的人，其他是人員編號。 */
export function matchesCreatedBy(item: Item, createdBy: string | null, me: string): boolean {
  return createdBy === null || item.createdBy.personId === (createdBy === 'me' ? me : createdBy);
}
