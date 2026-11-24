import { apiRequest } from './http';
import type { MyWork, ProjectSearchHit } from './types';

/**
 * 跨專案搜尋項目（標題或編號）：在呼叫者看得到的所有專案裡找，伺服器排好順序
 * （編號完全符合、標題開頭、標題包含；同一層最近更新的在前）。回應的形狀和「我的工作」一樣，只有 items。
 */
export function searchItems(text: string, limit: number, signal?: AbortSignal): Promise<MyWork> {
  const params = new URLSearchParams({ q: text, limit: String(limit) });
  return apiRequest<MyWork>(`/v1/search/items?${params.toString()}`, {
    signal,
  });
}

/**
 * 搜尋專案的代號、名稱與說明（找「某台主機、某個網址上有哪些專案」）：伺服器照權限只找看得到的專案、
 * 排好順序，每一筆帶說明裡比對到的那一行摘錄。limit 最多 20，超過或 q 太長、含控制字元是 422。
 */
export function searchProjects(text: string, limit: number, signal?: AbortSignal): Promise<readonly ProjectSearchHit[]> {
  const params = new URLSearchParams({ q: text, limit: String(limit) });
  return apiRequest<readonly ProjectSearchHit[]>(`/v1/search/projects?${params.toString()}`, { signal });
}
