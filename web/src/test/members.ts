import { http } from 'msw';
import type { Member, Person, ProjectDetail } from '../api/types';
import { failure, ok } from './api';
import { server } from './server';

export interface MembersBackend {
  readonly detailRequests: () => number;
  /** 專案已經看不到之後還來讀專案內容的次數（每一次都回 404）。 */
  readonly hiddenReads: () => number;
  readonly puts: { readonly personId: string; readonly body: unknown }[];
  readonly deletes: string[];
}

interface MembersOptions {
  /** 組織的人員（GET /v1/people），加成員時從這裡挑。 */
  readonly people: readonly Person[];
  /**
   * 登入的人。他是組織管理員時，最後一位 admin 照樣可以移除或換角色（和真的後端一樣不擋）；
   * 他改了自己的角色或把自己移出專案時，專案的 myRole 跟著變（移出是 null）。
   * 他被移出專案、又不是組織管理員時，之後讀專案是 404、專案清單裡也沒有它。
   */
  readonly viewer?: { readonly personId: string; readonly isOrganizationAdmin: boolean };
}

const LAST_ADMIN = '專案至少要有一位管理員：先把另一位成員設成管理員。';

function isLastAdmin(members: readonly Member[], personId: string): boolean {
  const admins = members.filter((member) => member.role === 'admin');
  return admins.length === 1 && admins[0]?.personId === personId;
}

function withRole(detail: ProjectDetail, people: readonly Person[], personId: string, role: string): ProjectDetail {
  const person = people.find((candidate) => candidate.personId === personId);
  const others = detail.members.filter((member) => member.personId !== personId);
  const changed = { personId, displayName: person?.displayName ?? personId, role };
  return { ...detail, members: [...others, changed] };
}

/** 登入的人自己的角色變了（null 是被移出專案）時，專案的 myRole 跟著變。 */
function withMyRole(detail: ProjectDetail, viewer: MembersOptions['viewer'], personId: string, role: string | null): ProjectDetail {
  return viewer?.personId === personId ? { ...detail, project: { ...detail.project, myRole: role } } : detail;
}

/**
 * 假的專案內容與成員端點，照合約（internal/HANDOFF-week4-admin-ops.md 3A.1、3A.3）：
 * PUT 加成員或改角色，不是組織管理員的人把最後一位 admin 換成別的角色是 409 project.last_admin；
 * DELETE 移除成員，不是成員是 404 project.member_not_found，不是組織管理員的人移除最後一位 admin 是 409。
 * 改過之後，下一次讀專案內容拿到改過的成員。
 */
export function serveMembers(initial: ProjectDetail, { people, viewer }: MembersOptions): MembersBackend {
  const key = initial.project.key;
  let detail = initial;
  let isVisible = true;
  let count = 0;
  let hidden = 0;
  const isGuarded = viewer?.isOrganizationAdmin !== true;
  const puts: MembersBackend['puts'] = [];
  const deletes: string[] = [];
  server.use(
    http.get(`/v1/projects/${key}`, () => {
      count += 1;
      if (isVisible) return ok(detail);
      hidden += 1;
      return failure(404, 'project.not_found', '找不到這個專案。');
    }),
    http.get('/v1/projects', () => ok(isVisible ? [detail.project] : [])),
    http.put(`/v1/projects/${key}/members/:personId`, async ({ params, request }) => {
      const body = (await request.json()) as { role: string };
      const personId = String(params.personId);
      puts.push({ personId, body });
      if (isGuarded && body.role !== 'admin' && isLastAdmin(detail.members, personId)) return failure(409, 'project.last_admin', LAST_ADMIN);
      detail = withMyRole(withRole(detail, people, personId, body.role), viewer, personId, body.role);
      return ok(detail.members.find((member) => member.personId === personId) ?? null);
    }),
    http.delete(`/v1/projects/${key}/members/:personId`, ({ params }) => {
      const personId = String(params.personId);
      deletes.push(personId);
      const member = detail.members.find((candidate) => candidate.personId === personId);
      if (member === undefined) return failure(404, 'project.member_not_found', '這個人不是專案的成員。');
      if (isGuarded && isLastAdmin(detail.members, personId)) return failure(409, 'project.last_admin', LAST_ADMIN);
      const remaining = { ...detail, members: detail.members.filter((candidate) => candidate.personId !== personId) };
      detail = withMyRole(remaining, viewer, personId, null);
      if (viewer?.personId === personId && !viewer.isOrganizationAdmin) isVisible = false;
      return ok({ personId, role: member.role });
    }),
    http.get('/v1/people', () => ok(people)),
  );
  return { detailRequests: () => count, hiddenReads: () => hidden, puts, deletes };
}
