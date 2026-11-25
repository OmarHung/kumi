import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { server } from '../test/server';
import { addComment, getItemActivity } from './activity';
import { applyChangeSet, getChangeSet, listChangeSets, proposeChangeSet, rejectChangeSet } from './changesets';
import { changeItemType, createItem, deleteItem, getItem, listItems, transitionItem, updateItem } from './items';
import { getMyWork } from './myWork';
import {
  getItemSubscription,
  getUnreadCount,
  listNotifications,
  markAllNotificationsRead,
  markNotificationsRead,
  setItemSubscription,
} from './notifications';
import { listAuditEvents } from './audit';
import { changeOwnPassword, createAccessToken, listAccessTokens, revokeAccessToken, signOutOtherSessions } from './account';
import { createPerson, disablePerson, enablePerson, listPeople, resetPersonPassword } from './people';
import { createProject, getProject, listProjects, removeMember, updateMember } from './projects';
import { searchItems, searchProjects } from './search';
import { getSession, signIn, signOut } from './session';
import { listTemplates } from './templates';
import type { ProjectBlueprint } from './blueprint';

const proposedBlueprint: ProjectBlueprint = {
  apiVersion: 'blueprint/v0',
  kind: 'ProjectBlueprint',
  metadata: { key: 'web', name: '官網改版' },
  fields: [],
  workflows: [{ key: 'main', name: '主要流程', states: [{ key: 'done', name: '完成', category: 'done' }] }],
  itemTypes: [{ key: 'task', name: '任務', workflow: 'main' }],
};

interface Captured {
  readonly method: string;
  readonly path: string;
  readonly search: string;
  readonly body: unknown;
}

/** 接住所有 /v1 的請求，記下方法、路徑、查詢字串與內文，回一個成功的信封。 */
function captureRequests(): Captured[] {
  const captured: Captured[] = [];
  server.use(
    http.all('/v1/*', async ({ request }) => {
      const url = new URL(request.url);
      const text = await request.text();
      captured.push({
        method: request.method,
        path: url.pathname,
        search: decodeURIComponent(url.search),
        body: text === '' ? undefined : (JSON.parse(text) as unknown),
      });
      return HttpResponse.json({ success: true, data: { ok: true }, error: null });
    }),
  );
  return captured;
}

describe('API 端點', () => {
  it.each([
    ['取得登入狀態', () => getSession(), 'GET', '/v1/session', undefined],
    [
      '登入',
      () => signIn({ email: 'user@example.com', password: 'example-password' }),
      'POST',
      '/v1/session',
      { email: 'user@example.com', password: 'example-password' },
    ],
    ['登出', () => signOut(), 'DELETE', '/v1/session', undefined],
    ['模板清單', () => listTemplates(), 'GET', '/v1/templates', undefined],
    ['專案清單', () => listProjects(), 'GET', '/v1/projects', undefined],
    [
      '開專案',
      () => createProject({ key: 'WEB', name: '官網改版', template: 'agency_project', includeExamples: true }),
      'POST',
      '/v1/projects',
      { key: 'WEB', name: '官網改版', template: 'agency_project', includeExamples: true },
    ],
    ['專案內容', () => getProject('WEB'), 'GET', '/v1/projects/WEB', undefined],
    [
      '改成員角色',
      () => updateMember('WEB', 'person-1', { role: 'editor' }),
      'PUT',
      '/v1/projects/WEB/members/person-1',
      { role: 'editor' },
    ],
    ['移除成員', () => removeMember('WEB', 'person-1'), 'DELETE', '/v1/projects/WEB/members/person-1', undefined],
    [
      '新增項目',
      () => createItem('WEB', { type: 'task', title: '首頁設計稿' }),
      'POST',
      '/v1/projects/WEB/items',
      { type: 'task', title: '首頁設計稿' },
    ],
    ['項目內容', () => getItem('WEB-12'), 'GET', '/v1/items/WEB-12', undefined],
    [
      '改狀態',
      () => transitionItem('WEB-12', { to: 'doing' }),
      'POST',
      '/v1/items/WEB-12/transitions',
      { to: 'doing' },
    ],
    ['刪除項目', () => deleteItem('WEB-12'), 'DELETE', '/v1/items/WEB-12', undefined],
    ['改類型', () => changeItemType('WEB-12', { type: 'bug' }), 'POST', '/v1/items/WEB-12/type', { type: 'bug' }],
    ['項目的活動', () => getItemActivity('WEB-12'), 'GET', '/v1/items/WEB-12/activity', undefined],
    [
      '留言',
      () => addComment('WEB-12', { body: '第一行\n第二行' }),
      'POST',
      '/v1/items/WEB-12/comments',
      { body: '第一行\n第二行' },
    ],
    ['我的工作', () => getMyWork(), 'GET', '/v1/my-work', undefined],
    [
      '送出變更集',
      () =>
        proposeChangeSet('WEB', {
          baseVersion: 3,
          summary: '拿掉驗收',
          blueprint: proposedBlueprint,
          migrations: { moveStates: [{ workflow: 'main', from: 'qa', to: 'done' }], dropFields: ['notes'] },
        }),
      'POST',
      '/v1/projects/WEB/changesets',
      {
        baseVersion: 3,
        summary: '拿掉驗收',
        blueprint: proposedBlueprint,
        migrations: { moveStates: [{ workflow: 'main', from: 'qa', to: 'done' }], dropFields: ['notes'] },
      },
    ],
    ['變更集清單', () => listChangeSets('WEB'), 'GET', '/v1/projects/WEB/changesets', undefined],
    ['一份變更集', () => getChangeSet('WEB', 3), 'GET', '/v1/projects/WEB/changesets/3', undefined],
    ['套用變更集', () => applyChangeSet('WEB', 3), 'POST', '/v1/projects/WEB/changesets/3/apply', undefined],
    ['放棄變更集', () => rejectChangeSet('WEB', 3), 'POST', '/v1/projects/WEB/changesets/3/reject', undefined],
    ['人員清單', () => listPeople(), 'GET', '/v1/people', undefined],
    [
      '新增人員',
      () =>
        createPerson({
          displayName: '範例使用者',
          email: 'user@example.com',
          password: 'example-password',
          role: 'member',
        }),
      'POST',
      '/v1/people',
      {
        displayName: '範例使用者',
        email: 'user@example.com',
        password: 'example-password',
        role: 'member',
      },
    ],
    ['停用帳號', () => disablePerson('person-1'), 'POST', '/v1/people/person-1/disable', undefined],
    ['重新啟用帳號', () => enablePerson('person-1'), 'POST', '/v1/people/person-1/enable', undefined],
    [
      '重設別人的密碼',
      () => resetPersonPassword('person-1', { newPassword: 'example-pass-2' }),
      'PUT',
      '/v1/people/person-1/password',
      { newPassword: 'example-pass-2' },
    ],
    [
      '改自己的密碼',
      () => changeOwnPassword({ currentPassword: 'example-pass-1', newPassword: 'example-pass-2', revokeAccessTokens: true }),
      'PUT',
      '/v1/account/password',
      { currentPassword: 'example-pass-1', newPassword: 'example-pass-2', revokeAccessTokens: true },
    ],
    ['登出其他裝置', () => signOutOtherSessions({ currentPassword: 'example-pass-1' }), 'POST', '/v1/account/other-sessions/sign-out', { currentPassword: 'example-pass-1' }],
    ['列出存取權杖', () => listAccessTokens(), 'GET', '/v1/account/tokens', undefined],
    [
      '建立存取權杖',
      () => createAccessToken({ name: '筆電', scopes: ['items:read'], expiresInDays: 90 }),
      'POST',
      '/v1/account/tokens',
      { name: '筆電', scopes: ['items:read'], expiresInDays: 90 },
    ],
    ['撤銷存取權杖', () => revokeAccessToken('tok-1'), 'DELETE', '/v1/account/tokens/tok-1', undefined],
    ['未讀數', () => getUnreadCount(), 'GET', '/v1/notifications/unread-count', undefined],
    [
      '標成已讀',
      () => markNotificationsRead(['n-1', 'n-2']),
      'POST',
      '/v1/notifications/read',
      { notificationIds: ['n-1', 'n-2'] },
    ],
    ['全部標為已讀', () => markAllNotificationsRead(), 'POST', '/v1/notifications/read-all', undefined],
    ['關注狀態', () => getItemSubscription('WEB-12'), 'GET', '/v1/items/WEB-12/subscription', undefined],
    [
      '取消關注',
      () => setItemSubscription('WEB-12', false),
      'PUT',
      '/v1/items/WEB-12/subscription',
      { following: false },
    ],
  ] as const)('%s', async (_name, call, method, path, body) => {
    // Arrange
    const captured = captureRequests();

    // Act
    await call();

    // Assert
    expect(captured).toEqual([{ method, path, search: '', body }]);
  });

  it('通知清單：未讀與游標、筆數寫進查詢字串，沒給的不帶', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await listNotifications({});
    await listNotifications({ unread: true, cursor: 'c-1', limit: 50 });

    // Assert
    expect(captured.map((request) => [request.method, request.path, request.search])).toEqual([
      ['GET', '/v1/notifications', ''],
      ['GET', '/v1/notifications', '?unread=true&cursor=c-1&limit=50'],
    ]);
  });

  it('稽核紀錄：第一頁只帶 limit，之後帶 cursor（游標原樣交給伺服器）', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await listAuditEvents(null);
    await listAuditEvents('2026-11-25T02:30:00Z|abc');

    // Assert
    expect(captured.map((request) => [request.method, request.path, request.search])).toEqual([
      ['GET', '/v1/audit-events', '?limit=50'],
      ['GET', '/v1/audit-events', '?limit=50&cursor=2026-11-25T02:30:00Z|abc'],
    ]);
  });

  it('送出變更集用 JSON；套用與放棄沒有內文', async () => {
    // Arrange
    const contentTypes: (string | null)[] = [];
    server.use(
      http.post('/v1/projects/WEB/changesets*', ({ request }) => {
        contentTypes.push(request.headers.get('Content-Type'));
        return HttpResponse.json({ success: true, data: null, error: null });
      }),
    );

    // Act
    await proposeChangeSet('WEB', { baseVersion: 1, blueprint: proposedBlueprint });
    await applyChangeSet('WEB', 1);

    // Assert
    expect(contentTypes).toEqual(['application/json', null]);
  });

  it('路徑裡的代號與編號會被編碼', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await getProject('A/B');

    // Assert
    expect(captured[0]?.path).toBe('/v1/projects/A%2FB');
  });

  it('人員編號在路徑裡也會被編碼', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await disablePerson('a/b');

    // Assert
    expect(captured[0]?.path).toBe('/v1/people/a%2Fb/disable');
  });

  it('修改項目只送有寫的屬性，null 照樣送出表示清掉', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await updateItem('WEB-12', { title: '新標題', dueDate: null, fields: { impact: null } });

    // Assert
    expect(captured).toEqual([
      {
        method: 'PATCH',
        path: '/v1/items/WEB-12',
        search: '',
        body: { title: '新標題', dueDate: null, fields: { impact: null } },
      },
    ]);
  });

  it('項目清單把可重複的條件拆成多個查詢參數', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await listItems('WEB', {
      type: ['task', 'requirement'],
      state: ['todo'],
      stateCategory: ['not_started', 'in_progress'],
      assignee: 'me',
      q: '首頁',
      sort: { field: 'due_date', direction: 'asc' },
      limit: 50,
      cursor: 'next-page',
    });

    // Assert
    expect(captured[0]?.path).toBe('/v1/projects/WEB/items');
    expect(captured[0]?.search).toBe(
      '?type=task&type=requirement&state=todo&stateCategory=not_started&stateCategory=in_progress' +
        '&assignee=me&q=首頁&sort=due_date:asc&limit=50&cursor=next-page',
    );
  });

  it('項目清單可以只要某個項目的直接下層', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await listItems('WEB', { parent: 'WEB-12', limit: 50 });

    // Assert
    expect(captured[0]?.search).toBe('?parent=WEB-12&limit=50');
  });

  it('週期與里程碑的條件是 cycle、milestone：編號或 none', async () => {
    // Arrange
    const captured = captureRequests();
    const cycleId = '0190a000-0000-7000-8000-0000000000d2';

    // Act
    await listItems('WEB', { cycleId, noMilestone: true });
    await listItems('WEB', { noCycle: true, milestoneId: cycleId });

    // Assert
    expect(captured.map((request) => request.search)).toEqual([`?cycle=${cycleId}&milestone=none`, `?cycle=none&milestone=${cycleId}`]);
  });

  it('自訂欄位的條件送成重複的 field.<key>，排序照原樣', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await listItems('WEB', {
      fields: { client: ['client_a', '_none'], billable: ['true'] },
      sort: { field: 'field.hours', direction: 'desc' },
    });
    await listItems('WEB', { fields: {} });

    // Assert
    expect(captured.map((request) => request.search)).toEqual([
      '?field.client=client_a&field.client=_none&field.billable=true&sort=field.hours:desc',
      '',
    ]);
  });

  it('跨專案搜尋把字與筆數放在查詢字串，特殊字元要編碼', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await searchItems('首頁 & 登入=1', 8);

    // Assert
    expect(captured.map((request) => [request.method, request.path, request.search])).toEqual([
      ['GET', '/v1/search/items', '?q=首頁+&+登入=1&limit=8'],
    ]);
  });

  it('專案搜尋把字與筆數放在查詢字串，特殊字元要編碼', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await searchProjects('192.0.2.10 & web01=1', 20);

    // Assert
    expect(captured.map((request) => [request.method, request.path, request.search])).toEqual([
      ['GET', '/v1/search/projects', '?q=192.0.2.10+&+web01=1&limit=20'],
    ]);
  });

  it('活動清單帶上筆數與游標', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await getItemActivity('WEB-12', { limit: 50, cursor: 'next-page' });

    // Assert
    expect(captured[0]?.path).toBe('/v1/items/WEB-12/activity');
    expect(captured[0]?.search).toBe('?limit=50&cursor=next-page');
  });

  it('新增與修改項目都可以帶說明，修改時 null 是清掉', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await createItem('WEB', { type: 'task', title: '首頁設計稿', description: '第一行\n第二行' });
    await updateItem('WEB-12', { description: null });

    // Assert
    expect(captured.map((request) => request.body)).toEqual([
      { type: 'task', title: '首頁設計稿', description: '第一行\n第二行' },
      { description: null },
    ]);
  });

  it('項目清單沒有條件時不帶查詢字串', async () => {
    // Arrange
    const captured = captureRequests();

    // Act
    await listItems('WEB');

    // Assert
    expect(captured[0]?.search).toBe('');
  });
});
