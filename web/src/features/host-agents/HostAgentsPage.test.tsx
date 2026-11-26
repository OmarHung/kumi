import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { http } from 'msw';
import { server } from '../../test/server';
import { exampleEngine, exampleHostAgent, LOGIN_CODE, PAIRING_CODE } from '../../test/hostAgents';
import { exampleOwner } from '../../test/fixtures';
import { signedInAs } from '../../test/api';
import { serveAiSettings } from '../../test/aiSettings';
import { renderApp } from '../../test/renderApp';
import { chooseMenuAction, engineRow, exampleAdmin, exampleMember, HOST_PATH, openAs, openHostSection, serializedCaches, serveHostReads, settle } from './hostAgentsTestKit';

describe('主機 AI 代理：誰看得到', () => {
  it('擁有者打開「主機 AI 代理」：頁名是 h1、有「主機」「來源」兩塊', async () => {
    // Arrange / Act
    await openHostSection();

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'AI 助理主機代理' })).toBeInTheDocument();
    expect(await screen.findByText('還沒有主機代理。先新增一台，再到主機上配對。')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: '主機' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: '來源' })).toBeInTheDocument();
  });

  it('擁有者的 AI 設定頁次導覽有「主機 AI 代理」，但頁面本身不再有主機代理的內容', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();
    const reads = serveHostReads();

    // Act
    renderApp('/ai-settings');
    await screen.findByRole('button', { name: '儲存' });
    await settle();

    // Assert
    expect(within(screen.getByRole('navigation', { name: '設定' })).getByRole('link', { name: 'AI 助理主機代理' })).toHaveAttribute('href', HOST_PATH);
    expect(screen.queryByRole('button', { name: '新增主機代理' })).not.toBeInTheDocument();
    expect(reads).toHaveLength(0);
  });

  it('管理員的次導覽沒有「主機 AI 代理」', async () => {
    // Arrange
    signedInAs(exampleAdmin);
    serveAiSettings();

    // Act
    renderApp('/ai-settings');
    await screen.findByRole('button', { name: '儲存' });

    // Assert
    expect(screen.queryByRole('link', { name: 'AI 助理主機代理' })).not.toBeInTheDocument();
  });

  it.each([
    ['管理員', exampleAdmin],
    ['一般成員', exampleMember],
  ])('%s打網址：說沒有權限，也不會去讀主機的 API', async (_name, user) => {
    // Arrange / Act
    const { reads } = await openAs(user);

    // Assert
    expect(screen.queryByRole('button', { name: '新增主機代理' })).not.toBeInTheDocument();
    expect(reads).toHaveLength(0);
  });

  it('對照：擁有者同樣的流程會去讀（證明上面的「沒有讀」不是因為沒等）', async () => {
    // Arrange / Act
    const { reads } = await openAs(exampleOwner);

    // Assert
    expect(reads.length).toBeGreaterThan(0);
  });
});

describe('主機 AI 代理：代理與引擎的狀態', () => {
  it('代理寫狀態文字與版本；每個引擎寫狀態、版本、計費方式、誰授權的與問題', async () => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent()] });

    // Assert
    const list = await screen.findByRole('list', { name: '主機代理' });
    expect(within(list).getByText('辦公室主機')).toBeInTheDocument();
    expect(within(list).getByText('在線')).toBeInTheDocument();
    expect(within(list).getByText('版本 1.4.0 · office-host')).toBeInTheDocument();
    const claude = engineRow('Claude Code');
    expect(within(claude).getByText('可以接工作')).toBeInTheDocument();
    expect(within(claude).getByText(/版本 2\.1\.330 · API 用量計費 · 範例擁有者 在 .* 授權/)).toBeInTheDocument();
    const codex = engineRow('Codex');
    expect(within(codex).getByText('需要登入')).toBeInTheDocument();
    expect(within(codex).getByText('還沒登入 Codex。')).toBeInTheDocument();
  });

  it.each([
    ['not_installed', '沒裝'],
    ['version_unsupported', '版本不支援'],
    ['not_authorized', '需要擁有者重新授權'],
    ['billing_not_shareable', '訂閱帳號不能共用'],
  ] as const)('引擎狀態 %s 顯示「%s」', async (readiness, text) => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent({ engines: [exampleEngine({ readiness })] })] });

    // Assert
    expect(await within(await screen.findByRole('listitem', { name: 'Claude Code' })).findByText(text)).toBeInTheDocument();
  });

  it('離線的代理寫最後在線的時間；等待配對與停用的各有說明', async () => {
    // Arrange / Act
    await openHostSection({
      agents: [
        exampleHostAgent({ id: 'a1', name: '離線機', online: false, lastSeenAt: new Date(Date.now() - 5 * 60_000).toISOString() }),
        exampleHostAgent({ id: 'a2', name: '新機', state: 'awaiting_pairing', online: false, lastSeenAt: null, engines: [] }),
        exampleHostAgent({ id: 'a3', name: '停用機', state: 'disabled', online: false }),
      ],
    });

    // Assert
    expect(await screen.findByText(/離線（最後在 5 分鐘前）/)).toBeInTheDocument();
    expect(screen.getByText('等待配對')).toBeInTheDocument();
    expect(screen.getByText('還沒配對：在主機上輸入配對碼。碼過期了就用「重新配對」拿新的。')).toBeInTheDocument();
    expect(screen.getByText('已停用')).toBeInTheDocument();
  });
});

describe('主機 AI 代理：憑證到期與警告', () => {
  it('在線、已啟用、沒有警告：寫到期日與會自動換新；沒配對過的不寫', async () => {
    // Arrange / Act
    await openHostSection({
      agents: [
        exampleHostAgent({ credentialExpiresAt: '2027-02-20T08:00:00+00:00' }),
        exampleHostAgent({ id: 'a2', name: '新機', state: 'awaiting_pairing', online: false, engines: [], credentialExpiresAt: null }),
      ],
    });

    // Assert
    expect(await screen.findByText(/機器憑證 .* 到期，會在到期前自動換新/)).toBeInTheDocument();
    expect(screen.getAllByText(/機器憑證/)).toHaveLength(1);
  });

  it('離線時只寫到期日，不承諾會自動換新', async () => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent({ online: false, credentialExpiresAt: '2027-02-20T08:00:00+00:00' })] });

    // Assert
    expect(await screen.findByText(/機器憑證 .* 到期/)).toBeInTheDocument();
    expect(screen.queryByText(/自動換新/)).not.toBeInTheDocument();
  });

  it('已經過期：寫「已於 … 過期」，不寫自動換新', async () => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent({ credentialExpiresAt: '2026-01-05T08:00:00+00:00' })] });

    // Assert
    expect(await screen.findByText(/機器憑證已於 .* 過期/)).toBeInTheDocument();
    expect(screen.queryByText(/自動換新/)).not.toBeInTheDocument();
  });

  it('有警告時不寫到期那一行（和警告矛盾）', async () => {
    // Arrange / Act
    await openHostSection({
      agents: [exampleHostAgent({ credentialExpiresAt: '2027-02-20T08:00:00+00:00', warning: { kind: 'other_machine', machineName: 'evil-pc', at: '2026-11-25T01:00:00+00:00' } })],
    });

    // Assert
    await screen.findByText(/拿這台的憑證登記/);
    expect(screen.queryByText(/機器憑證 /)).not.toBeInTheDocument();
    expect(screen.queryByText(/自動換新/)).not.toBeInTheDocument();
  });

  it.each([
    [{ kind: 'other_machine', machineName: 'evil-pc', at: '2026-11-25T01:00:00+00:00' }, '有另一台電腦（evil-pc）拿這台的憑證登記，已拒絕。憑證可能外流，請重新配對。'],
    [{ kind: 'paired_by_former_owner', machineName: null, at: '2026-11-25T01:00:00+00:00' }, '配對這台的人已經不是擁有者，請由現在的擁有者重新配對。'],
    [{ kind: 'credential_expired', machineName: null, at: '2026-11-25T01:00:00+00:00' }, '憑證過期了，請重新配對。'],
  ] as const)('警告 %#：顯示處理方式，旁邊的「重新配對」拿到新的配對碼', async (warning, text) => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent({ warning })] });
    expect(await screen.findByText(text)).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '重新配對（辦公室主機）' }));

    // Assert
    expect(await screen.findByRole('region', { name: '配對碼' })).toHaveTextContent(PAIRING_CODE);
    expect(backend.pairings).toEqual(['agent-1']);
  });

  it('重新配對之後警告消失（伺服器清掉警告與憑證到期日），快速連按只配對一次', async () => {
    // Arrange
    const warning = { kind: 'credential_expired', machineName: null, at: '2026-11-25T01:00:00+00:00' } as const;
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent({ warning, credentialExpiresAt: '2026-01-05T08:00:00+00:00' })] });
    const button = await screen.findByRole('button', { name: '重新配對（辦公室主機）' });

    // Act
    await user.dblClick(button);

    // Assert
    await waitFor(() => { expect(screen.queryByText('憑證過期了，請重新配對。')).not.toBeInTheDocument(); });
    expect(screen.queryByRole('button', { name: '重新配對（辦公室主機）' })).not.toBeInTheDocument();
    expect(backend.pairings).toEqual(['agent-1']);
  });

  it('同一瞬間連按兩下（畫面還沒更新）也只配對一次：第二組配對碼會讓第一組失效', async () => {
    // Arrange
    const warning = { kind: 'credential_expired', machineName: null, at: '2026-11-25T01:00:00+00:00' } as const;
    const { backend } = await openHostSection({ agents: [exampleHostAgent({ warning, credentialExpiresAt: '2026-01-05T08:00:00+00:00' })] });
    const button = await screen.findByRole('button', { name: '重新配對（辦公室主機）' });

    // Act：兩下在同一個工作裡送出，中間沒有機會重新畫面
    fireEvent.click(button);
    fireEvent.click(button);

    // Assert
    await waitFor(() => { expect(screen.queryByText('憑證過期了，請重新配對。')).not.toBeInTheDocument(); });
    expect(backend.pairings).toEqual(['agent-1']);
  });

  it('第二下落在配對完成、清單還沒重讀之前（機器慢時的連按）也只配對一次', async () => {
    // Arrange：配對之後的重讀先卡住，固定出「配對已完成、警告列還在」的空檔
    const warning = { kind: 'credential_expired', machineName: null, at: '2026-11-25T01:00:00+00:00' } as const;
    const { backend } = await openHostSection({ agents: [exampleHostAgent({ warning, credentialExpiresAt: '2026-01-05T08:00:00+00:00' })] });
    const button = await screen.findByRole('button', { name: '重新配對（辦公室主機）' });
    let release = () => {};
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(http.get('/v1/ai/host', async () => {
      if (backend.pairings.length > 0) await gate;
      return undefined;
    }));

    // Act
    fireEvent.click(button);
    await waitFor(() => { expect(backend.pairings).toHaveLength(1); });
    await settle();
    fireEvent.click(button);
    await settle();
    release();

    // Assert
    expect(await screen.findByRole('region', { name: '配對碼' })).toHaveTextContent(PAIRING_CODE);
    await waitFor(() => { expect(screen.queryByText('憑證過期了，請重新配對。')).not.toBeInTheDocument(); });
    expect(backend.pairings).toEqual(['agent-1']);
  });

  it('沒有警告時沒有「重新配對」鈕（它在「更多」選單裡）', async () => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent({ warning: null })] });
    await screen.findByText('辦公室主機');

    // Assert
    expect(screen.queryByRole('button', { name: '重新配對' })).not.toBeInTheDocument();
  });
});

describe('主機 AI 代理：自動更新', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('每 5 秒重讀一次：代理掉線、排隊的件數變了，畫面跟著變', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { backend } = await openHostSection({ agents: [exampleHostAgent()] });
    expect(await screen.findByText('在線')).toBeInTheDocument();

    // Act
    backend.set({ agents: [exampleHostAgent({ online: false, lastSeenAt: new Date().toISOString() })], sources: [] });
    await vi.advanceTimersByTimeAsync(5_000);

    // Assert
    expect(await screen.findByText(/離線（最後在/)).toBeInTheDocument();
  });
});

describe('主機 AI 代理：新增與配對', () => {
  it('新增代理：顯示一次性的配對碼、期限與要在主機上執行的指令；關上之後碼就不見了', async () => {
    // Arrange
    const { user, add, backend, queryClient } = await openHostSection();

    // Act
    await user.click(add);
    await user.type(await screen.findByRole('textbox', { name: '名稱' }), '機房主機');
    await user.click(screen.getByRole('button', { name: '建立並取得配對碼' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '配對主機代理' });
    expect(backend.createdAgents).toEqual(['機房主機']);
    expect(within(dialog).getByRole('region', { name: '配對碼' })).toHaveTextContent(PAIRING_CODE);
    expect(within(dialog).getByRole('region', { name: '配對的指令' })).toHaveTextContent('docker compose exec -it ai-worker kumi host pair');
    expect(within(dialog).getByText(/前有效，只用得一次/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '複製配對碼' })).toBeInTheDocument();
    // 碼只在對話框裡：查詢與 mutation 的快取、瀏覽器的儲存都沒有。
    expect(serializedCaches(queryClient)).not.toContain(PAIRING_CODE);
    expect(sessionStorage.length).toBe(0);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '完成' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.queryByText(PAIRING_CODE)).not.toBeInTheDocument();
    expect(await screen.findByText('機房主機')).toBeInTheDocument();
    // 登入時記的「上一個登入的人」是唯一會有的鍵，裡面沒有配對碼。
    expect(Object.keys(localStorage).filter((key) => key !== 'session.lastPersonId')).toEqual([]);
    expect(Object.keys(localStorage).map((key) => localStorage.getItem(key)).join()).not.toContain(PAIRING_CODE);
    expect(serializedCaches(queryClient)).not.toContain(PAIRING_CODE);
    // 焦點回到「新增主機代理」。
    expect(screen.getByRole('button', { name: '新增主機代理' })).toHaveFocus();
  });

  it('名稱空白：不送出、說明在欄位下面', async () => {
    // Arrange
    const { user, add, backend } = await openHostSection();

    // Act
    await user.click(add);
    await user.click(await screen.findByRole('button', { name: '建立並取得配對碼' }));

    // Assert
    expect(await screen.findByText('請填名稱')).toBeInTheDocument();
    expect(backend.createdAgents).toHaveLength(0);
  });

  it('伺服器拒絕（例如到上限）：說明留在對話框裡，沒有配對碼', async () => {
    // Arrange
    const { user, add, backend } = await openHostSection();
    backend.rejectNext('create', { status: 422, code: 'ai.settings_invalid', message: '最多 3 台主機代理。' });

    // Act
    await user.click(add);
    await user.type(await screen.findByRole('textbox', { name: '名稱' }), '第四台');
    await user.click(screen.getByRole('button', { name: '建立並取得配對碼' }));

    // Assert
    expect(await screen.findByText('最多 3 台主機代理。')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: '配對碼' })).not.toBeInTheDocument();
  });

  it('更多選單：重新配對給新的配對碼', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()] });
    await screen.findByText('辦公室主機');

    // Act
    await chooseMenuAction(user, '「辦公室主機」的更多動作', '重新配對');

    // Assert
    expect(await screen.findByRole('region', { name: '配對碼' })).toHaveTextContent(PAIRING_CODE);
    expect(backend.pairings).toEqual(['agent-1']);
  });

  it('更多選單：改名稱、停用、移除（要確認）', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()] });
    await screen.findByText('辦公室主機');

    // Act：改名稱
    await chooseMenuAction(user, '「辦公室主機」的更多動作', '改名稱');
    const name = await screen.findByRole('textbox', { name: '名稱' });
    await user.clear(name);
    await user.type(name, '新名稱');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('新名稱')).toBeInTheDocument();
    expect(backend.updatedAgents).toEqual([{ id: 'agent-1', input: { name: '新名稱', enabled: true } }]);

    // Act：停用
    await chooseMenuAction(user, '「新名稱」的更多動作', '停用');
    const disable = await screen.findByRole('dialog', { name: '停用主機代理「新名稱」？' });
    expect(backend.updatedAgents).toHaveLength(1);
    await user.click(within(disable).getByRole('button', { name: '停用' }));

    // Assert
    expect(await screen.findByText('已停用')).toBeInTheDocument();
    expect(backend.updatedAgents[1]?.input).toEqual({ name: '新名稱', enabled: false });

    // Act：移除要先確認
    await chooseMenuAction(user, '「新名稱」的更多動作', '移除');
    const confirm = await screen.findByRole('dialog');
    expect(backend.deletedAgents).toHaveLength(0);
    await user.click(within(confirm).getByRole('button', { name: '移除' }));

    // Assert
    await waitFor(() => {
      expect(backend.deletedAgents).toEqual(['agent-1']);
    });
    expect(await screen.findByText('還沒有主機代理。先新增一台，再到主機上配對。')).toBeInTheDocument();
  });
});

describe('主機 AI 代理：引擎登入', () => {
  it('開始登入 Codex：顯示登入碼、期限、指令與只能用 API 金鑰的說明', async () => {
    // Arrange
    const { user, backend, queryClient } = await openHostSection({ agents: [exampleHostAgent()] });
    await screen.findByText('辦公室主機');

    // Act
    await user.click(screen.getByRole('button', { name: '開始登入 Codex' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '登入主機上的 Codex' });
    expect(serializedCaches(queryClient)).not.toContain(LOGIN_CODE);
    expect(sessionStorage.length).toBe(0);
    expect(backend.logins).toEqual([{ agentId: 'agent-1', engine: 'codex' }]);
    expect(within(dialog).getByRole('region', { name: '登入碼' })).toHaveTextContent(LOGIN_CODE);
    expect(within(dialog).getByRole('region', { name: '登入的指令' })).toHaveTextContent('docker compose exec -it ai-worker kumi host login codex');
    expect(within(dialog).getByText(/只能用 OpenAI API 金鑰登入。ChatGPT 的訂閱帳號依條款不能給全組織共用，登入了會被拒絕並登出。/)).toBeInTheDocument();

    // Act：關掉之後碼不在快取，焦點留在這個引擎那一列（「開始登入」鈕已經因為登入進行中不見了）
    await user.click(within(dialog).getByRole('button', { name: '完成' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(serializedCaches(queryClient)).not.toContain(LOGIN_CODE);
    await waitFor(() => {
      expect(engineRow('Codex')).toHaveFocus();
    });
  });

  it('Claude Code 的指令是 claude-code，說明寫 Anthropic Console 與 Claude 訂閱', async () => {
    // Arrange
    const { user } = await openHostSection({ agents: [exampleHostAgent()] });
    await screen.findByText('辦公室主機');

    // Act
    await user.click(screen.getByRole('button', { name: '重新登入 Claude Code' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '登入主機上的 Claude Code' });
    expect(within(dialog).getByRole('region', { name: '登入的指令' })).toHaveTextContent('kumi host login claude-code');
    expect(within(dialog).getByText(/只能用 Anthropic Console（API 用量計費）登入。Claude 的訂閱帳號/)).toBeInTheDocument();
  });

  it('進行中的登入：顯示進度與「作廢」，進行中時不再給「開始登入」；作廢之後回來', async () => {
    // Arrange
    const login = { id: 'login-9', engine: 'codex' as const, state: 'claimed' as const, expiresAt: '2026-11-24T03:15:00+00:00', error: null, startedBy: { personId: 'p-owner', displayName: '範例擁有者' }, createdAt: '2026-11-24T03:00:00+00:00' };
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent({ logins: [login] })] });

    // Assert
    const codex = await waitFor(() => engineRow('Codex'));
    expect(within(codex).getByRole('status')).toHaveTextContent(/登入進行中：主機已收到登入碼，等待完成登入/);
    expect(within(codex).queryByRole('button', { name: /開始登入/ })).not.toBeInTheDocument();

    // Act
    await user.click(within(codex).getByRole('button', { name: '作廢' }));

    // Assert
    await waitFor(() => {
      expect(backend.cancelledLogins).toEqual(['login-9']);
    });
    expect(await within(engineRow('Codex')).findByRole('button', { name: '開始登入 Codex' })).toBeInTheDocument();
  });

  it('上一次登入失敗：顯示原因，可以再開始', async () => {
    // Arrange
    const failed = { id: 'login-3', engine: 'codex' as const, state: 'failed' as const, expiresAt: '2026-11-24T03:15:00+00:00', error: '登入的是訂閱帳號。', startedBy: { personId: 'p-owner', displayName: '範例擁有者' }, createdAt: '2026-11-24T03:00:00+00:00' };
    await openHostSection({ agents: [exampleHostAgent({ logins: [failed] })] });

    // Assert
    const codex = await waitFor(() => engineRow('Codex'));
    expect(within(codex).getByRole('alert')).toHaveTextContent('上一次登入失敗：登入的是訂閱帳號。');
    expect(within(codex).getByRole('button', { name: '開始登入 Codex' })).toBeInTheDocument();
  });

  it('失敗之後已經成功授權：不再顯示那次失敗的紅字', async () => {
    // Arrange：Codex 先失敗一次，之後的登入成功並授權了。
    const failed = { id: 'login-4', engine: 'codex' as const, state: 'failed' as const, expiresAt: '2026-11-24T03:15:00+00:00', error: '登入的是訂閱帳號。', startedBy: { personId: 'p-owner', displayName: '範例擁有者' }, createdAt: '2026-11-24T03:00:00+00:00' };
    const authorized = exampleEngine({ kind: 'codex', authorizedAt: '2026-11-24T03:05:00+00:00' });
    await openHostSection({ agents: [exampleHostAgent({ engines: [exampleEngine(), authorized], logins: [failed] })] });

    // Assert
    const codex = await waitFor(() => engineRow('Codex'));
    expect(await within(codex).findByText(/授權/)).toBeInTheDocument();
    expect(within(codex).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(codex).queryByText(/上一次登入失敗/)).not.toBeInTheDocument();
  });

  it('代理還沒配對或已停用：不能登入', async () => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent({ state: 'disabled' })] });

    // Assert
    const claude = await waitFor(() => engineRow('Claude Code'));
    expect(within(claude).queryByRole('button', { name: /登入/ })).not.toBeInTheDocument();
  });

  it('已授權的引擎可以登出，要先確認', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()] });
    const claude = await waitFor(() => engineRow('Claude Code'));

    // Act
    await user.click(within(claude).getByRole('button', { name: '登出 Claude Code' }));
    const confirm = await screen.findByRole('dialog');
    expect(backend.logouts).toHaveLength(0);
    await user.click(within(confirm).getByRole('button', { name: '登出' }));

    // Assert
    await waitFor(() => {
      expect(backend.logouts).toEqual([{ agentId: 'agent-1', engine: 'claude_code' }]);
    });
    // 主機還沒回報之前是「登出中」，不是「需要重新授權」，也不再給登出、登入的按鈕（看起來才不像沒成功）。
    expect(await within(engineRow('Claude Code')).findByText('登出中')).toBeInTheDocument();
    expect(within(engineRow('Claude Code')).getByText(/已要求 Claude Code 登出/)).toBeInTheDocument();
    expect(within(engineRow('Claude Code')).queryByRole('button', { name: '登出 Claude Code' })).not.toBeInTheDocument();
    expect(within(engineRow('Claude Code')).queryByRole('button', { name: /登入 Claude Code/ })).not.toBeInTheDocument();
  });

  it('主機回報已經登出之後顯示需要登入、可以重新開始登入', async () => {
    // Arrange
    const loggedOut = exampleEngine({ readiness: 'not_logged_in', loggedIn: false, authorizedAt: null, authorizedBy: null, problem: '還沒登入。' });

    // Act
    await openHostSection({ agents: [exampleHostAgent({ engines: [loggedOut] })] });

    // Assert
    const claude = await waitFor(() => engineRow('Claude Code'));
    expect(within(claude).getByText('需要登入')).toBeInTheDocument();
    expect(within(claude).getByRole('button', { name: '開始登入 Claude Code' })).toBeInTheDocument();
    expect(within(claude).queryByRole('button', { name: '登出 Claude Code' })).not.toBeInTheDocument();
  });

  it('開始登入被拒絕：說明寫在那個引擎底下', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()] });
    backend.rejectNext('login', { status: 422, code: 'ai.host_agent_unavailable', message: '這台代理還沒配對或已停用。' });
    await screen.findByText('辦公室主機');

    // Act
    await user.click(screen.getByRole('button', { name: '開始登入 Codex' }));

    // Assert
    expect(await screen.findByText('這台代理還沒配對或已停用。')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
