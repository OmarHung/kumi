import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { exampleConnection, serveAiSettings } from '../../test/aiSettings';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { servePersonalAi } from '../../test/personalAi';
import { currentPath, renderApp } from '../../test/renderApp';

describe('用 OpenRouter 登入回來', () => {
  it('帶著 code 與 state：送給伺服器，個人的連線導到 AI 帳號頁並打開編輯對話框', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions({ canAddPersonal: true }));
    const backend = servePersonalAi();

    // Act
    const { router } = renderApp('/ai/openrouter/callback?code=abc&state=xyz');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '編輯 API 金鑰' });
    expect(within(dialog).getByLabelText('名稱')).toHaveValue('我的 OpenRouter');
    expect(backend.completions).toEqual([{ code: 'abc', state: 'xyz' }]);
    await waitFor(() => {
      expect(currentPath(router)).toBe('/account/ai');
    });
  });

  it('組織的連線導到 AI 設定並打開編輯對話框；用的是 replace（上一頁不會回到這個網址）', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings({ connections: [exampleConnection({ id: 'pc-login-1', name: 'OpenRouter', kind: 'openrouter' })] });
    servePersonalAi({ completedScope: 'organization' });

    // Act
    const { router } = renderApp('/ai/openrouter/callback?code=abc&state=xyz');

    // Assert
    expect(await screen.findByRole('dialog', { name: '編輯 API 金鑰' })).toBeInTheDocument();
    await waitFor(() => {
      expect(currentPath(router)).toBe('/ai-settings');
    });
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('只送一次（code 只能用一次），StrictMode 掛上兩次也一樣', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions({ canAddPersonal: true }));
    const backend = servePersonalAi();

    // Act
    renderApp('/ai/openrouter/callback?code=abc&state=xyz', { strict: true });

    // Assert
    await screen.findByRole('dialog', { name: '編輯 API 金鑰' });
    expect(backend.completions).toHaveLength(1);
  });

  it('沒有 code（OpenRouter 拒絕或取消）：不送請求，說沒有完成並給回去的連結', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = servePersonalAi();

    // Act
    renderApp('/ai/openrouter/callback?state=xyz');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '沒有完成 OpenRouter 的登入' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到 AI 助理設定' })).toHaveAttribute('href', '/ai-settings');
    expect(screen.getByRole('link', { name: '回到我的 AI 助理' })).toHaveAttribute('href', '/account/ai');
    expect(backend.completions).toHaveLength(0);
  });

  it('一般成員失敗：只有回到我的 AI 助理', async () => {
    // Arrange
    signedInAs(exampleMember);
    servePersonalAi();

    // Act
    renderApp('/ai/openrouter/callback');

    // Assert
    await screen.findByRole('heading', { level: 1, name: '沒有完成 OpenRouter 的登入' });
    expect(screen.queryByRole('link', { name: '回到 AI 助理設定' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到我的 AI 助理' })).toBeInTheDocument();
  });

  it('伺服器說登入失敗（422）：顯示原因與回去的連結，不導走', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = servePersonalAi();
    backend.rejectNext('complete', { status: 422, code: 'ai.login_failed', message: 'OpenRouter 的登入沒有完成：登入已經過期。' });

    // Act
    const { router } = renderApp('/ai/openrouter/callback?code=abc&state=xyz');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '無法完成登入' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('登入已經過期');
    expect(screen.getByRole('link', { name: '回到我的 AI 助理' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('OpenRouter 的登入沒有完成：登入已經過期。');
    expect(screen.queryByRole('heading', { name: 'OpenRouter 的登入沒有完成' })).not.toBeInTheDocument();
    // 送出之後 code 與 state 就不在網址上了（不留在瀏覽紀錄、重新整理不會再送一次）。
    expect(currentPath(router)).toBe('/ai/openrouter/callback');
  });

  it('429：說太頻繁', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = servePersonalAi();
    backend.rejectNext('complete', { status: 429, code: 'rate_limited', message: 'x' });

    // Act
    renderApp('/ai/openrouter/callback?code=abc&state=xyz');

    // Assert
    expect(await screen.findByText('請求太頻繁，請稍後再試。')).toBeInTheDocument();
  });

  it('不在側欄的版面裡（沒有側欄的導覽）', async () => {
    // Arrange
    signedInAs(exampleOwner);
    servePersonalAi();

    // Act
    renderApp('/ai/openrouter/callback');

    // Assert
    await screen.findByRole('heading', { level: 1, name: '沒有完成 OpenRouter 的登入' });
    expect(screen.queryByRole('link', { name: '我的工作' })).not.toBeInTheDocument();
  });
});
