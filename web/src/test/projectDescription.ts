import { http } from 'msw';
import type { DescriptionTemplate, DescriptionTemplateInput, ProjectDescription } from '../api/types';
import { failure, ok } from './api';
import { webProjectTemplate } from './descriptionTemplateFixtures';
import type { RecordedRequest } from './items';
import { server } from './server';

export const EMPTY_DESCRIPTION: ProjectDescription = { text: null, updatedAt: null, updatedBy: null };

export { webProjectTemplate };

export function describedBy(text: string, overrides: Partial<ProjectDescription> = {}): ProjectDescription {
  return {
    text,
    updatedAt: '2026-11-20T08:05:00Z',
    updatedBy: { personId: 'p-wang', displayName: '王小明' },
    ...overrides,
  };
}

interface Rejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  /** 422 時錯誤掛在哪個欄位（說明是 text）。 */
  readonly path?: string;
}

export interface ProjectDescriptionServer {
  readonly saved: RecordedRequest[];
  readonly current: () => ProjectDescription;
  /** 之後的讀回這份說明：測「別人改過」。 */
  readonly replaceOnServer: (next: ProjectDescription) => void;
  /** 下一次儲存回這個錯誤（只一次）。 */
  readonly rejectNext: (error: Rejection) => void;
}

/** 有狀態的專案說明端點：PUT 之後 GET 讀到新的，更新的人是 savedBy。 */
export function serveProjectDescription(
  projectKey: string,
  initial: ProjectDescription = EMPTY_DESCRIPTION,
  savedBy = { personId: 'p-owner', displayName: '範例擁有者' },
): ProjectDescriptionServer {
  let current = initial;
  let rejection: Rejection | undefined;
  const saved: RecordedRequest[] = [];
  const path = `/v1/projects/${projectKey}/description`;
  server.use(
    http.get(path, () => ok(current)),
    http.put(path, async ({ request }) => {
      const body = (await request.json()) as { text: string | null };
      saved.push({ url: new URL(request.url), body });
      if (rejection !== undefined) {
        const { status, code, message, path: field } = rejection;
        rejection = undefined;
        return failure(status, code, message, field === undefined ? [] : [{ code, message, path: field }]);
      }
      const text = body.text === null || body.text.trim() === '' ? null : body.text;
      current = { text, updatedAt: '2026-11-20T09:30:00Z', updatedBy: savedBy };
      return ok(current);
    }),
  );
  return {
    saved,
    current: () => current,
    replaceOnServer: (next) => {
      current = next;
    },
    rejectNext: (error) => {
      rejection = error;
    },
  };
}

export function orgTemplate(overrides: Partial<DescriptionTemplate> = {}): DescriptionTemplate {
  return {
    id: '0190a000-0000-7000-8000-0000000000a1',
    name: '維運案子',
    body: '## 主機\n- \n',
    isBuiltIn: false,
    isDefault: false,
    ...overrides,
  };
}

interface DescriptionTemplatesServer {
  /** 目前伺服器上的清單（排序與預設都照後端的規則）。 */
  readonly current: () => readonly DescriptionTemplate[];
  /** POST、PUT、DELETE 收到的請求，照順序。 */
  readonly writes: RecordedRequest[];
  /** 別人把這一份刪掉了（畫面還開著它）。 */
  readonly removeOnServer: (id: string) => void;
  /** 下一次寫入（新增、修改、刪除）回這個錯誤（只一次）。 */
  readonly rejectNext: (error: Rejection) => void;
}

/** 後端的排序：組織的（預設的在最前面，其餘依名稱）再接內建的；沒有組織的預設時內建的是預設。 */
function arranged(organization: readonly DescriptionTemplate[]): readonly DescriptionTemplate[] {
  const hasDefault = organization.some((template) => template.isDefault);
  const sorted = [...organization].sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || a.name.localeCompare(b.name));
  return [...sorted, { ...webProjectTemplate, isDefault: !hasDefault }];
}

function failureOf({ status, code, message, path }: Rejection) {
  return failure(status, code, message, path === undefined ? [] : [{ code, message, path }]);
}

const NOT_FOUND = () => failure(404, 'description_template.not_found', '找不到這個說明格式。');
const NAME_TAKEN = () =>
  failure(409, 'description_template.name_taken', '已經有同名的說明格式。', [
    { code: 'description_template.name_taken', message: '已經有同名的說明格式。', path: 'name' },
  ]);

/** 伺服器上組織的格式與排隊的錯誤。內建的不在這裡：一律是 webProjectTemplate。 */
class TemplateStore {
  organization: readonly DescriptionTemplate[];
  rejection: Rejection | undefined;
  private nextId = 1;

  constructor(initial: readonly DescriptionTemplate[]) {
    this.organization = initial.filter((template) => !template.isBuiltIn);
  }

  newId(): string {
    return `0190a000-0000-7000-8000-${String(this.nextId++).padStart(12, '0')}`;
  }

  /** 只一次：下一次寫入回排好的錯誤。 */
  takeRejection(): Rejection | undefined {
    const taken = this.rejection;
    this.rejection = undefined;
    return taken;
  }

  /** 名稱不分大小寫不能重複，內建的名稱也算（修改時不含自己）。 */
  isTaken(name: string, exceptId?: string): boolean {
    const others = this.organization.filter((template) => template.id !== exceptId);
    return [webProjectTemplate, ...others].some((template) => template.name.toLowerCase() === name.trim().toLowerCase());
  }

  /** 設成預設會取消原本的。 */
  withoutOtherDefaults(input: DescriptionTemplateInput, id: string): readonly DescriptionTemplate[] {
    return input.isDefault ? this.organization.map((template) => (template.id === id ? template : { ...template, isDefault: false })) : this.organization;
  }

  create(input: DescriptionTemplateInput): DescriptionTemplate {
    const created: DescriptionTemplate = { id: this.newId(), name: input.name.trim(), body: input.body, isBuiltIn: false, isDefault: input.isDefault };
    this.organization = [...this.withoutOtherDefaults(input, created.id), created];
    return created;
  }

  update(id: string, input: DescriptionTemplateInput): DescriptionTemplate {
    const updated: DescriptionTemplate = { id, name: input.name.trim(), body: input.body, isBuiltIn: false, isDefault: input.isDefault };
    this.organization = this.withoutOtherDefaults(input, id).map((template) => (template.id === id ? updated : template));
    return updated;
  }
}

/** 一次寫入的共同開頭：記下請求，有排好的錯誤就回它。 */
function beginWrite(store: TemplateStore, writes: RecordedRequest[], request: Request, body: unknown) {
  writes.push({ url: new URL(request.url), body });
  const rejected = store.takeRejection();
  return rejected === undefined ? undefined : failureOf(rejected);
}

/**
 * 有狀態的說明格式端點（GET、POST、PUT、DELETE /v1/description-templates），規則照後端：
 * 名稱不分大小寫不能重複（含內建的）、設成預設會取消原本的、內建的與不存在的是 404。
 * 沒給參數時只有內建的「網站案子」；傳進來的內建格式會被忽略，內建的一律是 webProjectTemplate。
 */
export function serveDescriptionTemplates(initial: readonly DescriptionTemplate[] = []): DescriptionTemplatesServer {
  const store = new TemplateStore(initial);
  const writes: RecordedRequest[] = [];
  server.use(
    http.get('/v1/description-templates', () => ok(arranged(store.organization))),
    http.post('/v1/description-templates', async ({ request }) => {
      const input = (await request.json()) as DescriptionTemplateInput;
      const rejected = beginWrite(store, writes, request, input);
      if (rejected !== undefined) return rejected;
      return store.isTaken(input.name) ? NAME_TAKEN() : ok(store.create(input), 201);
    }),
    http.put('/v1/description-templates/:id', async ({ request, params }) => {
      const input = (await request.json()) as DescriptionTemplateInput;
      const id = String(params.id);
      const rejected = beginWrite(store, writes, request, input);
      if (rejected !== undefined) return rejected;
      if (!store.organization.some((template) => template.id === id)) return NOT_FOUND();
      return store.isTaken(input.name, id) ? NAME_TAKEN() : ok(store.update(id, input));
    }),
    http.delete('/v1/description-templates/:id', ({ request, params }) => {
      const rejected = beginWrite(store, writes, request, null);
      if (rejected !== undefined) return rejected;
      const removed = store.organization.find((template) => template.id === params.id);
      if (removed === undefined) return NOT_FOUND();
      store.organization = store.organization.filter((template) => template !== removed);
      return ok(removed);
    }),
  );
  return {
    current: () => arranged(store.organization),
    writes,
    removeOnServer: (id) => { store.organization = store.organization.filter((template) => template.id !== id); },
    rejectNext: (error) => { store.rejection = error; },
  };
}
