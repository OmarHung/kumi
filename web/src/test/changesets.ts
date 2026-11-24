import { http, type HttpResponse, type JsonBodyType } from 'msw';
import type { ProjectBlueprint } from '../api/blueprint';
import type { ChangeSet, ChangeSetDetail, ChangeSetRequest, ProjectDetail } from '../api/types';
import { ownerRef } from './activity';
import { failure, ok } from './api';
import { server } from './server';

/** 和合約一樣的形狀：等待核准、沒有決定的人。 */
export function makeChangeSet(overrides: Partial<ChangeSet> = {}): ChangeSet {
  return {
    number: 1,
    status: 'pending',
    baseVersion: 1,
    appliedVersion: null,
    summary: null,
    proposedBy: ownerRef,
    proposedAt: '2026-11-15T02:00:00Z',
    origin: 'direct',
    decidedBy: null,
    decidedAt: null,
    ...overrides,
  };
}

/** 沒給 baseBlueprint 時和 blueprint 一樣（沒有差異，和預設的 changes 一致）；有差異的變更集要給基準版本的藍圖。 */
export function makeChangeSetDetail(blueprint: ProjectBlueprint, overrides: Partial<ChangeSetDetail> = {}): ChangeSetDetail {
  return {
    changeSet: makeChangeSet(),
    blueprint,
    baseBlueprint: blueprint,
    changes: [],
    impacts: [],
    blockers: [],
    canApply: true,
    ...overrides,
  };
}

/** 假的 GET …/changesets。 */
export function serveChangeSets(projectKey: string, changeSets: readonly ChangeSet[]): void {
  server.use(http.get(`/v1/projects/${projectKey}/changesets`, () => ok(changeSets)));
}

/** 假的 GET …/changesets/{number}；回傳讀了幾次（套用後要重讀）。set 換掉之後讀到的內容。 */
export function serveChangeSet(projectKey: string, detail: ChangeSetDetail) {
  let current = detail;
  let reads = 0;
  server.use(
    http.get(`/v1/projects/${projectKey}/changesets/${String(detail.changeSet.number)}`, () => {
      reads += 1;
      return ok(current);
    }),
  );
  return {
    reads: () => reads,
    set: (next: ChangeSetDetail) => {
      current = next;
    },
  };
}

export interface RecordedProposal {
  readonly contentType: string | null;
  readonly body: ChangeSetRequest;
}

/**
 * 假的 POST …/changesets：記下內文與 Content-Type。沒給 respond 時像後端一樣存成第 7 份等待核准的變更集，
 * 回 201 與它的內容（藍圖是送來的那一份，基準版本的藍圖是專案目前的那一份）。
 */
export function captureProposals(
  project: ProjectDetail,
  respond?: (body: ChangeSetRequest) => HttpResponse<JsonBodyType>,
): RecordedProposal[] {
  const proposals: RecordedProposal[] = [];
  server.use(
    http.post(`/v1/projects/${project.project.key}/changesets`, async ({ request }) => {
      const body = (await request.json()) as ChangeSetRequest;
      proposals.push({ contentType: request.headers.get('Content-Type'), body });
      if (respond) return respond(body);
      const detail = makeChangeSetDetail(body.blueprint, {
        baseBlueprint: project.blueprint,
        changeSet: makeChangeSet({ number: 7, baseVersion: body.baseVersion, summary: body.summary ?? null }),
      });
      return ok(detail, 201);
    }),
  );
  return proposals;
}

export function invalidChangeSet(details: JsonBodyType) {
  return failure(422, 'changeset.invalid', '變更集沒有通過檢查。', details);
}

/** 假的套用或放棄：回傳收到幾次請求。 */
export function serveDecision(
  projectKey: string,
  number: number,
  action: 'apply' | 'reject',
  respond: () => HttpResponse<JsonBodyType>,
): { readonly count: () => number } {
  let count = 0;
  server.use(
    http.post(`/v1/projects/${projectKey}/changesets/${String(number)}/${action}`, () => {
      count += 1;
      return respond();
    }),
  );
  return { count: () => count };
}
