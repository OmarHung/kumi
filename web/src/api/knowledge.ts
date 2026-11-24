import { apiRequest } from './http';
import type {
  KnowledgeAttachment,
  KnowledgePage,
  KnowledgePageDeletion,
  KnowledgePageMove,
  KnowledgePagePatch,
  KnowledgePageRestriction,
  KnowledgePageTree,
  KnowledgeSpace,
  KnowledgeSpaceSettings,
  KnowledgeTrashEntry,
  NewKnowledgePage,
  NewKnowledgeSpace,
} from './knowledgeTypes';

/**
 * 知識庫的端點（internal/HANDOFF-knowledge-base.md 第 5 節）。全部只能用 Cookie（權杖不能用）。
 * 看不到的空間與頁面和不存在一樣是 404（knowledge.space_not_found、knowledge.page_not_found）；
 * 看得到卻不能改是 403 knowledge.forbidden；空間或它的專案封存了是 409 knowledge.space_archived。
 */

/** 頁面內文的上限（和伺服器的 KnowledgeRules.MaxPageBodyLength 一樣）。 */
export const KNOWLEDGE_PAGE_BODY_MAX = 100_000;
export const KNOWLEDGE_PAGE_TITLE_MAX = 200;
export const KNOWLEDGE_SPACE_NAME_MAX = 100;
export const KNOWLEDGE_SPACE_DESCRIPTION_MAX = 500;
export const KNOWLEDGE_MAX_DEPTH = 10;
export const KNOWLEDGE_MAX_RESTRICTED_PEOPLE = 100;
/** 和伺服器的 KnowledgeRules.SpaceKeyPattern 一樣。 */
export const KNOWLEDGE_SPACE_KEY_PATTERN = /^[A-Z][A-Z0-9]{1,9}$/;

/** 409：送出的 expectedRevision 不是現在的（別人先存了），什麼都沒改。 */
export const PAGE_CONFLICT = 'knowledge.page_conflict';
/** 409：底下有自己看不到的頁面，不能刪除。 */
export const PAGE_HAS_HIDDEN_DESCENDANTS = 'knowledge.page_has_hidden_descendants';
export const SPACE_ARCHIVED = 'knowledge.space_archived';

export function spacePath(spaceKey: string): string {
  return `/v1/knowledge/spaces/${encodeURIComponent(spaceKey)}`;
}

export function pagePath(spaceKey: string, pageNumber: number): string {
  return `${spacePath(spaceKey)}/pages/${String(pageNumber)}`;
}

/** 看得到的空間：沒封存的在前，再依名稱。 */
export function listSpaces(signal?: AbortSignal): Promise<readonly KnowledgeSpace[]> {
  return apiRequest<readonly KnowledgeSpace[]>('/v1/knowledge/spaces', { signal });
}

export function getSpace(spaceKey: string, signal?: AbortSignal): Promise<KnowledgeSpace> {
  return apiRequest<KnowledgeSpace>(spacePath(spaceKey), { signal });
}

/**
 * 201。組織空間只有組織管理員能建；專案空間要那個專案的 project.manage。
 * 409 knowledge.space_key_taken、knowledge.space_project_taken、knowledge.space_limit_reached、knowledge.space_archived；
 * 422（path 是 key、name、description、projectKey）。
 */
export function createSpace(space: NewKnowledgeSpace): Promise<KnowledgeSpace> {
  return apiRequest<KnowledgeSpace>('/v1/knowledge/spaces', { method: 'POST', body: space });
}

export function updateSpace(spaceKey: string, settings: KnowledgeSpaceSettings): Promise<KnowledgeSpace> {
  return apiRequest<KnowledgeSpace>(spacePath(spaceKey), { method: 'PUT', body: settings });
}

/** 已經是那個狀態時照樣成功。 */
export function setSpaceArchived(spaceKey: string, archived: boolean): Promise<KnowledgeSpace> {
  return apiRequest<KnowledgeSpace>(`${spacePath(spaceKey)}/${archived ? 'archive' : 'unarchive'}`, { method: 'POST' });
}

export function getPageTree(spaceKey: string, signal?: AbortSignal): Promise<KnowledgePageTree> {
  return apiRequest<KnowledgePageTree>(`${spacePath(spaceKey)}/tree`, { signal });
}

/** 新刪的在前。 */
export function listTrash(spaceKey: string, signal?: AbortSignal): Promise<readonly KnowledgeTrashEntry[]> {
  return apiRequest<readonly KnowledgeTrashEntry[]>(`${spacePath(spaceKey)}/trash`, { signal });
}

/**
 * 201。422 knowledge.page_position_invalid（path 是 parentNumber 或 beforeNumber）、knowledge.page_too_deep、
 * 標題與內文的問題（path 是 title、body）；409 knowledge.page_limit_reached。
 */
export function createPage(spaceKey: string, page: NewKnowledgePage): Promise<KnowledgePage> {
  return apiRequest<KnowledgePage>(`${spacePath(spaceKey)}/pages`, { method: 'POST', body: page });
}

export function getPage(spaceKey: string, pageNumber: number, signal?: AbortSignal): Promise<KnowledgePage> {
  return apiRequest<KnowledgePage>(pagePath(spaceKey, pageNumber), { signal });
}

/** 409 knowledge.page_conflict；422（path 是 title、body）。 */
export function updatePage(spaceKey: string, pageNumber: number, patch: KnowledgePagePatch): Promise<KnowledgePage> {
  return apiRequest<KnowledgePage>(pagePath(spaceKey, pageNumber), { method: 'PATCH', body: patch });
}

/** 回傳搬完的樹。422 knowledge.page_position_invalid、knowledge.page_too_deep。 */
export function movePage(spaceKey: string, pageNumber: number, move: KnowledgePageMove): Promise<KnowledgePageTree> {
  return apiRequest<KnowledgePageTree>(`${pagePath(spaceKey, pageNumber)}/move`, { method: 'POST', body: move });
}

/** 連同底下的頁面一起進垃圾桶。409 knowledge.page_has_hidden_descendants。 */
export function deletePage(spaceKey: string, pageNumber: number): Promise<KnowledgePageDeletion> {
  return apiRequest<KnowledgePageDeletion>(pagePath(spaceKey, pageNumber), { method: 'DELETE' });
}

/** 把這一頁所在的那一批拿回來；不在垃圾桶裡時照樣成功。409 knowledge.page_limit_reached。 */
export function restorePage(spaceKey: string, pageNumber: number): Promise<KnowledgePage> {
  return apiRequest<KnowledgePage>(`${pagePath(spaceKey, pageNumber)}/restore`, { method: 'POST' });
}

export function getPageRestriction(spaceKey: string, pageNumber: number, signal?: AbortSignal): Promise<KnowledgePageRestriction> {
  return apiRequest<KnowledgePageRestriction>(`${pagePath(spaceKey, pageNumber)}/restriction`, { signal });
}

/** people 是 null 或空陣列是拿掉限制；有名單時伺服器自動把自己加進去。422 knowledge.restriction_invalid（path 是 people 或 people[i]）。 */
export function setPageRestriction(
  spaceKey: string,
  pageNumber: number,
  people: readonly string[] | null,
): Promise<KnowledgePageRestriction> {
  return apiRequest<KnowledgePageRestriction>(`${pagePath(spaceKey, pageNumber)}/restriction`, {
    method: 'PUT',
    body: { people },
  });
}

/** 先上傳的在前。 */
export function listPageAttachments(spaceKey: string, pageNumber: number, signal?: AbortSignal): Promise<readonly KnowledgeAttachment[]> {
  return apiRequest<readonly KnowledgeAttachment[]>(`${pagePath(spaceKey, pageNumber)}/attachments`, { signal });
}

/** 上傳的網址（multipart，欄位名稱 file）；上傳本身和項目的附件共用同一段 XHR（工作包 W2 抽出來）。 */
export function pageAttachmentUploadUrl(spaceKey: string, pageNumber: number): string {
  return `${pagePath(spaceKey, pageNumber)}/attachments`;
}

/** 下載的網址；呈現的規則和項目的附件相同（只有 png、jpeg、gif、webp 在瀏覽器裡顯示，其他一律下載）。 */
export function knowledgeAttachmentContentUrl(attachmentId: string): string {
  return `/v1/knowledge/attachments/${encodeURIComponent(attachmentId)}/content`;
}
