import { apiRequest } from './http';
import type { SavedView, SavedViewDraft } from './types';

function savedViewsPath(projectKey: string): string {
  return `/v1/projects/${encodeURIComponent(projectKey)}/saved-views`;
}

/**
 * 自己在這個專案的檢視，依名稱排序。只給網頁用（存取權杖打不進來）。
 * 錯誤代碼：404 project.not_found、404 saved_view.not_found、409 saved_view.name_taken、
 * 409 saved_view.limit_reached；欄位的問題在 details，path 是 name、layout、query.due……。
 */
export function listSavedViews(projectKey: string, signal?: AbortSignal): Promise<readonly SavedView[]> {
  return apiRequest<readonly SavedView[]>(savedViewsPath(projectKey), { signal });
}

export function createSavedView(projectKey: string, draft: SavedViewDraft): Promise<SavedView> {
  return apiRequest<SavedView>(savedViewsPath(projectKey), { method: 'POST', body: draft });
}

/** 整個換掉名稱、版面與條件：「更新成目前的條件」與「重新命名」都送完整的一份。 */
export function replaceSavedView(projectKey: string, viewId: string, draft: SavedViewDraft): Promise<SavedView> {
  return apiRequest<SavedView>(`${savedViewsPath(projectKey)}/${encodeURIComponent(viewId)}`, { method: 'PUT', body: draft });
}

export function deleteSavedView(projectKey: string, viewId: string): Promise<unknown> {
  return apiRequest<unknown>(`${savedViewsPath(projectKey)}/${encodeURIComponent(viewId)}`, { method: 'DELETE' });
}

const MY_WORK_VIEWS_PATH = '/v1/my-work/saved-views';

/**
 * 自己的「我的工作」檢視，依名稱排序（internal/HANDOFF-my-work-views.md 第 4.3 節）。和專案的檢視同一套錯誤代碼，
 * 只是沒有 project.not_found；條件的意思不同（SavedViewQuery 的 category、project、group 與 assignee）。
 */
export function listMyWorkViews(signal?: AbortSignal): Promise<readonly SavedView[]> {
  return apiRequest<readonly SavedView[]>(MY_WORK_VIEWS_PATH, { signal });
}

export function createMyWorkView(draft: SavedViewDraft): Promise<SavedView> {
  return apiRequest<SavedView>(MY_WORK_VIEWS_PATH, { method: 'POST', body: draft });
}

export function replaceMyWorkView(viewId: string, draft: SavedViewDraft): Promise<SavedView> {
  return apiRequest<SavedView>(`${MY_WORK_VIEWS_PATH}/${encodeURIComponent(viewId)}`, { method: 'PUT', body: draft });
}

export function deleteMyWorkView(viewId: string): Promise<unknown> {
  return apiRequest<unknown>(`${MY_WORK_VIEWS_PATH}/${encodeURIComponent(viewId)}`, { method: 'DELETE' });
}
