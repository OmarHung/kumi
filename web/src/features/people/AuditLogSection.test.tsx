import { screen, waitFor, within } from '@testing-library/react';
import { delay, http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuditEvent } from '../../api/audit';
import type { SessionUser } from '../../api/types';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { failure, ok, signedInAs } from '../../test/api';
import { auditEventOf, auditEvents, forbidAudit, serveAudit } from '../../test/audit';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { memberPerson, ownerPerson, servePeopleAdmin } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const orgAdmin: SessionUser = { ...exampleMember, role: 'admin' };

interface OpenOptions {
  readonly user?: SessionUser;
  /** 稽核紀錄的內容；沒給就是沒有紀錄。 */
  readonly events?: readonly AuditEvent[];
  readonly pageSize?: number;
  /** 要自己蓋掉稽核紀錄的端點時（慢、失敗）：在 signedInAs 之後呼叫，後加的優先。 */
  readonly setup?: () => void;
}

/** 以指定的人打開人員頁；稽核紀錄的假後端要在 signedInAs 之後才設（後加的優先）。 */
async function openPeople({ user = exampleOwner, events = [], pageSize, setup }: OpenOptions = {}) {
  signedInAs(user);
  const backend = serveAudit(events, { pageSize });
  setup?.();
  servePeopleAdmin({ actor: user, people: [ownerPerson, memberPerson] });
  const view = renderApp('/people');
  // 窄螢幕的人員是清單、寬螢幕是表格：等到人員載入就好。
  await screen.findAllByText('範例成員');
  return { ...view, backend };
}

const SECTION = { name: '稽核紀錄' };

describe('人員頁：稽核紀錄的權限', () => {
  it('擁有者看得到，請求只送一次、帶 limit', async () => {
    // Arrange
    // Act
    const { backend } = await openPeople({ events: [auditEventOf()] });

    // Assert
    expect(await screen.findByRole('heading', { level: 2, ...SECTION })).toBeInTheDocument();
    await screen.findByRole('table', { name: '稽核紀錄' });
    expect(backend.requests).toHaveLength(1);
    expect(backend.requests[0]?.get('limit')).toBe('50');
    expect(backend.requests[0]?.get('cursor')).toBeNull();
  });

  it.each([
    ['管理員', orgAdmin],
    ['成員', { ...exampleMember, role: 'member' as const }],
  ])('%s看不到：沒有這個區塊，也不發請求', async (_role, user) => {
    // Arrange
    // Act
    const { backend } = await openPeople({ user, events: [auditEventOf()] });
    await flushPending();

    // Assert
    expect(screen.queryByRole('heading', { level: 2, ...SECTION })).not.toBeInTheDocument();
    expect(backend.requests).toHaveLength(0);
  });
});

describe('人員頁：稽核紀錄的內容', () => {
  it('每一筆有時間、做的人、對象、動作的中文說明、結果與來源位址；失敗標「失敗」', async () => {
    // Arrange
    const events = [
      auditEventOf({
        eventId: 'a1',
        action: 'role_changed',
        target: { personId: 'p2', displayName: '範例成員' },
        details: { from: 'member', to: 'admin' },
      }),
      auditEventOf({ eventId: 'a2', action: 'sign_in', outcome: 'failed', actor: null, sourceAddress: '198.51.100.7' }),
    ];

    // Act
    await openPeople({ events });
    const table = await screen.findByRole('table', { name: '稽核紀錄' });

    // Assert
    const [, first, second] = within(table).getAllByRole('row');
    expect(first).toHaveTextContent('範例使用者');
    expect(first).toHaveTextContent('範例成員');
    expect(first).toHaveTextContent('改角色（從 成員 到 管理員）');
    expect(first).toHaveTextContent('成功');
    expect(first).toHaveTextContent('203.0.113.5');
    expect(within(first as HTMLElement).getByText(/2026\/11\/25/).tagName).toBe('TIME');
    expect(second).toHaveTextContent('登入失敗');
    expect(second).toHaveTextContent('失敗');
    expect(second).toHaveTextContent('198.51.100.7');
    expect(within(second as HTMLElement).getByText('沒有做的人')).toBeInTheDocument();
  });

  it.each([
    [{ action: 'sign_in' }, '登入成功'],
    [{ action: 'password_changed', details: { signedOutSessions: '2', revokedAccessTokens: '3' } }, '改密碼（登出其他裝置 2 個、撤銷存取權杖 3 把）'],
    [{ action: 'password_changed', details: { signedOutSessions: '0', revokedAccessTokens: '0' } }, '改密碼'],
    [{ action: 'other_sessions_signed_out' }, '登出其他裝置'],
    [{ action: 'password_reset' }, '重設密碼'],
    [{ action: 'account_disabled' }, '停用帳號'],
    [{ action: 'account_enabled' }, '啟用帳號'],
    [{ action: 'person_created', details: { role: 'guest' } }, '新增人員（訪客）'],
    [{ action: 'access_token_created', details: { tokenName: 'CI', scopes: 'items:read items:write' } }, '建立存取權杖「CI」（讀取項目、修改項目）'],
    [{ action: 'access_token_created', details: { tokenName: 'CI', scopes: 'items:read future:scope' } }, '建立存取權杖「CI」（讀取項目、future:scope）'],
    [{ action: 'access_token_revoked', details: { tokenName: 'CI' } }, '撤銷存取權杖「CI」'],
    [{ action: 'cli_login_approved', details: { machine: 'MacBook-Pro' } }, '核准 kumi 登入（MacBook-Pro）'],
    [{ action: 'cli_login_redeemed' }, '兌換 kumi 登入成功'],
    [{ action: 'cli_login_redeemed', outcome: 'failed' as const, details: { reason: 'origin_mismatch' } }, '兌換 kumi 登入失敗（來源和核准時不同）'],
    [{ action: 'cli_login_redeemed', outcome: 'failed' as const, details: { reason: 'verifier_mismatch' } }, '兌換 kumi 登入失敗（驗證碼不符）'],
    [{ action: 'cli_login_code_reused', details: { revokedTokens: '2' } }, '登入碼被重複使用（撤銷存取權杖 2 把）'],
    [{ action: 'oauth_authorized', details: { client: 'Claude Code', clientHost: 'client.example' } }, '授權 AI 用戶端「Claude Code」（client.example）'],
    [{ action: 'oauth_grant_revoked', details: { client: 'Claude Code' } }, '撤銷 AI 用戶端授權「Claude Code」'],
    [{ action: 'something_new' }, 'something_new'],
  ])('動作 %j 寫成「%s」', async (event, text) => {
    // Arrange
    // Act
    await openPeople({ events: [auditEventOf(event)] });
    const table = await screen.findByRole('table', { name: '稽核紀錄' });

    // Assert
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent(text);
  });

  it('做的人或對象已經不在組織（displayName 是 null）：寫「（已不在組織）」，不是空白', async () => {
    // Arrange
    const gone = { personId: '0190a000-0000-7000-8000-0000000000ff', displayName: null };
    const events = [auditEventOf({ action: 'password_reset', actor: gone, target: gone })];

    // Act
    await openPeople({ events });
    const table = await screen.findByRole('table', { name: '稽核紀錄' });

    // Assert
    expect(within(table).getAllByText('（已不在組織）')).toHaveLength(2);
  });

  it('沒有紀錄時說明還沒有', async () => {
    // Arrange
    // Act
    await openPeople();

    // Assert
    expect(await screen.findByText('還沒有稽核紀錄')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '稽核紀錄' })).not.toBeInTheDocument();
  });

  it('載入中先顯示骨架', async () => {
    // Arrange
    const slow = () => {
      server.use(
        http.get('/v1/audit-events', async () => {
          await delay(200);
          return ok({ items: [], nextCursor: null });
        }),
      );
    };

    // Act
    await openPeople({ setup: slow });

    // Assert
    expect(await screen.findByText('正在載入稽核紀錄…')).toBeInTheDocument();
    expect(await screen.findByText('還沒有稽核紀錄')).toBeInTheDocument();
  });

  it('第一次就讀不到：說明原因，可以重試', async () => {
    // Arrange
    const { user } = await openPeople({ setup: forbidAudit });
    const alert = await screen.findByText('無法載入稽核紀錄');
    serveAudit([auditEventOf()]);

    // Act
    await user.click(within(alert.closest('[role="alert"]') as HTMLElement).getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('table', { name: '稽核紀錄' })).toBeInTheDocument();
  });
});

describe('人員頁：稽核紀錄的分頁', () => {
  it('一次只載入一頁；「載入更多」用 nextCursor 接下一頁，到底了就沒有按鈕', async () => {
    // Arrange
    const { user, backend } = await openPeople({ events: auditEvents(5), pageSize: 2 });
    const table = await screen.findByRole('table', { name: '稽核紀錄' });

    // Assert
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(screen.getByText('已載入 2 筆')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    await waitFor(() => {
      expect(within(table).getAllByRole('row')).toHaveLength(5);
    });
    expect(backend.requests.map((query) => query.get('cursor'))).toEqual([null, 'after-2']);

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    await waitFor(() => {
      expect(within(table).getAllByRole('row')).toHaveLength(6);
    });
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(screen.getByText('已載入 5 筆')).toBeInTheDocument();
  });

  it('載入更多失敗：說明並留著已載入的，可以再試', async () => {
    // Arrange
    const { user } = await openPeople({ events: auditEvents(3), pageSize: 2 });
    const table = await screen.findByRole('table', { name: '稽核紀錄' });
    server.use(http.get('/v1/audit-events', () => failure(500, 'server.error', 'boom')));

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByText('載入更多失敗，請再試一次。')).toBeInTheDocument();
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(screen.getByRole('button', { name: '載入更多' })).toBeInTheDocument();
  });
});

describe('人員頁：稽核紀錄（窄螢幕）', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('改成清單：第一行動作，第二行時間、人、結果與來源位址', async () => {
    // Arrange
    const events = [auditEventOf({ action: 'password_reset', target: { personId: 'p2', displayName: '範例成員' } })];

    // Act
    await openPeople({ events });
    const list = await screen.findByRole('list', { name: '稽核紀錄' });

    // Assert
    const row = within(list).getByRole('listitem');
    expect(row).toHaveTextContent('重設密碼');
    expect(row).toHaveTextContent('做的人：範例使用者');
    expect(row).toHaveTextContent('對象：範例成員');
    expect(row).toHaveTextContent('結果：成功');
    expect(row).toHaveTextContent('來源位址：203.0.113.5');
    expect(screen.queryByRole('table', { name: '稽核紀錄' })).not.toBeInTheDocument();
  });
});
