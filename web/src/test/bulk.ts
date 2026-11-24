import { http } from 'msw';
import type { BulkItemFailure } from '../api/itemRecovery';
import type { Item, PersonRef } from '../api/types';
import { ok } from './api';
import { exampleMember, exampleOwner } from './fixtures';
import { DELETED_AT, type RecordedRequest } from './items';
import { server } from './server';

/** 某一筆要失敗時的原因（和伺服器的 BulkItemFailure.error 同一個形狀，細項可以省略）。 */
export interface BulkFailureSpec {
  readonly code: string;
  readonly message: string;
  readonly details?: BulkItemFailure['error']['details'];
}

interface BulkOptions {
  /** 編號對應的失敗；沒列的都成功。 */
  readonly failures?: Readonly<Record<string, BulkFailureSpec>>;
}

interface BulkBody {
  readonly keys: readonly string[];
  readonly state?: string;
  readonly [property: string]: unknown;
}

function failureOf(key: string, spec: BulkFailureSpec): BulkItemFailure {
  return { key, error: { code: spec.code, message: spec.message, details: spec.details ?? [] } };
}

function assigneeOf(people: readonly PersonRef[], assigneeId: unknown): PersonRef | null {
  if (assigneeId === null) return null;
  const person = people.find((candidate) => candidate.personId === assigneeId);
  return person === undefined ? null : { personId: person.personId, displayName: person.displayName };
}

/** 照請求的順序把編號分成「成功的項目」與「失敗」；不在清單裡的編號是 item.not_found。 */
function partition(items: readonly Item[], keys: readonly string[], failures: BulkOptions['failures']) {
  const succeeded: Item[] = [];
  const failed: BulkItemFailure[] = [];
  for (const key of keys) {
    const spec = failures?.[key];
    const item = items.find((candidate) => candidate.key === key);
    if (spec !== undefined) failed.push(failureOf(key, spec));
    else if (item === undefined) failed.push(failureOf(key, { code: 'item.not_found', message: '找不到這個項目。' }));
    else succeeded.push(item);
  }
  return { succeeded, failed };
}

/**
 * 假的 POST /v1/projects/{projectKey}/items/bulk-update：state 是改狀態（狀態的類別照 categories 查，沒列的不變），
 * 否則把有寫的屬性合併上去（null 是清掉）。回傳收到的請求（內文是 { keys, ... }）。
 */
export function serveBulkUpdate(
  projectKey: string,
  items: readonly Item[],
  options: BulkOptions & {
    readonly categories?: Readonly<Record<string, Item['stateCategory']>>;
    /** assigneeId 對到的人（回應的 assignee 是 { personId, displayName }）；預設是範例的擁有者與成員。 */
    readonly people?: readonly PersonRef[];
  } = {},
): RecordedRequest[] {
  const people = options.people ?? [exampleOwner, exampleMember];
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items/bulk-update`, async ({ request }) => {
      const body = (await request.json()) as BulkBody;
      requests.push({ url: new URL(request.url), body });
      const { keys, state, assigneeId, ...patch } = body;
      const { succeeded, failed } = partition(items, keys, options.failures);
      const updated = succeeded.map((item) => {
        const base = { ...item, updatedAt: '2026-11-18T09:00:00Z' };
        if (state !== undefined) return { ...base, state, stateCategory: options.categories?.[state] ?? item.stateCategory };
        // 請求寫 assigneeId（沒寫是不改，null 是清掉）；項目上是 assignee 物件，沒有 assigneeId 這個屬性。
        const assignee = assigneeId === undefined ? item.assignee : assigneeOf(people, assigneeId);
        return { ...base, ...patch, assignee };
      });
      return ok({ updated, failed });
    }),
  );
  return requests;
}

/** 假的 POST /v1/projects/{projectKey}/items/bulk-delete。 */
export function serveBulkDelete(projectKey: string, items: readonly Item[], options: BulkOptions = {}): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items/bulk-delete`, async ({ request }) => {
      const body = (await request.json()) as BulkBody;
      requests.push({ url: new URL(request.url), body });
      const { succeeded, failed } = partition(items, body.keys, options.failures);
      return ok({ deleted: succeeded.map((item) => ({ key: item.key, deletedAt: DELETED_AT })), failed });
    }),
  );
  return requests;
}

/** 假的 POST /v1/projects/{projectKey}/items/bulk-restore：回清單形狀的項目。 */
export function serveBulkRestore(projectKey: string, items: readonly Item[], options: BulkOptions = {}): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items/bulk-restore`, async ({ request }) => {
      const body = (await request.json()) as BulkBody;
      requests.push({ url: new URL(request.url), body });
      const { succeeded, failed } = partition(items, body.keys, options.failures);
      return ok({ restored: succeeded, failed });
    }),
  );
  return requests;
}
