import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { grantOf, serveOAuthGrants } from '../../test/oauth';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const CLAUDE = grantOf();
// 「昨天」要相對於執行測試的這一天：寫死日期的話，過了那一天就變成「前天」（2026-11-24 壞過一次）。
const ONE_DAY_MS = 24 * 60 * 60 * 1000;

const CODEX = grantOf({
  grantId: '55555555-5555-4555-8555-555555555555',
  clientName: 'Codex',
  clientHost: null,
  scopes: ['items:read'],
  lastUsedAt: new Date(Date.now() - ONE_DAY_MS).toISOString(),
});

async function openGrants(initial = [CLAUDE, CODEX]) {
  signedInAs(exampleOwner);
  const backend = serveOAuthGrants(initial);
  const view = renderApp('/account/integrations');
  const section = await screen.findByRole('region', { name: '已授權的 AI 代理' });
  return { ...view, backend, section };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('我的帳號：已授權的 AI 代理', () => {
  it('沒有授權時是一行說明', async () => {
    // Act
    const { section } = await openGrants([]);

    // Assert
    expect(await within(section).findByText('還沒有授權過任何 AI 代理。')).toBeInTheDocument();
  });

  it('列出名稱、提供者（沒驗證的寫「未驗證」）、範圍、最後使用', async () => {
    // Act
    const { section } = await openGrants();

    // Assert
    const list = await within(section).findByRole('list', { name: '已授權的 AI 代理' });
    const [claude, codex] = within(list).getAllByRole('listitem');
    expect(claude).toHaveTextContent('Claude Code');
    expect(claude).toHaveTextContent('client.example');
    expect(claude).toHaveTextContent('還沒用過');
    expect(codex).toHaveTextContent('未驗證');
  });

  it('欄位有說法：授權於日期、最後使用相對時間，範圍逐項列出（同意頁的說法）', async () => {
    // Act
    const { section } = await openGrants();

    // Assert
    const [claude, codex] = within(await within(section).findByRole('list', { name: '已授權的 AI 代理' })).getAllByRole('listitem');
    expect(claude).toHaveTextContent(/授權於 \d{4}/);
    expect(claude).toHaveTextContent('最後使用 還沒用過');
    expect(claude).toHaveTextContent('看專案與項目、建立與修改項目、留言、看專案的設定與變更集、提出變更集（還是要有人核准）');
    expect(claude).not.toHaveTextContent('全部');
    expect(codex).toHaveTextContent('看專案與項目');
    expect(codex).not.toHaveTextContent('建立與修改項目');
    expect(codex).toHaveTextContent(/最後使用 昨天/);
  });

  it('載入失敗：顯示錯誤與重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/account/oauth-grants', () => failure(500, 'server.error', '伺服器忙線中。')));
    const view = renderApp('/account/integrations');
    const { user } = view;
    const section = await screen.findByRole('region', { name: '已授權的 AI 代理' });

    // Act
    const retry = await within(section).findByRole('button', { name: '重試' });

    // Assert
    expect(within(section).getByRole('alert')).toHaveTextContent('載入失敗');
    expect(within(section).queryByRole('list', { name: '已授權的 AI 代理' })).not.toBeInTheDocument();
    serveOAuthGrants([CLAUDE]);
    await user.click(retry);
    expect(await within(section).findByRole('list', { name: '已授權的 AI 代理' })).toBeInTheDocument();
  });

  it('撤銷要先確認；確認後那一列不在了', async () => {
    // Arrange
    const { user, section, backend } = await openGrants();
    await user.click(await within(section).findByRole('button', { name: '撤銷「Claude Code」的授權' }));
    const dialog = await screen.findByRole('dialog', { name: '撤銷「Claude Code」的授權？' });
    expect(within(dialog).getByText('它之後要重新登入授權才能再連。', { exact: true })).toBeInTheDocument();
    expect(dialog).not.toHaveTextContent('撤銷 Claude Code 的授權？它');
    expect(backend.revokes).toEqual([]);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '撤銷授權' }));

    // Assert
    await within(section).findByRole('button', { name: '撤銷「Codex」的授權' });
    expect(within(section).queryByRole('button', { name: '撤銷「Claude Code」的授權' })).not.toBeInTheDocument();
    expect(backend.revokes).toEqual([CLAUDE.grantId]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('撤銷成功後焦點移到下一列的「撤銷」，不掉到 body', async () => {
    // Arrange
    const { user, section } = await openGrants();
    await user.click(await within(section).findByRole('button', { name: '撤銷「Claude Code」的授權' }));
    const dialog = await screen.findByRole('dialog');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '撤銷授權' }));

    // Assert
    const next = await within(section).findByRole('button', { name: '撤銷「Codex」的授權' });
    await vi.waitFor(() => {
      expect(next).toHaveFocus();
    });
  });

  it('撤銷最後一筆後焦點移到區塊的標題', async () => {
    // Arrange
    const { user, section } = await openGrants([CODEX]);
    await user.click(await within(section).findByRole('button', { name: '撤銷「Codex」的授權' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '撤銷授權' }));

    // Assert
    await within(section).findByText('還沒有授權過任何 AI 代理。');
    await vi.waitFor(() => {
      expect(within(section).getByRole('heading', { name: '已授權的 AI 代理' })).toHaveFocus();
    });
  });

  it('取消不撤銷', async () => {
    // Arrange
    const { user, section, backend } = await openGrants();
    await user.click(await within(section).findByRole('button', { name: '撤銷「Codex」的授權' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '取消' }));

    // Assert
    expect(backend.revokes).toEqual([]);
    expect(within(section).getByRole('button', { name: '撤銷「Codex」的授權' })).toBeInTheDocument();
  });

  it('別處先撤銷了（404）也算成功，並說明', async () => {
    // Arrange
    const { user, section } = await openGrants();
    await user.click(await within(section).findByRole('button', { name: '撤銷「Codex」的授權' }));
    // 畫面上還有它，伺服器端已經不在了。
    serveOAuthGrants([CLAUDE]);

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '撤銷授權' }));

    // Assert
    expect(await within(section).findByText('這筆授權已經撤銷了。')).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '撤銷「Codex」的授權' })).not.toBeInTheDocument();
  });
});

describe('我的帳號：連接 Claude Code／Codex', () => {
  const URL_TEXT = `${window.location.origin}/mcp`;

  async function openConnect() {
    signedInAs(exampleOwner);
    serveOAuthGrants([]);
    const view = renderApp('/account/integrations');
    const section = await screen.findByRole('region', { name: '連接 Claude Code／Codex' });
    return { ...view, section };
  }

  it('顯示這個部署的 MCP 網址與兩個指令', async () => {
    // Act
    const { section } = await openConnect();

    // Assert
    expect(within(section).getByText(URL_TEXT)).toBeInTheDocument();
    const claude = within(section).getByRole('region', { name: 'Claude Code 的指令' });
    expect(claude).toHaveTextContent(`claude mcp add --scope user --transport http kumi ${URL_TEXT}`);
    expect(claude).toHaveTextContent('claude mcp login kumi');
    const codex = within(section).getByRole('region', { name: 'Codex 的指令' });
    expect(codex).toHaveTextContent(`codex mcp add kumi --url ${URL_TEXT}`);
    expect(codex).toHaveTextContent('codex mcp login kumi');
    expect(section).not.toHaveTextContent('config.toml');
  });

  it('可以捲動的程式碼區塊是有名稱的 region', async () => {
    // Act
    const { section } = await openConnect();

    // Assert
    expect(within(section).getByRole('region', { name: 'Claude Code 的指令' })).toHaveTextContent('claude mcp add');
    expect(within(section).getByRole('region', { name: 'Codex 的指令' })).toHaveTextContent('codex mcp add');
  });

  it('按「複製」把指令放進剪貼簿並顯示「已複製」', async () => {
    // Arrange
    const { user, section } = await openConnect();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    // Act
    await user.click(within(section).getByRole('button', { name: '複製Claude Code 的指令' }));

    // Assert
    expect(writeText).toHaveBeenCalledWith(`claude mcp add --scope user --transport http kumi ${URL_TEXT}\nclaude mcp login kumi`);
    expect(await within(section).findByText('已複製')).toBeInTheDocument();
  });

  it('剪貼簿不給用時說明可以手動選取', async () => {
    // Arrange
    const { user, section } = await openConnect();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));

    // Act
    await user.click(within(section).getByRole('button', { name: '複製Codex 的指令' }));

    // Assert
    expect(await within(section).findByText(/無法自動複製/)).toBeInTheDocument();
  });
});
