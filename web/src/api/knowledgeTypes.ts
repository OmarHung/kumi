import type { Instant, PersonRef } from './types';

/**
 * 知識庫的型別（internal/HANDOFF-knowledge-base.md 第 5 節的 HTTP 合約）。
 * 空間有兩種：組織空間（project 是 null，組織的擁有者、管理員、一般成員都看得到、都能寫，訪客看不到）
 * 與專案空間（看得到專案的人就看得到，能不能寫依他在專案的角色）。
 */

/** 空間或它的專案封存時 canEdit、canComment 一律是 false，canManage 照舊（要能取消封存）。 */
export interface KnowledgeSpacePermissions {
  /** 新增、修改、搬移、刪除與復原頁面、上傳附件、設定頁面的限制。 */
  readonly canEdit: boolean;
  /** 留言（第二段）。 */
  readonly canComment: boolean;
  /** 改名稱與簡介、封存與取消封存。 */
  readonly canManage: boolean;
}

export interface KnowledgeSpaceProject {
  readonly key: string;
  readonly name: string;
  readonly isArchived: boolean;
}

export interface KnowledgeSpace {
  readonly id: string;
  /** 代號（和專案代號同一個格式），網址用它：/knowledge/HR。建立後不能改。 */
  readonly key: string;
  readonly name: string;
  readonly description: string | null;
  /** 專案空間綁的專案；組織空間是 null。 */
  readonly project: KnowledgeSpaceProject | null;
  readonly createdAt: Instant;
  readonly createdBy: PersonRef;
  readonly archivedAt: Instant | null;
  readonly myPermissions: KnowledgeSpacePermissions;
}

/** 樹上的一頁（側欄的頁面樹）：只有標題與位置，沒有內容。 */
export interface KnowledgePageNode {
  readonly id: string;
  /** 空間裡的編號（從 1 起，不重複使用），網址用它：/knowledge/HR/12。 */
  readonly number: number;
  /** 上層頁面的 id；最上層是 null。 */
  readonly parentId: string | null;
  /** 同一層裡的順序，從 0 起連續。 */
  readonly position: number;
  readonly title: string;
  /** 這一頁自己設了限制（不含從上層繼承的）。 */
  readonly isRestricted: boolean;
  readonly updatedAt: Instant;
  readonly updatedBy: PersonRef;
}

/**
 * 空間裡看得到的頁面（不含垃圾桶裡的），先序排列：上層在它的子頁面前面，同一層依 position。
 * 看得到的頁面它的上層一定也看得到，所以一定接得成一棵樹。
 */
export interface KnowledgePageTree {
  readonly space: KnowledgeSpace;
  readonly pages: readonly KnowledgePageNode[];
}

export interface KnowledgePageRef {
  readonly id: string;
  readonly number: number;
  readonly title: string;
}

export interface KnowledgePagePermissions {
  readonly canEdit: boolean;
  readonly canComment: boolean;
  /** canEdit，而且底下沒有自己看不到的頁面。 */
  readonly canDelete: boolean;
}

export interface KnowledgePage {
  readonly id: string;
  readonly space: KnowledgeSpace;
  readonly number: number;
  readonly title: string;
  /** Markdown；空的頁面是空字串。 */
  readonly body: string;
  /** 內容每改一次加一。修改要帶它（expectedRevision）；搬移與改限制不算。 */
  readonly revision: number;
  /** 從最上層到上一層，不含自己。 */
  readonly ancestors: readonly KnowledgePageRef[];
  readonly isRestricted: boolean;
  /** 上層裡自己設了限制的頁面（從最上層往下），這一頁的限制從它們繼承。 */
  readonly restrictedAncestors: readonly KnowledgePageRef[];
  readonly createdAt: Instant;
  readonly createdBy: PersonRef;
  readonly updatedAt: Instant;
  readonly updatedBy: PersonRef;
  readonly versionCount: number;
  readonly myPermissions: KnowledgePagePermissions;
}

export interface NewKnowledgeSpace {
  readonly key: string;
  readonly name: string;
  readonly description?: string | null;
  /** 給了就是專案空間。 */
  readonly projectKey?: string | null;
}

export interface KnowledgeSpaceSettings {
  readonly name: string;
  readonly description: string | null;
}

export interface NewKnowledgePage {
  /** 上層頁面的編號；null 是最上層。 */
  readonly parentNumber: number | null;
  readonly title: string;
  readonly body?: string | null;
  /** 放在這個同層頁面的前面；null 或省略是最後。 */
  readonly beforeNumber?: number | null;
}

/** 只送要改的；標題與內文都省略時什麼都不改。 */
export interface KnowledgePagePatch {
  readonly title?: string;
  readonly body?: string;
  readonly expectedRevision: number;
}

export interface KnowledgePageMove {
  readonly parentNumber: number | null;
  readonly beforeNumber: number | null;
}

export interface KnowledgePageDeletion {
  readonly deletionId: string;
  readonly page: KnowledgePageRef;
  /** 這一批有幾頁（含自己）。 */
  readonly pageCount: number;
}

export interface KnowledgeTrashEntry {
  readonly deletionId: string;
  readonly page: KnowledgePageRef;
  readonly pageCount: number;
  /** 刪除時的上層；最上層是 null。 */
  readonly formerParent: KnowledgePageRef | null;
  readonly deletedAt: Instant;
  readonly deletedBy: PersonRef;
}

export interface KnowledgePageRestriction {
  /** 這一頁自己限制給哪些人看；沒有設是 null。 */
  readonly people: readonly PersonRef[] | null;
  /** 從上層繼承來的限制；要同時在每一份名單裡才看得到。 */
  readonly restrictedAncestors: readonly KnowledgePageRef[];
}

export interface KnowledgeAttachment {
  readonly id: string;
  readonly pageId: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly sha256: string;
  readonly uploadedBy: PersonRef;
  readonly uploadedAt: Instant;
}
