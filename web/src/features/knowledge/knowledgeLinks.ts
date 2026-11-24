/** 知識庫的網址（internal/HANDOFF-knowledge-base.md 第 6 節）。路由在 app/routes.tsx。 */

export const KNOWLEDGE_HOME = '/knowledge';

export function knowledgeSpaceUrl(spaceKey: string): string {
  return `${KNOWLEDGE_HOME}/${encodeURIComponent(spaceKey)}`;
}

export function knowledgePageUrl(spaceKey: string, pageNumber: number): string {
  return `${knowledgeSpaceUrl(spaceKey)}/${String(pageNumber)}`;
}

export function knowledgeTrashUrl(spaceKey: string): string {
  return `${knowledgeSpaceUrl(spaceKey)}/trash`;
}

export function knowledgeSettingsUrl(spaceKey: string): string {
  return `${knowledgeSpaceUrl(spaceKey)}/settings`;
}

/** 網址上的頁面編號：正整數才算，其他（0、負數、小數、文字）是 null，畫面當成找不到。 */
export function pageNumberOf(segment: string | undefined): number | null {
  if (segment === undefined || !/^[1-9][0-9]{0,8}$/.test(segment)) {
    return null;
  }
  return Number(segment);
}
