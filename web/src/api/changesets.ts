import { apiRequest } from './http';
import type { ChangeSet, ChangeSetDetail, ChangeSetRequest } from './types';

function changeSetsPath(projectKey: string): string {
  return `/v1/projects/${encodeURIComponent(projectKey)}/changesets`;
}

function changeSetPath(projectKey: string, number: number): string {
  return `${changeSetsPath(projectKey)}/${String(number)}`;
}

/**
 * 送出變更集，成功時回 201 與它的內容（差異、資料影響、能不能套用）。送出時就存成等待核准。
 * 驗證或計畫有問題時是 422 `changeset.invalid`，details 是每一個問題，path 指向送出的文件
 * （摘要超過 500 字是 `/summary`；基準版本已經不是目前的版本是 `plan.base_version_mismatch`，路徑 `/baseVersion`）。
 * 沒有 blueprint.propose 是 403 `work.forbidden`。
 */
export function proposeChangeSet(projectKey: string, request: ChangeSetRequest): Promise<ChangeSetDetail> {
  return apiRequest<ChangeSetDetail>(changeSetsPath(projectKey), { method: 'POST', body: request });
}

/** 專案的變更集，新的在前。需要 blueprint.read。 */
export function listChangeSets(projectKey: string, signal?: AbortSignal): Promise<readonly ChangeSet[]> {
  return apiRequest<readonly ChangeSet[]>(changeSetsPath(projectKey), { signal });
}

/** 一份變更集；等待核准的會依目前的資料重算影響與擋住的原因。找不到是 404 `changeset.not_found`。 */
export function getChangeSet(projectKey: string, number: number, signal?: AbortSignal): Promise<ChangeSetDetail> {
  return apiRequest<ChangeSetDetail>(changeSetPath(projectKey, number), { signal });
}

/**
 * 核准並套用。409 有四種：`changeset.not_pending`（已經不是等待核准）、`changeset.outdated`（別的變更先套用了）、
 * `changeset.apply_blocked`（資料擋住，details 是每一個原因）、`changeset.busy`（專案的項目一直有人在改，等不到鎖，稍後再試）。
 */
export function applyChangeSet(projectKey: string, number: number): Promise<ChangeSetDetail> {
  return apiRequest<ChangeSetDetail>(`${changeSetPath(projectKey, number)}/apply`, { method: 'POST' });
}

/** 放棄（拒絕）一份等待核准的變更集。提出的人或有 blueprint.apply 的人可以做。 */
export function rejectChangeSet(projectKey: string, number: number): Promise<ChangeSetDetail> {
  return apiRequest<ChangeSetDetail>(`${changeSetPath(projectKey, number)}/reject`, { method: 'POST' });
}
