import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { signedInAs } from '../../test/api';
import type { SessionUser } from '../../api/types';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { servePersonalAi } from '../../test/personalAi';
import { renderApp } from '../../test/renderApp';

const exampleAdmin: SessionUser = { ...exampleOwner, role: 'admin', displayName: '範例管理員' };

async function openAiAccounts() {
  serveAiOptions(makeAiOptions({ canAddPersonal: true }));
  servePersonalAi();
  renderApp('/account/ai');
  return screen.findByRole('region', { name: 'AI 的四種接法' });
}

describe('AI 的四種接法', () => {
  it('列與欄的標題說清楚誰能用、用什麼登入；AI 帳號頁的兩格是目前所在，連到各自那一塊', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    const map = await openAiAccounts();

    // Assert
    expect(within(map).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['誰能用', 'API 金鑰', 'Claude Code／Codex']);
    expect(within(map).getAllByRole('rowheader').map((header) => header.textContent)).toEqual(['全組織共用', '只有自己']);
    expect(within(map).getByRole('link', { name: '我的 API 金鑰' })).toHaveAttribute('href', '/account/ai#ai-keys');
    expect(within(map).getByRole('link', { name: '我的 API 金鑰' })).toHaveAttribute('aria-current', 'page');
    expect(within(map).getByRole('link', { name: '本機代理' })).toHaveAttribute('aria-current', 'page');
  });

  it('一般成員：組織的兩格沒有連結，寫由誰設定', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    const map = await openAiAccounts();

    // Assert
    expect(within(map).queryByRole('link', { name: '組織的 API 金鑰' })).not.toBeInTheDocument();
    expect(within(map).queryByRole('link', { name: '主機 AI 代理' })).not.toBeInTheDocument();
    expect(within(map).getByText('由組織管理員設定')).toBeInTheDocument();
    expect(within(map).getByText('由組織擁有者設定')).toBeInTheDocument();
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
