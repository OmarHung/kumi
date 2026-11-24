import { apiRequest } from './http';
import { itemResourcePath } from './items';
import type { Activity, ActivityPage, ActivityQuery, CommentRevisionList, NewCommentRequest } from './types';

function queryString(query: ActivityQuery): string {
  const params = new URLSearchParams();
  if (query.limit !== undefined) params.append('limit', String(query.limit));
  if (query.cursor !== undefined) params.append('cursor', query.cursor);
  if (query.kind !== undefined) params.append('kind', query.kind);
  const text = params.toString();
  return text === '' ? '' : `?${text}`;
}

/** 項目的活動與留言，由舊到新。limit 或 cursor 不合法時丟出 422 `query.invalid`。 */
export function getItemActivity(
  itemKey: string,
  query: ActivityQuery = {},
  signal?: AbortSignal,
): Promise<ActivityPage> {
  return apiRequest<ActivityPage>(`${itemResourcePath(itemKey)}/activity${queryString(query)}`, { signal });
}

/**
 * 留言，成功時回 201 與這則留言的活動。沒有 comment.create 是 403 `work.forbidden`；
 * 內容的問題是 422：comment.body_required、comment.body_too_long、comment.body_invalid。
 */
export function addComment(itemKey: string, request: NewCommentRequest): Promise<Activity> {
  return apiRequest<Activity>(`${itemResourcePath(itemKey)}/comments`, { method: 'POST', body: request });
}

function commentPath(itemKey: string, commentId: string): string {
  return `${itemResourcePath(itemKey)}/comments/${encodeURIComponent(commentId)}`;
}

/**
 * 編輯自己的留言（含回覆），回編輯之後的活動；內容和現在的一樣時什麼都不寫。
 * 403 `comment.not_author`、`work.forbidden`；409 `comment.deleted`、`comment.too_many_edits`、`project.archived`；
 * 422 `comment.body_*`。
 */
export function editComment(itemKey: string, commentId: string, body: string): Promise<Activity> {
  return apiRequest<Activity>(commentPath(itemKey, commentId), { method: 'PATCH', body: { body } });
}

/** 刪除（藏起來）一則留言，回刪除之後的活動；已經刪除的重送也是 200。 */
export function deleteComment(itemKey: string, commentId: string): Promise<Activity> {
  return apiRequest<Activity>(commentPath(itemKey, commentId), { method: 'DELETE' });
}

/** 復原刪除的留言，回復原之後的活動；沒有刪除的重送也是 200。 */
export function restoreComment(itemKey: string, commentId: string): Promise<Activity> {
  return apiRequest<Activity>(`${commentPath(itemKey, commentId)}/restore`, { method: 'POST' });
}

/** 留言的版本，新的在前。刪除的留言是 409 `comment.deleted`。 */
export function listCommentRevisions(itemKey: string, commentId: string, signal?: AbortSignal): Promise<CommentRevisionList> {
  return apiRequest<CommentRevisionList>(`${commentPath(itemKey, commentId)}/revisions`, { signal });
}
