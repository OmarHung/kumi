import { FIELD_PARAM_PREFIX } from '../domain/fieldFilters';
import { apiRequest } from './http';
import type {
  ChangeTypeRequest,
  DeletedItem,
  ItemDetail,
  ItemPage,
  ItemPatch,
  ItemQuery,
  NewItemRequest,
  TransitionRequest,
} from './types';

export function itemResourcePath(itemKey: string): string {
  return `/v1/items/${encodeURIComponent(itemKey)}`;
}

function queryString(query: ItemQuery): string {
  const params = new URLSearchParams();
  const appendAll = (name: string, values: readonly string[] | undefined) => {
    values?.forEach((value) => {
      params.append(name, value);
    });
  };
  appendAll('type', query.type);
  appendAll('state', query.state);
  appendAll('stateCategory', query.stateCategory);
  appendAll('priority', query.priority);
  if (query.dueFrom !== undefined) params.append('dueFrom', query.dueFrom);
  if (query.dueTo !== undefined) params.append('dueTo', query.dueTo);
  if (query.datesFrom !== undefined) params.append('datesFrom', query.datesFrom);
  if (query.datesTo !== undefined) params.append('datesTo', query.datesTo);
  if (query.noDue === true) params.append('noDue', 'true');
  if (query.cycleId !== undefined) params.append('cycle', query.cycleId);
  if (query.noCycle === true) params.append('cycle', 'none');
  if (query.milestoneId !== undefined) params.append('milestone', query.milestoneId);
  if (query.noMilestone === true) params.append('milestone', 'none');
  if (query.assignee !== undefined) params.append('assignee', query.assignee);
  if (query.q !== undefined && query.q !== '') params.append('q', query.q);
  // 自訂欄位：同一個欄位的多個值重複帶（是「或」），不同欄位是「而且」。
  Object.entries(query.fields ?? {}).forEach(([key, values]) => {
    appendAll(`${FIELD_PARAM_PREFIX}${key}`, values);
  });
  appendAll('project', query.project);
  if (query.parent !== undefined) params.append('parent', query.parent);
  if (query.sort !== undefined) params.append('sort', `${query.sort.field}:${query.sort.direction}`);
  if (query.limit !== undefined) params.append('limit', String(query.limit));
  if (query.cursor !== undefined) params.append('cursor', query.cursor);
  const text = params.toString();
  return text === '' ? '' : `?${text}`;
}

export function listItems(
  projectKey: string,
  query: ItemQuery = {},
  signal?: AbortSignal,
): Promise<ItemPage> {
  const path = `/v1/projects/${encodeURIComponent(projectKey)}/items${queryString(query)}`;
  return apiRequest<ItemPage>(path, { signal });
}

/**
 * 跨專案的項目清單（「我的工作」的四種版面，internal/HANDOFF-my-work-views.md 第 4 節）：看得到、沒有封存的專案裡符合條件的項目。
 * 條件與分頁和 listItems 一樣，另外收 project（只看這幾個專案）。排序的 number 是依專案代號再依編號，state 是依狀態類別。
 */
export function listItemsAcrossProjects(query: ItemQuery = {}, signal?: AbortSignal): Promise<ItemPage> {
  return apiRequest<ItemPage>(`/v1/items${queryString(query)}`, { signal });
}

/** 成功時回 201。欄位的問題放在錯誤的 details，path 是欄位的 key（核心欄位用 due_date 這類名字）。 */
export function createItem(projectKey: string, request: NewItemRequest): Promise<ItemDetail> {
  return apiRequest<ItemDetail>(`/v1/projects/${encodeURIComponent(projectKey)}/items`, {
    method: 'POST',
    body: request,
  });
}

/** 看不到或不存在的項目丟出 404 `item.not_found`。 */
export function getItem(itemKey: string, signal?: AbortSignal): Promise<ItemDetail> {
  return apiRequest<ItemDetail>(itemResourcePath(itemKey), { signal });
}

/** 只送 patch 裡有寫的屬性；JSON.stringify 會略過 undefined，null 照樣送出。 */
export function updateItem(itemKey: string, patch: ItemPatch): Promise<ItemDetail> {
  return apiRequest<ItemDetail>(itemResourcePath(itemKey), { method: 'PATCH', body: patch });
}

/**
 * 改狀態。被流程擋下時的代碼：item.transition_not_allowed、item.transition_role_required
 * （details 列允許的角色）、item.transition_fields_required（details 列缺的欄位）。
 */
export function transitionItem(itemKey: string, request: TransitionRequest): Promise<ItemDetail> {
  return apiRequest<ItemDetail>(`${itemResourcePath(itemKey)}/transitions`, { method: 'POST', body: request });
}

/**
 * 刪除（封存）項目：之後讀它一律 404，清單與我的工作都不再出現；活動紀錄留著。
 * 還有沒刪除的下層時是 409 item.has_children，details 每個下層一筆（path 是 children/下層的編號）。
 */
export function deleteItem(itemKey: string): Promise<DeletedItem> {
  return apiRequest<DeletedItem>(itemResourcePath(itemKey), { method: 'DELETE' });
}

/**
 * 改類型。狀態由伺服器決定（新流程有同 key 就留著，否則第一個同類別的，再沒有就第一個），新類型沒有的欄位值封存。
 * 擋下時的代碼：item.unknown_type（path type）、item.parent_not_allowed（path parent）、
 * item.children_not_allowed（details 每個不允許的下層一筆，path 是 children/下層的編號）。
 */
export function changeItemType(itemKey: string, request: ChangeTypeRequest): Promise<ItemDetail> {
  return apiRequest<ItemDetail>(`${itemResourcePath(itemKey)}/type`, { method: 'POST', body: request });
}
