import { apiRequest } from './http';
import type {
  Member,
  NewProjectRequest,
  Project,
  ProjectDetail,
  RemovedMember,
  UpdateMemberRequest,
} from './types';

function projectPath(projectKey: string): string {
  return `/v1/projects/${encodeURIComponent(projectKey)}`;
}

export function listProjects(signal?: AbortSignal): Promise<readonly Project[]> {
  return apiRequest<readonly Project[]>('/v1/projects', { signal });
}

/** 成功時回 201。代號重複、格式不對等問題放在錯誤的 details，path 是 key、name 或 template。 */
export function createProject(request: NewProjectRequest): Promise<Project> {
  return apiRequest<Project>('/v1/projects', { method: 'POST', body: request });
}

/** 專案不存在或看不到時丟出 404 `project.not_found`。 */
export function getProject(projectKey: string, signal?: AbortSignal): Promise<ProjectDetail> {
  return apiRequest<ProjectDetail>(projectPath(projectKey), { signal });
}

function memberPath(projectKey: string, personId: string): string {
  return `${projectPath(projectKey)}/members/${encodeURIComponent(personId)}`;
}

/** 設定某人在專案裡的角色；他還不是成員時就是加成員。把最後一位 admin 換成別的角色是 409 project.last_admin。 */
export function updateMember(
  projectKey: string,
  personId: string,
  request: UpdateMemberRequest,
): Promise<Member> {
  return apiRequest<Member>(memberPath(projectKey, personId), { method: 'PUT', body: request });
}

/**
 * 把一個人移出專案，回傳他被移除前的角色。他負責的項目與成員欄位的值都留著。
 * 不是成員時是 404 project.member_not_found；最後一位 admin 是 409 project.last_admin。
 */
export function removeMember(projectKey: string, personId: string): Promise<RemovedMember> {
  return apiRequest<RemovedMember>(memberPath(projectKey, personId), { method: 'DELETE' });
}

/**
 * 改專案的名稱（代號不能改）。需要 project.manage；封存的專案也可以改。
 * 422 的 details 路徑是 name（空白、太長、有控制字元）。名稱沒變時照樣回傳專案。
 */
export function renameProject(projectKey: string, name: string): Promise<Project> {
  return apiRequest<Project>(`${projectPath(projectKey)}/name`, { method: 'PUT', body: { name } });
}

/** 封存：之後只能看。已經封存的照樣回傳（archivedAt 是原本的時刻）。需要 project.manage。 */
export function archiveProject(projectKey: string): Promise<Project> {
  return apiRequest<Project>(`${projectPath(projectKey)}/archive`, { method: 'POST' });
}

/** 取消封存。沒有封存的照樣回傳。需要 project.manage。 */
export function unarchiveProject(projectKey: string): Promise<Project> {
  return apiRequest<Project>(`${projectPath(projectKey)}/unarchive`, { method: 'POST' });
}
