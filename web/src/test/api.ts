import { http, HttpResponse, type JsonBodyType } from 'msw';
import type { Item, Project, ProjectDetail, SessionUser, Template } from '../api/types';
import { exampleCliCatalog } from './cli';
import { filterAcrossProjects, itemTypesOf } from './crossProjectItems';
import { webProjectTemplate } from './descriptionTemplateFixtures';
import { exampleTemplates, projectDetail } from './fixtures';
import { server } from './server';

export function ok(data: JsonBodyType, status = 200) {
  return HttpResponse.json({ success: true, data, error: null }, { status });
}

export function failure(status: number, code: string, message: string, details: JsonBodyType = []) {
  return HttpResponse.json(
    { success: false, data: null, error: { code, message, details } },
    { status },
  );
}

export function authRequired() {
  return failure(401, 'auth.required', '請先登入。');
}

interface SignedInOptions {
  readonly projects?: readonly Project[];
  /** 專案內容；沒給的專案用 projectDetail(project)。 */
  readonly details?: readonly ProjectDetail[];
  readonly myWork?: readonly Item[];
  /**
   * 跨專案清單（GET /v1/items，「我的工作」的四種版面）裡的所有項目，照條件篩選（test/crossProjectItems.ts）。
   * 沒給時用 myWork：原本「我的工作」的測試照樣看得到那些項目。
   */
  readonly items?: readonly Item[];
  readonly templates?: readonly Template[];
  /** 側欄的未讀數；預設 0（不顯示）。要檢查收件匣的測試用 serveNotifications 蓋掉。 */
  readonly unreadCount?: number;
}

function detailFor(
  projectKey: unknown,
  projects: readonly Project[],
  details: readonly ProjectDetail[],
): ProjectDetail | undefined {
  const detail = details.find((candidate) => candidate.project.key === projectKey);
  const project = projects.find((candidate) => candidate.key === projectKey);
  return detail ?? (project && projectDetail(project));
}

/**
 * 已登入的假後端：登入狀態、專案清單、專案內容、我的工作、模板。
 * 個別測試再用 server.use 蓋掉需要的端點（後加的優先）。
 */
export function signedInAs(user: SessionUser, options: SignedInOptions = {}): void {
  const { projects = [], details = [], myWork = [], items = myWork, templates = exampleTemplates, unreadCount = 0 } = options;
  const listed = [...projects, ...details.map((detail) => detail.project)].filter(
    (project, index, all) => all.findIndex((other) => other.key === project.key) === index,
  );
  server.use(
    http.get('/v1/session', () => ok({ user, scopes: null })),
    http.get('/v1/projects', () => ok(listed)),
    http.get('/v1/projects/:projectKey', ({ params }) => {
      const detail = detailFor(params.projectKey, projects, details);
      return detail ? ok(detail) : failure(404, 'project.not_found', '找不到這個專案。');
    }),
    // 專案頁的工具列問自己的檢視；預設一個都沒有，要檢查的測試用 serveSavedViews 蓋掉。
    http.get('/v1/projects/:projectKey/saved-views', () => ok([])),
    // 項目面板、篩選與規劃頁問週期與里程碑；預設都沒有，要內容的測試用 serveCycles、serveMilestones 蓋掉。
    http.get('/v1/projects/:projectKey/cycles', () => ok([])),
    http.get('/v1/projects/:projectKey/milestones', () => ok([])),
    http.get('/v1/my-work', () => ok({ items: myWork })),
    // 「我的工作」的四種版面：跨專案清單照條件篩、類型的選項從專案內容合併、自己的檢視預設一個都沒有。
    http.get('/v1/items', ({ request }) => ok(filterAcrossProjects(items, new URL(request.url), user.personId))),
    http.get('/v1/item-types', () =>
      ok(itemTypesOf(listed.flatMap((project) => detailFor(project.key, projects, details) ?? []))),
    ),
    // 專案頁的頁列問有沒有綁這個專案的知識庫空間；預設一個都沒有，要空間的測試用 serveKnowledge 蓋掉。
    http.get('/v1/knowledge/spaces', () => ok([])),
    http.get('/v1/my-work/saved-views', () => ok([])),
    // 專案總覽：預設沒有專案，要列的測試用 serveOverview 蓋掉。
    http.get('/v1/overview/projects', () => ok([])),
    // 快速跳轉的跨專案搜尋：預設找不到東西，要結果的測試用 serveSearch 蓋掉。
    http.get('/v1/search/items', () => ok({ items: [] })),
    // 快速跳轉與專案清單頁的專案搜尋（代號、名稱、說明）：預設找不到，要結果的測試用 serveProjectSearch 蓋掉。
    http.get('/v1/search/projects', () => ok([])),
    http.get('/v1/templates', () => ok(templates)),
    // 開新專案的對話框與資訊頁問說明格式；和真的伺服器一樣，預設只有內建的「網站案子」（而且是預設）；要組織的格式的測試用 serveDescriptionTemplates 蓋掉。
    http.get('/v1/description-templates', () => ok([webProjectTemplate])),
    // 每一頁的側欄都問未讀數；項目頁與面板都問關注狀態（預設沒有在關注）。
    http.get('/v1/notifications/unread-count', () => ok({ count: unreadCount })),
    // 新通知的提示讀最新的未讀；預設沒有，要通知的測試用 serveNotifications 蓋掉。
    http.get('/v1/notifications', () => ok({ items: [], nextCursor: null })),
    // 項目頁與面板的「附件」區問清單；預設沒有附件，要內容的測試用 serveAttachments 蓋掉。
    http.get('/v1/items/:itemKey/attachments', () => ok([])),
    // 「我的帳號」的 Email 通知開關；預設是開著、組織沒開寄信，要檢查的測試用 serveEmailPreference 蓋掉。
    http.get('/v1/account/email-notifications', () => ok({ enabled: true, organizationEnabled: false })),
    // 專案頁的「AI 助理」入口問成員能選的 AI；預設組織沒開，要入口的測試用 serveAiOptions 蓋掉。
    http.get('/v1/ai/options', () => ok({ available: false, connections: [], default: null, canAddPersonal: false })),
    // AI 帳號頁的「AI 供應商」讀自己的連線；預設一個都沒有，要內容的測試用 servePersonalAi 蓋掉。
    http.get('/v1/account/ai-connections', () => ok([])),
    // AI 帳號頁的「本機代理」清單；預設沒有登記過的電腦，要內容的測試用 serveLocalAgents 蓋掉。
    http.get('/v1/account/local-agents', () => ok([])),
    // 「我的帳號」的「命令列工具」讀可以下載的 kumi；預設三個平台都有（0.3.0），要別的情況的測試用 serveCli 蓋掉。
    http.get('/v1/cli', () => ok(exampleCliCatalog())),
    // 「命令列與整合」的設定精靈與存取權杖、已授權的 AI 代理：預設都沒有，要內容的測試用 serveAccessTokens、serveOAuthGrants 蓋掉。
    http.get('/v1/account/tokens', () => ok([])),
    http.get('/v1/account/oauth-grants', () => ok([])),
    // 人員頁下面的稽核紀錄（只有擁有者）：預設還沒有紀錄，要內容的測試用 serveAudit 蓋掉。
    http.get('/v1/audit-events', () => ok({ items: [], nextCursor: null })),
    http.get('/v1/items/:itemKey/subscription', () => ok({ following: false, reason: null, canFollow: true })),
  );
}

export function signedOut(): void {
  server.use(http.get('/v1/session', () => authRequired()));
}

/**
 * 登入狀態只在第一次檢查時有效，之後的檢查都是 401：真的失效的工作階段是這樣的。
 * 網頁收到任何 401 會先重問一次 /v1/session，還是 401 才登出，所以要測「登出」的測試用這個。
 */
export function sessionEndsAfterFirstCheck(user: SessionUser): void {
  let checks = 0;
  server.use(
    http.get('/v1/session', () => {
      checks += 1;
      return checks === 1 ? ok({ user, scopes: null }) : authRequired();
    }),
  );
}
