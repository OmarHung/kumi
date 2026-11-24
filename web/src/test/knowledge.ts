import { http } from 'msw';
import type {
  KnowledgePage,
  KnowledgePageNode,
  KnowledgePagePatch,
  KnowledgeSpace,
  KnowledgeTrashEntry,
  NewKnowledgePage,
} from '../api/knowledgeTypes';
import { failure, ok } from './api';
import { server } from './server';

/**
 * 知識庫的假資料與假的後端（internal/HANDOFF-knowledge-base.md 第 6 節）。
 * 各工作包共用：要別的行為（失敗、延遲、看不到）的測試用 server.use 蓋掉單一端點。
 */

export const exampleAuthor = { personId: 'p-owner', displayName: '範例使用者' } as const;
const AT = '2026-11-25T02:00:00+00:00';

export function knowledgeSpaceOf(overrides: Partial<KnowledgeSpace> = {}): KnowledgeSpace {
  return {
    id: 'space-docs',
    key: 'DOCS',
    name: '團隊文件',
    description: '操作手冊、會議記錄與決策紀錄。',
    project: null,
    createdAt: AT,
    createdBy: exampleAuthor,
    archivedAt: null,
    myPermissions: { canEdit: true, canComment: true, canManage: true },
    ...overrides,
  };
}

export function pageNodeOf(overrides: Partial<KnowledgePageNode> & Pick<KnowledgePageNode, 'number'>): KnowledgePageNode {
  return {
    id: `page-${String(overrides.number)}`,
    parentId: null,
    position: 0,
    title: `頁面 ${String(overrides.number)}`,
    isRestricted: false,
    updatedAt: AT,
    updatedBy: exampleAuthor,
    ...overrides,
  };
}

/** 範例的樹：1 新人到職（底下 2 帳號申請、3 開發環境）、4 會議記錄。 */
export function exampleKnowledgeTree(): KnowledgePageNode[] {
  return [
    pageNodeOf({ number: 1, title: '新人到職', position: 0 }),
    pageNodeOf({ number: 2, title: '帳號申請', parentId: 'page-1', position: 0 }),
    pageNodeOf({ number: 3, title: '開發環境', parentId: 'page-1', position: 1 }),
    pageNodeOf({ number: 4, title: '會議記錄', position: 1 }),
  ];
}

export interface KnowledgeBackend {
  spaces: KnowledgeSpace[];
  /** 依空間代號放的樹（先序排列）。 */
  trees: Record<string, KnowledgePageNode[]>;
  /** 依「代號/編號」放的內文；沒放的是空字串。 */
  bodies: Record<string, string>;
  revisions: Record<string, number>;
  trash: Record<string, KnowledgeTrashEntry[]>;
  /** 收到的寫入，測試拿來驗送出的內容。 */
  readonly requests: { method: string; path: string; body: unknown }[];
}

interface ServeKnowledgeOptions {
  readonly spaces?: readonly KnowledgeSpace[];
  readonly trees?: Readonly<Record<string, readonly KnowledgePageNode[]>>;
  readonly bodies?: Readonly<Record<string, string>>;
}

function pageKey(spaceKey: string, number: number): string {
  return `${spaceKey}/${String(number)}`;
}

/** 先序：上層在前、同層依 position。 */
function ordered(nodes: readonly KnowledgePageNode[]): KnowledgePageNode[] {
  const children = (parentId: string | null) =>
    nodes.filter((node) => node.parentId === parentId).sort((a, b) => a.position - b.position);
  const visit = (parentId: string | null): KnowledgePageNode[] =>
    children(parentId).flatMap((node) => [node, ...visit(node.id)]);
  return visit(null);
}

function ancestorsOf(nodes: readonly KnowledgePageNode[], node: KnowledgePageNode): KnowledgePageNode[] {
  const parent = nodes.find((candidate) => candidate.id === node.parentId);
  return parent === undefined ? [] : [...ancestorsOf(nodes, parent), parent];
}

function refOf(node: KnowledgePageNode) {
  return { id: node.id, number: node.number, title: node.title };
}

export function pageOf(backend: KnowledgeBackend, space: KnowledgeSpace, node: KnowledgePageNode): KnowledgePage {
  const nodes = backend.trees[space.key] ?? [];
  const ancestors = ancestorsOf(nodes, node);
  const key = pageKey(space.key, node.number);
  return {
    id: node.id,
    space,
    number: node.number,
    title: node.title,
    body: backend.bodies[key] ?? '',
    revision: backend.revisions[key] ?? 1,
    ancestors: ancestors.map(refOf),
    isRestricted: node.isRestricted,
    restrictedAncestors: ancestors.filter((ancestor) => ancestor.isRestricted).map(refOf),
    createdAt: AT,
    createdBy: exampleAuthor,
    updatedAt: node.updatedAt,
    updatedBy: node.updatedBy,
    versionCount: 1,
    myPermissions: {
      canEdit: space.myPermissions.canEdit,
      canComment: space.myPermissions.canComment,
      canDelete: space.myPermissions.canEdit,
    },
  };
}

const notFoundSpace = () => failure(404, 'knowledge.space_not_found', '找不到這個空間。');
const notFoundPage = () => failure(404, 'knowledge.page_not_found', '找不到這一頁。');

/**
 * 假的知識庫後端：空間清單、樹、讀寫頁面、搬移、刪除與復原、垃圾桶。限制、附件、空間的寫入沒有預設的行為，
 * 要用到的測試自己 server.use。回傳的 backend 是活的狀態（測試可以直接改、也可以拿來驗）。
 */
export function serveKnowledge(options: ServeKnowledgeOptions = {}): KnowledgeBackend {
  const spaces = [...(options.spaces ?? [knowledgeSpaceOf()])];
  const backend: KnowledgeBackend = {
    spaces,
    trees: Object.fromEntries(
      spaces.map((space) => [space.key, ordered(options.trees?.[space.key] ?? (space.key === 'DOCS' ? exampleKnowledgeTree() : []))]),
    ),
    bodies: { ...options.bodies },
    revisions: {},
    trash: {},
    requests: [],
  };
  const spaceOf = (key: unknown) => backend.spaces.find((space) => space.key === String(key));
  const nodeOf = (space: KnowledgeSpace, number: unknown) =>
    (backend.trees[space.key] ?? []).find((node) => node.number === Number(number));
  const record = async (request: Request, path: string) => {
    const text = await request.text();
    backend.requests.push({ method: request.method, path, body: text === '' ? null : (JSON.parse(text) as unknown) });
  };

  server.use(
    http.get('/v1/knowledge/spaces', () => ok(backend.spaces)),
    http.get('/v1/knowledge/spaces/:key', ({ params }) => {
      const space = spaceOf(params.key);
      return space === undefined ? notFoundSpace() : ok(space);
    }),
    http.get('/v1/knowledge/spaces/:key/tree', ({ params }) => {
      const space = spaceOf(params.key);
      return space === undefined ? notFoundSpace() : ok({ space, pages: backend.trees[space.key] ?? [] });
    }),
    http.get('/v1/knowledge/spaces/:key/trash', ({ params }) => {
      const space = spaceOf(params.key);
      return space === undefined ? notFoundSpace() : ok(backend.trash[space.key] ?? []);
    }),
    http.get('/v1/knowledge/spaces/:key/pages/:number', ({ params }) => {
      const space = spaceOf(params.key);
      const node = space === undefined ? undefined : nodeOf(space, params.number);
      return space === undefined || node === undefined ? notFoundPage() : ok(pageOf(backend, space, node));
    }),
    http.post('/v1/knowledge/spaces/:key/pages', async ({ request, params }) => {
      const space = spaceOf(params.key);
      if (space === undefined) return notFoundSpace();
      const input = (await request.clone().json()) as NewKnowledgePage;
      await record(request, `/v1/knowledge/spaces/${space.key}/pages`);
      const nodes = backend.trees[space.key] ?? [];
      const number = Math.max(0, ...nodes.map((node) => node.number)) + 1;
      const parent = input.parentNumber === null ? undefined : nodes.find((node) => node.number === input.parentNumber);
      const parentId = parent?.id ?? null;
      const node = pageNodeOf({
        number,
        title: input.title.trim(),
        parentId,
        position: nodes.filter((candidate) => candidate.parentId === parentId).length,
      });
      backend.trees[space.key] = ordered([...nodes, node]);
      backend.bodies[pageKey(space.key, number)] = input.body ?? '';
      return ok(pageOf(backend, space, node), 201);
    }),
    http.patch('/v1/knowledge/spaces/:key/pages/:number', async ({ request, params }) => {
      const space = spaceOf(params.key);
      const node = space === undefined ? undefined : nodeOf(space, params.number);
      if (space === undefined || node === undefined) return notFoundPage();
      const patch = (await request.clone().json()) as KnowledgePagePatch;
      await record(request, `/v1/knowledge/spaces/${space.key}/pages/${String(node.number)}`);
      const key = pageKey(space.key, node.number);
      const revision = backend.revisions[key] ?? 1;
      if (patch.expectedRevision !== revision) {
        return failure(409, 'knowledge.page_conflict', '這一頁在你編輯時被別人改過了。');
      }
      const updated = { ...node, title: patch.title?.trim() ?? node.title };
      backend.trees[space.key] = (backend.trees[space.key] ?? []).map((candidate) => (candidate.id === node.id ? updated : candidate));
      if (patch.body !== undefined) backend.bodies[key] = patch.body;
      backend.revisions[key] = revision + 1;
      return ok(pageOf(backend, space, updated));
    }),
    http.delete('/v1/knowledge/spaces/:key/pages/:number', async ({ request, params }) => {
      const space = spaceOf(params.key);
      const node = space === undefined ? undefined : nodeOf(space, params.number);
      if (space === undefined || node === undefined) return notFoundPage();
      await record(request, `/v1/knowledge/spaces/${space.key}/pages/${String(node.number)}`);
      const nodes = backend.trees[space.key] ?? [];
      const doomed = new Set([node.id]);
      for (const candidate of nodes) {
        if (candidate.parentId !== null && doomed.has(candidate.parentId)) doomed.add(candidate.id);
      }
      backend.trees[space.key] = nodes.filter((candidate) => !doomed.has(candidate.id));
      const parent = nodes.find((candidate) => candidate.id === node.parentId);
      const entry: KnowledgeTrashEntry = {
        deletionId: `deletion-${node.id}`,
        page: refOf(node),
        pageCount: doomed.size,
        formerParent: parent === undefined ? null : refOf(parent),
        deletedAt: AT,
        deletedBy: exampleAuthor,
      };
      backend.trash[space.key] = [entry, ...(backend.trash[space.key] ?? [])];
      return ok({ deletionId: entry.deletionId, page: entry.page, pageCount: entry.pageCount });
    }),
  );
  return backend;
}
