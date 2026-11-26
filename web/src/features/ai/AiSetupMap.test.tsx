import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { signedInAs } from '../../test/api';
import type { SessionUser } from '../../api/types';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { servePersonalAi } from '../../test/personalAi';
import { renderApp } from '../../test/renderApp';
import { serveAiSettings } from '../../test/aiSettings';
import { serveHostReads } from '../host-agents/hostAgentsTestKit';

const exampleAdmin: SessionUser = { ...exampleOwner, role: 'admin', displayName: '範例管理員' };

async function openAiAccounts() {
  serveAiOptions(makeAiOptions({ canAddPersonal: true }));
  servePersonalAi();
  renderApp('/account/ai');
  return screen.findByRole('region', { name: /^AI 的(四|兩)種接法$/ });
}

describe('AI 的四種接法', () => {
  it('列與欄的標題說清楚誰能用、用什麼登入；AI 帳號頁的兩格是目前所在，連到各自那一塊', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    const map = await openAiAccounts();

    // Assert
    expect(within(map).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['誰能用', 'API 金鑰', 'Claude Code／Codex']);
    expect(within(map).getAllByRole('rowheader').map((header) => header.textContent)).toEqual(['只有自己']);
    expect(within(map).getByRole('link', { name: '我的 API 金鑰' })).toHaveAttribute('href', '/account/ai#ai-keys');
    expect(within(map).getByRole('link', { name: '我的 API 金鑰' })).toHaveAttribute('aria-current', 'page');
    expect(within(map).getByRole('link', { name: '本機代理' })).toHaveAttribute('aria-current', 'page');
  });

  it('我的 AI 助理頁是精簡版：只有每一格的費用那一行，沒有好處、代價與建議', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    const map = await openAiAccounts();

    // Assert
    expect(within(map).queryByText(/好處：/)).not.toBeInTheDocument();
    expect(within(map).queryByText(/^建議：/)).not.toBeInTheDocument();
    expect(within(map).getByText('按用量計費，算在自己的供應商帳號；要組織允許')).toBeInTheDocument();
  });

  it('AI 助理設定頁是完整版：每一格寫好處與代價，說明給選擇的建議', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();
    serveHostReads();

    // Act
    renderApp('/ai-settings');
    const map = await screen.findByRole('region', { name: 'AI 的四種接法' });

    // Assert
    expect(within(map).getByText(/^建議：全組織共用先開組織的 API 金鑰/)).toBeInTheDocument();
    const hostCell = within(map).getByRole('link', { name: '主機 AI 代理' }).closest('td');
    expect(hostCell).toHaveTextContent('好處：成員不用設定，用的是 Claude Code／Codex 本身');
    expect(hostCell).toHaveTextContent('代價：一次只跑一則、其他人排隊；要有人維護主機上的 ai-worker');
  });

  it('一般成員：沒有全組織共用那一列（兩格都設定不了），只有「只有自己」', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    const map = await openAiAccounts();

    // Assert
    expect(within(map).getAllByRole('rowheader').map((header) => header.textContent)).toEqual(['只有自己']);
    expect(within(map).queryByText('組織的 API 金鑰')).not.toBeInTheDocument();
    expect(within(map).queryByText('主機 AI 代理')).not.toBeInTheDocument();
    expect(within(map).queryByText(/由組織/)).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'AI 的兩種接法' })).toBeInTheDocument();
  });

  it('管理員：組織的 API 金鑰有連結，主機 AI 代理只有擁有者能設定', async () => {
    // Arrange
    signedInAs(exampleAdmin);

    // Act
    const map = await openAiAccounts();

    // Assert
    expect(within(map).getByRole('link', { name: '組織的 API 金鑰' })).toHaveAttribute('href', '/ai-settings');
    expect(within(map).queryByRole('link', { name: '主機 AI 代理' })).not.toBeInTheDocument();
    expect(within(map).getByText('由組織擁有者設定')).toBeInTheDocument();
  });
});
