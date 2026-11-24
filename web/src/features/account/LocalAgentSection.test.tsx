import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { LocalAgentMachine } from '../../api/aiTypes';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { failure, ok, signedInAs } from '../../test/api';
import { serveAccessTokens } from '../../test/account';
import { exampleOwner } from '../../test/fixtures';
import { exampleMachine, servePersonalAi, serveLocalAgents } from '../../test/personalAi';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const ALLOWED = makeAiOptions({ canAddPersonal: true });
const NOW = new Date('2026-11-23T03:10:00+00:00');
const LAPTOP = exampleMachine({ lastSeenAt: '2026-11-23T03:09:30+00:00' });
const DESKTOP = exampleMachine({
  id: 'machine-2',
  name: '範例桌機',
  engines: [{ kind: 'codex', version: '0.163.0' }],
  online: false,
  lastSeenAt: '2026-11-23T03:07:00+00:00',
});

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'], now: NOW });
});

afterEach(() => {
  vi.useRealTimers();
});

const LOCAL_SECTION = '本機代理';

/** 先登入再蓋各端點的處理（signedInAs 會重設預設的）；回傳本機代理的假後端。 */
async function openSection(machines: readonly LocalAgentMachine[] = [], options = ALLOWED) {
  signedInAs(exampleOwner);
  serveAiOptions(options);
  servePersonalAi();
  const backend = serveLocalAgents(machines);
  const view = renderApp('/account/ai');
  const section = await screen.findByRole('region', { name: LOCAL_SECTION });
  return { ...view, section, backend };
}

describe('AI 帳號：本機代理（出現的條件）', () => {
  it('組織允許、沒有登記過的電腦：說明一行與接上電腦的步驟', async () => {
    // Arrange
    // Act
    const { section } = await openSection();

    // Assert
    expect(within(section).getByRole('heading', { name: LOCAL_SECTION })).toBeInTheDocument();
    expect(within(section).getByText(/不能碰你電腦上的檔案/)).toBeInTheDocument();
    const steps = within(section).getByRole('list', { name: '接上電腦的步驟' });
    expect(within(steps).getAllByRole('listitem')).toHaveLength(5);
    expect(within(steps).getByRole('button', { name: '建立本機代理用的權杖' })).toBeInTheDocument();
    expect(within(steps).getByRole('region', { name: '啟動代理的指令' })).toHaveTextContent('kumi agent start');
    expect(within(steps).getByRole('region', { name: '登入的指令' })).toHaveTextContent(`kumi agent login --server ${window.location.origin}`);
    expect(within(steps).getByRole('region', { name: '自動啟動的指令' })).toHaveTextContent('kumi agent enable');
    expect(within(steps).getByRole('region', { name: '貼上權杖登入的指令' })).toHaveTextContent(`kumi agent login --server ${window.location.origin} --paste`);
    expect(within(section).queryByRole('list', { name: '我的電腦' })).not.toBeInTheDocument();
  });

  it('第一步的「設定精靈」連結指到「命令列與整合」那一頁的 #setup', async () => {
    // Arrange
    // Act
    const { section } = await openSection();

    // Assert
    const link = await within(section).findByRole('link', { name: '設定精靈' });
    expect(link).toHaveAttribute('href', '/account/integrations#setup');
  });

  it('組織不允許、也沒有登記過的電腦：整段不出現', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions({ canAddPersonal: false }));
    servePersonalAi();
    serveLocalAgents();

    // Act
    renderApp('/account/ai');

    // Assert
    const notice = await screen.findByRole('region', { name: 'AI 供應商' });
    expect(await within(notice).findByText(/組織目前不允許使用自己的 AI 帳號/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: LOCAL_SECTION })).not.toBeInTheDocument();
  });

  it('組織不允許、但登記過電腦：照樣列出，沒有設定步驟與建立權杖的按鈕', async () => {
    // Arrange
    // Act
    const { section } = await openSection([LAPTOP], makeAiOptions({ canAddPersonal: false }));

    // Assert
    const list = await within(section).findByRole('list', { name: '我的電腦' });
    expect(within(list).getByText('範例筆電')).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '建立本機代理用的權杖' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('list', { name: '接上電腦的步驟' })).not.toBeInTheDocument();
  });
});

describe('我的帳號：電腦的清單', () => {
  it('在線的寫「在線」；離線的寫最後在線的時間；引擎與版本都列出來', async () => {
    // Arrange
    // Act
    const { section } = await openSection([LAPTOP, DESKTOP]);

    // Assert
    const list = await within(section).findByRole('list', { name: '我的電腦' });
    const [laptop, desktop] = within(list).getAllByRole('listitem');
    expect(laptop).toHaveTextContent('範例筆電');
    expect(laptop).toHaveTextContent('狀態：在線');
    expect(laptop).toHaveTextContent('Claude Code 2.1.330、Codex 0.163.0');
    expect(desktop).toHaveTextContent('範例桌機');
    expect(desktop).toHaveTextContent('離線（最後在 3 分鐘前）');
    expect(desktop).toHaveTextContent('Codex 0.163.0');
    expect(desktop).not.toHaveTextContent('Claude Code');
    // 有電腦之後不再列四個步驟，只留再建一把權杖的入口。
    expect(within(section).queryByRole('list', { name: '接上電腦的步驟' })).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '建立本機代理用的權杖' })).toBeInTheDocument();
  });

  it('「更多」裡的移除要確認：取消什麼都不做；確認之後打 DELETE，那一列不在了', async () => {
    // Arrange
    const { user, section, backend } = await openSection([LAPTOP, DESKTOP]);
    await within(section).findByRole('list', { name: '我的電腦' });

    // Act：取消
    await user.click(within(section).getByRole('button', { name: '「範例桌機」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '移除' }));
    const dialog = await screen.findByRole('dialog', { name: '移除「範例桌機」？' });
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.removed).toEqual([]);

    // Act：確認
    await user.click(within(section).getByRole('button', { name: '「範例桌機」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '移除' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '移除' }));

    // Assert
    await waitFor(() => {
      expect(backend.removed).toEqual(['machine-2']);
    });
    await waitFor(() => {
      expect(within(section).queryByText('範例桌機')).not.toBeInTheDocument();
    });
    expect(within(section).getByText('範例筆電')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('移除失敗：對話框留著並說明原因', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();
    const backend = serveLocalAgents([LAPTOP]);
    backend.rejectNextRemove({ status: 500, code: 'server.error', message: '伺服器暫時出錯。' });
    const { user } = renderApp('/account/ai');
    const section = await screen.findByRole('region', { name: LOCAL_SECTION });
    await within(section).findByRole('list', { name: '我的電腦' });

    // Act
    await user.click(within(section).getByRole('button', { name: '「範例筆電」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '移除' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '移除' }));

    // Assert
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText(/伺服器暫時無法處理/)).toBeInTheDocument();
  });

  it('清單讀不到：說明並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();
    let isFailing = true;
    server.use(http.get('/v1/account/local-agents', () => (isFailing ? failure(404, 'not_found', '讀不到。') : ok([LAPTOP]))));
    const { user } = renderApp('/account/ai');
    const section = await screen.findByRole('region', { name: LOCAL_SECTION });
    const retry = await within(section).findByRole('button', { name: '重試' });
    isFailing = false;

    // Act
    await user.click(retry);

    // Assert
    expect(await within(section).findByText('範例筆電')).toBeInTheDocument();
  });
});

describe('我的帳號：建立本機代理用的權杖', () => {
  it('送出 agent:run、blueprint:read、blueprint:write、items:read、名稱「本機代理」加日期、90 天；明文只在對話框裡顯示一次', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();
    serveLocalAgents();
    const tokens = serveAccessTokens([]);
    const { user } = renderApp('/account/ai');
    const section = await screen.findByRole('region', { name: LOCAL_SECTION });
    const button = await within(section).findByRole('button', { name: '建立本機代理用的權杖' });

    // Act
    await user.click(button);

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '本機代理用的權杖已建立' });
    expect(tokens.creates).toEqual([
      { name: '本機代理 2026/11/23', scopes: ['agent:run', 'blueprint:read', 'blueprint:write', 'items:read'], expiresInDays: 90 },
    ]);
    expect(section).toHaveTextContent('之前建立的權杖讀不到項目，要讀項目請重新建立一把');
    const secret = tokens.lastSecret() ?? '';
    expect(within(dialog).getByLabelText('存取權杖')).toHaveValue(secret);
    expect(dialog).toHaveTextContent('只會顯示這一次');

    // Act：關上
    await user.click(within(dialog).getByRole('button', { name: '完成' }));

    // Assert：明文不在畫面上、也沒有存進瀏覽器
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(document.body).not.toHaveTextContent(secret);
    expect(JSON.stringify([...Object.entries(localStorage), ...Object.entries(sessionStorage)])).not.toContain(secret);
  });

  it('建立失敗（已達上限）：原因寫在按鈕旁邊，沒有對話框', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();
    serveLocalAgents();
    serveAccessTokens([]);
    server.use(http.post('/v1/account/tokens', () => failure(409, 'access_token.limit_reached', '最多只能同時有 20 把還能用的存取權杖。')));
    const { user } = renderApp('/account/ai');
    const section = await screen.findByRole('region', { name: LOCAL_SECTION });

    // Act
    await user.click(await within(section).findByRole('button', { name: '建立本機代理用的權杖' }));

    // Assert
    expect(await within(section).findByText(/最多只能同時有 20 把/)).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('我的帳號：電腦清單每 15 秒重讀', () => {
  it('電腦從離線變在線：15 秒後畫面跟上', async () => {
    // Arrange
    vi.useRealTimers();
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();
    const backend = serveLocalAgents([DESKTOP]);
    renderApp('/account/ai');
    const section = await screen.findByRole('region', { name: LOCAL_SECTION });
    expect(await within(section).findByText(/離線/)).toBeInTheDocument();
    const before = backend.listCalls();

    // Act
    backend.set([{ ...DESKTOP, online: true }]);
    await vi.advanceTimersByTimeAsync(15_000);

    // Assert
    await waitFor(() => {
      expect(backend.listCalls()).toBeGreaterThan(before);
    });
    expect(await within(section).findByText('在線')).toBeInTheDocument();
  });
});
