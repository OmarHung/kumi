import { http, type JsonBodyType } from 'msw';
import type { Activity, ActivityPage, CommentRevision, FieldChange, PersonRef } from '../api/types';
import { ACTIVITY_VIEW_STORAGE_KEY } from '../features/item-page/activityView';
import { failure, ok } from './api';
import { exampleMember, exampleOwner } from './fixtures';
import type { RecordedRequest } from './items';
import { server } from './server';

export const ownerRef: PersonRef = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
export const memberRef: PersonRef = { personId: exampleMember.personId, displayName: exampleMember.displayName };

let sequence = 0;

/** 和合約一樣的形狀：沒有的屬性是 null，changes 只有 updated 有內容。 */
function activity(kind: Activity['kind'], overrides: Partial<Activity>): Activity {
  sequence += 1;
  return {
    id: `0190a000-0000-7000-8000-${String(sequence).padStart(12, '0')}`,
    kind,
    actor: ownerRef,
    at: '2026-11-13T08:00:00Z',
    changes: [],
    fromState: null,
    toState: null,
    comment: null,
    replyToId: null,
    blueprintVersion: null,
    importedFrom: null,
    editedAt: null,
    deletedAt: null,
    deletedBy: null,
    ...overrides,
  };
}

/** 從別的系統匯入的活動：作者與時間是原本的，importedFrom 是來源的名字。 */
export const IMPORTED_FROM = 'monday';

export function created(overrides: Partial<Activity> = {}): Activity {
  return activity('created', overrides);
}

export function updated(changes: readonly FieldChange[], overrides: Partial<Activity> = {}): Activity {
  return activity('updated', { changes, ...overrides });
}

export function transitioned(fromState: string, toState: string, overrides: Partial<Activity> = {}): Activity {
  return activity('transitioned', { fromState, toState, ...overrides });
}

export function commented(comment: string, overrides: Partial<Activity> = {}): Activity {
  return activity('commented', { comment, ...overrides });
}

/** 編輯過的留言：editedAt 有值（時間軸上的位置 at 不變）。 */
export function editedComment(comment: string, overrides: Partial<Activity> = {}): Activity {
  return commented(comment, { editedAt: '2026-11-13T09:00:00Z', ...overrides });
}

/** 刪除（藏起來）的留言：內容是 null，留在原位；deletedBy 預設是作者自己。 */
export function deletedComment(overrides: Partial<Activity> = {}): Activity {
  const actor = overrides.actor ?? ownerRef;
  return commented('', { comment: null, actor, deletedAt: '2026-11-13T10:00:00Z', deletedBy: actor, ...overrides });
}

/** 套用變更集時的搬移：狀態搬移給 fromState、toState，欄位刪除與轉換給 changes（刪除的 to 是 null）。 */
export function migrated(blueprintVersion: number, overrides: Partial<Activity> = {}): Activity {
  return activity('migrated', { blueprintVersion, ...overrides });
}

/**
 * 改類型：changes 第一筆是 item_type 的前後值（類型的 key），接著是封存的自訂欄位（to 是 null）。
 * 狀態跟著搬了時給 fromState、toState。
 */
export function typeChanged(
  fromType: string,
  toType: string,
  archived: readonly FieldChange[] = [],
  overrides: Partial<Activity> = {},
): Activity {
  return activity('type_changed', { changes: [{ field: 'item_type', from: fromType, to: toType }, ...archived], ...overrides });
}

/** 刪除（封存）項目的紀錄：復原之後，時間軸上看得到它。 */
export function deleted(overrides: Partial<Activity> = {}): Activity {
  return activity('deleted', overrides);
}

/** 附加了檔案（PM-5）：changes 一筆，field 是 attachment，to 是檔名。 */
export function attachmentAdded(fileName: string, overrides: Partial<Activity> = {}): Activity {
  return activity('attachment_added', { changes: [{ field: 'attachment', from: null, to: fileName }], ...overrides });
}

/** 刪除了附件（PM-5）：changes 一筆，field 是 attachment，from 是檔名。 */
export function attachmentRemoved(fileName: string, overrides: Partial<Activity> = {}): Activity {
  return activity('attachment_removed', { changes: [{ field: 'attachment', from: fileName, to: null }], ...overrides });
}

/**
 * 復原刪除的項目。照最新的藍圖整理時才有搬移：狀態不在流程裡給 fromState、toState，
 * 封存的自訂欄位每個一筆（to 是 null），拿掉上層是 field 為 parent 的一筆（from 是上層編號、to 是 null）。
 */
export function restored(changes: readonly FieldChange[] = [], overrides: Partial<Activity> = {}): Activity {
  return activity('restored', { changes, ...overrides });
}

/** 後端沒有給 limit 時的每頁筆數。 */
const DEFAULT_ACTIVITY_LIMIT = 50;

function activityPage(all: readonly Activity[], url: URL): ActivityPage {
  // 和後端一樣先照 kind 篩，再分頁（沒給是全部）。
  const kind = url.searchParams.get('kind');
  const entries = kind === null || kind === '' ? all : all.filter((entry) => entry.kind === kind);
  const limit = Number(url.searchParams.get('limit') ?? DEFAULT_ACTIVITY_LIMIT);
  const cursor = url.searchParams.get('cursor');
  const start = cursor === null ? 0 : entries.findIndex((entry) => entry.id === cursor) + 1;
  // 和後端一樣多讀一筆：讀得到就表示還有下一頁，游標是這一頁最後一筆的位置（這裡用編號）。
  const rows = entries.slice(start, start + limit + 1);
  const items = rows.slice(0, limit);
  const nextCursor = rows.length > limit ? (items.at(-1)?.id ?? null) : null;
  return { items, nextCursor };
}

/** serveActivity 回傳的假時間軸：收到的請求，以及換掉（set）或接上（add）之後回的內容。 */
export interface ServedTimeline {
  readonly requests: URL[];
  readonly set: (entries: readonly Activity[]) => void;
  readonly add: (entry: Activity) => void;
  /** 目前整條時間軸（假的留言端點用它找回覆的對象）。 */
  readonly all: () => readonly Activity[];
}

/**
 * 假的活動清單：整條時間軸由舊到新，照請求的 limit 與游標真的分頁（游標是上一頁最後一筆的編號）；請求帶 kind 時先篩過再分頁，和後端一樣。
 * 要測頁界就給超過一頁（50 筆）的活動。set 換掉整條時間軸、add 接在最後，模擬修改或留言後多了一筆；
 * 要讓「新的一筆」被認出來，前面的活動要用同一個物件。
 */
export function serveActivity(itemKey: string, entries: readonly Activity[]): ServedTimeline {
  const requests: URL[] = [];
  let current = entries;
  server.use(
    http.get(`/v1/items/${itemKey}/activity`, ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      return ok(activityPage(current, url));
    }),
  );
  return {
    requests,
    set: (next) => {
      current = next;
    },
    add: (entry) => {
      current = [...current, entry];
    },
    all: () => current,
  };
}

/** count 則留言，內容是「{label} 1」「{label} 2」…，時間都比 created() 晚。 */
export function manyComments(count: number, label = '留言'): Activity[] {
  return Array.from({ length: count }, (_, index) => commented(`${label} ${String(index + 1)}`));
}

interface ServeCommentOptions {
  readonly author?: PersonRef;
  /** 給了就和後端一樣，把這則留言也接到時間軸最後。 */
  readonly timeline?: Pick<ServedTimeline, 'add' | 'all'>;
}

/**
 * 假的 POST /v1/items/{key}/comments：成功時回 201 與這則留言的活動。
 * 帶 replyTo 時照後端的規則：對象要是這條時間軸上的留言，不是就 404 comment.not_found；
 * 對象本身是回覆時，replyToId 存它的最上層那一則。沒有給 timeline 時無從查起，replyTo 照原樣當成最上層那一則。
 */
export function serveComment(itemKey: string, { author = ownerRef, timeline }: ServeCommentOptions = {}): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/items/${itemKey}/comments`, async ({ request }) => {
      const body = (await request.json()) as { readonly body: string; readonly replyTo?: string };
      requests.push({ url: new URL(request.url), body });
      const parent = body.replyTo === undefined ? undefined : timeline?.all().find((entry) => entry.id === body.replyTo);
      if (body.replyTo !== undefined && timeline !== undefined && parent?.kind !== 'commented') {
        return failure(404, 'comment.not_found', '找不到這則留言。');
      }
      const replyToId = parent === undefined ? (body.replyTo ?? null) : (parent.replyToId ?? parent.id);
      const posted = commented(body.body, { actor: author, at: '2026-11-14T08:00:00Z', replyToId });
      timeline?.add(posted);
      return ok(posted, 201);
    }),
  );
  return requests;
}

export function failComment(itemKey: string, status: number, code: string, message: string, details: JsonBodyType = []): void {
  server.use(http.post(`/v1/items/${itemKey}/comments`, () => failure(status, code, message, details)));
}

/**
 * 項目時間軸的預設是「留言」，看不到變更、附件、狀態那些行。要看那些行的測試在自己的 beforeEach 呼叫這個，
 * 選「全部」；選擇是使用者的偏好，放在 localStorage（setup.ts 每個測試後會清掉）。要測留言的測試不要呼叫。
 */
export function showAllActivity(): void {
  localStorage.setItem(ACTIVITY_VIEW_STORAGE_KEY, 'all');
}

const EDITED_AT = '2026-11-15T09:00:00Z';
const DELETED_AT = '2026-11-15T10:00:00Z';

interface ServeEditingOptions {
  /** 做這些事的人（刪除的 deletedBy）；預設是 ownerRef。 */
  readonly viewer?: PersonRef;
}

/** serveCommentEditing 的結果：收到的請求（依路徑分開）。 */
export interface ServedEditing {
  readonly edits: RecordedRequest[];
  readonly deletes: RecordedRequest[];
  readonly restores: RecordedRequest[];
  readonly revisionReads: RecordedRequest[];
}

/**
 * 假的留言編輯、刪除、復原與版本端點（HANDOFF-comment-edit 4.6），直接改 serveActivity 的那條時間軸：
 * 編輯把舊的內容存成版本、換上新的內容與 editedAt；刪除把內容藏起來（comment 是 null）、復原放回來。
 * 權限與錯誤不在這裡假：要測失敗的測試在後面用 server.use 蓋掉。
 */
export function serveCommentEditing(itemKey: string, timeline: Pick<ServedTimeline, 'set' | 'all'>, { viewer = ownerRef }: ServeEditingOptions = {}): ServedEditing {
  const served: ServedEditing = { edits: [], deletes: [], restores: [], revisionReads: [] };
  const olderVersions = new Map<string, readonly CommentRevision[]>();
  const hiddenBodies = new Map<string, string>();
  const find = (id: unknown) => timeline.all().find((entry) => entry.id === id);
  const replace = (next: Activity) => {
    timeline.set(timeline.all().map((entry) => (entry.id === next.id ? next : entry)));
    return next;
  };
  const base = `/v1/items/${itemKey}/comments/:commentId`;
  server.use(
    http.patch(base, async ({ request, params }) => {
      const body = (await request.json()) as { readonly body: string };
      served.edits.push({ url: new URL(request.url), body });
      const entry = find(params.commentId);
      if (entry === undefined) return failure(404, 'comment.not_found', '找不到這則留言。');
      if (entry.deletedAt !== null) return failure(409, 'comment.deleted', '這則留言已經刪除，先復原才能編輯。');
      if (body.body === entry.comment) return ok(entry);
      const older = olderVersions.get(entry.id) ?? [];
      const number = older.length + 1;
      olderVersions.set(entry.id, [...older, { number, body: entry.comment ?? '', at: entry.editedAt ?? entry.at, isCurrent: false }]);
      return ok(replace({ ...entry, comment: body.body, editedAt: EDITED_AT }));
    }),
    http.delete(base, ({ request, params }) => {
      served.deletes.push({ url: new URL(request.url), body: null });
      const entry = find(params.commentId);
      if (entry === undefined) return failure(404, 'comment.not_found', '找不到這則留言。');
      if (entry.deletedAt !== null) return ok(entry);
      hiddenBodies.set(entry.id, entry.comment ?? '');
      return ok(replace({ ...entry, comment: null, deletedAt: DELETED_AT, deletedBy: viewer }));
    }),
    http.post(`${base}/restore`, ({ request, params }) => {
      served.restores.push({ url: new URL(request.url), body: null });
      const entry = find(params.commentId);
      if (entry === undefined) return failure(404, 'comment.not_found', '找不到這則留言。');
      if (entry.deletedAt === null) return ok(entry);
      return ok(replace({ ...entry, comment: hiddenBodies.get(entry.id) ?? '', deletedAt: null, deletedBy: null }));
    }),
    http.get(`${base}/revisions`, ({ request, params }) => {
      served.revisionReads.push({ url: new URL(request.url), body: null });
      const entry = find(params.commentId);
      if (entry === undefined) return failure(404, 'comment.not_found', '找不到這則留言。');
      if (entry.deletedAt !== null) return failure(409, 'comment.deleted', '這則留言已經刪除，先復原才能看版本。');
      const older = olderVersions.get(entry.id) ?? [];
      const current: CommentRevision = { number: older.length + 1, body: entry.comment ?? '', at: entry.editedAt ?? entry.at, isCurrent: true };
      return ok({ items: [current, ...older.toReversed()] });
    }),
  );
  return served;
}
