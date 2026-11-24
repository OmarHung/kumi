import { http } from 'msw';
import type { Project, ProjectDetail } from '../api/types';
import { failure, ok } from './api';
import { server } from './server';

/** 封存之後呼叫者還剩的權限（和後端的 ProjectAccess 一樣）：只有讀取與 project.manage，原本沒有的不會多出來。 */
const ARCHIVED_PERMISSIONS: readonly ProjectDetail['myPermissions'][number][] = ['item.read', 'blueprint.read', 'project.manage'];

/** 封存的專案內容：archivedAt 有值，myPermissions 只剩讀取與 project.manage。 */
export function archivedDetail(detail: ProjectDetail, archivedAt = '2026-11-18T08:00:00Z'): ProjectDetail {
  return {
    ...detail,
    project: { ...detail.project, archivedAt },
    myPermissions: detail.myPermissions.filter((permission) => ARCHIVED_PERMISSIONS.includes(permission)),
  };
}

/** 取消封存：權限回到沒封存時的樣子。 */
function unarchivedDetail(archived: ProjectDetail, permissions: ProjectDetail['myPermissions']): ProjectDetail {
  return { ...archived, project: { ...archived.project, archivedAt: null }, myPermissions: permissions };
}

/** 只剩三個權限的專案：唯讀的專案頁、規劃頁、設定頁測試用。 */
export function readOnlyPermissions(): ProjectDetail['myPermissions'] {
  return ['item.read', 'blueprint.read', 'project.manage'];
}

export interface ProjectLifecycleBackend {
  /** 收到的改名請求內文（name）。 */
  readonly renames: string[];
  readonly archives: () => number;
  readonly unarchives: () => number;
  readonly current: () => ProjectDetail;
}

interface LifecycleOptions {
  /** 取消封存之後的權限；沒給就是開始時（沒封存的專案）的那一份。從封存的專案開始時要給。 */
  readonly restoredPermissions?: ProjectDetail['myPermissions'];
  /** 改名時伺服器的回應；沒給就照合約：空白 422，其餘成功。 */
  readonly renameFailure?: { readonly status: number; readonly code: string; readonly message: string };
}

const NAME_REQUIRED = { path: 'name', code: 'project.name_required', message: '請輸入專案名稱。' };

/**
 * 假的專案改名、封存、取消封存（HANDOFF-pm4b 2.4）：每個都回專案，之後讀專案與專案清單拿到改過的樣子。
 * 封存之後權限只剩三個，取消封存回到原本的。
 */
export function serveProjectLifecycle(initial: ProjectDetail, options: LifecycleOptions = {}): ProjectLifecycleBackend {
  const key = initial.project.key;
  const restored = options.restoredPermissions ?? initial.myPermissions;
  let detail = initial;
  let archiveCount = 0;
  let unarchiveCount = 0;
  const renames: string[] = [];
  const respond = (project: Project) => ok(project);
  server.use(
    http.get(`/v1/projects/${key}`, () => ok(detail)),
    http.get('/v1/projects', () => ok([detail.project])),
    http.put(`/v1/projects/${key}/name`, async ({ request }) => {
      const { name } = (await request.json()) as { name: string };
      renames.push(name);
      if (options.renameFailure !== undefined) {
        return failure(options.renameFailure.status, options.renameFailure.code, options.renameFailure.message);
      }
      if (name.trim() === '') return failure(422, 'validation.failed', '請輸入專案名稱。', [NAME_REQUIRED]);
      detail = { ...detail, project: { ...detail.project, name } };
      return respond(detail.project);
    }),
    http.post(`/v1/projects/${key}/archive`, () => {
      archiveCount += 1;
      if (detail.project.archivedAt === null) detail = archivedDetail(detail);
      return respond(detail.project);
    }),
    http.post(`/v1/projects/${key}/unarchive`, () => {
      unarchiveCount += 1;
      if (detail.project.archivedAt !== null) detail = unarchivedDetail(detail, restored);
      return respond(detail.project);
    }),
  );
  return { renames, archives: () => archiveCount, unarchives: () => unarchiveCount, current: () => detail };
}
