import { useMemo } from 'react';
import type { ItemTypeDefinition, ProjectBlueprint } from '../../api/blueprint';
import { isApiError } from '../../api/http';
import { useProjectItems } from '../../api/queries';
import type { Item, ItemQuery } from '../../api/types';
import { flattenItemPages } from '../item-table/itemCache';

/** 一頁 50 個，和表格一樣；照編號排，先建立的在前面。 */
const CHILD_PAGE_SIZE = 50;

/** 項目頁的下層清單用的條件：刪除與改類型的對話框讀同一份快取，不必再讀一次。 */
export function childItemsQuery(itemKey: string): ItemQuery {
  return { parent: itemKey, limit: CHILD_PAGE_SIZE };
}

/** 可以掛在這個類型底下的類型。 */
export function childTypesOf(blueprint: ProjectBlueprint, typeKey: string): readonly ItemTypeDefinition[] {
  return blueprint.itemTypes.filter((itemType) => (itemType.parents ?? []).includes(typeKey));
}

export interface KnownChildren {
  /** 已經載入的下層；可能只有第一頁。 */
  readonly children: readonly Item[];
  /** 還有沒載入的下層（只讀了第一頁）：children 只是一部分。 */
  readonly hasMore: boolean;
  /** 第一頁還沒讀回來，還不知道有沒有下層。不能有下層的類型不讀，一律是 false。 */
  readonly isLoading: boolean;
}

/**
 * 已經知道的下層項目。類型可以有下層時，項目頁的下層清單已經讀過同一份（同一個查詢鍵，不會再發請求）；
 * 不能有下層的類型不讀，真的有（藍圖改過）的話由伺服器擋下。
 */
export function useKnownChildren(item: Item, blueprint: ProjectBlueprint): KnownChildren {
  const canHaveChildren = childTypesOf(blueprint, item.type).length > 0;
  const result = useProjectItems(item.projectKey, childItemsQuery(item.key), canHaveChildren);
  const children = useMemo(() => flattenItemPages(result.data), [result.data]);
  return { children, hasMore: result.hasNextPage, isLoading: canHaveChildren && result.isPending };
}

const CHILD_PATH = 'children/';

/**
 * 伺服器擋下時 details 列出的下層編號（合約的 path 是 children/AGY-8）。
 * 開頭多一個斜線（驗證問題的 JSON Pointer 寫法）也認得。
 */
export function childKeysOf(error: unknown): readonly string[] {
  if (!isApiError(error)) return [];
  return error.details.flatMap((detail) => {
    const path = detail.path?.replace(/^\//, '') ?? '';
    return path.startsWith(CHILD_PATH) ? [path.slice(CHILD_PATH.length)] : [];
  });
}
