import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { examplePersonalConnection, servePersonalAi } from '../../test/personalAi';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const navigateTo = vi.hoisted(() => vi.fn<(url: string) => void>());
vi.mock('../../lib/navigateTo', () => ({ navigateTo }));

beforeEach(() => {
  navigateTo.mockReset();
});

const ALLOWED = makeAiOptions({ canAddPersonal: true });

describe('AI 帳號：AI 供應商（出現的條件）', () => {
  it('組織不允許、自己也沒有連線與電腦：不是空白頁，只有一塊說明，沒有新增的按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions({ canAddPersonal: false }));
    servePersonalAi();

    // Act
    renderApp('/account/ai');

    // Assert
    const section = await screen.findByRole('region', { name: 'AI 供應商' });
    expect(await within(section).findByText(/組織目前不允許使用自己的 AI 帳號。要用的話請組織的擁有者或管理員在「AI 設定」開啟。/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增 AI 帳號' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: '本機代理' })).not.toBeInTheDocument();
  });

  it('讀不到組織的 AI 設定：說讀不到、可以重試，不說「組織不允許」', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/ai/options', () => failure(500, 'server.error', 'boom')));
    servePersonalAi();
    const { user } = renderApp('/account/ai');
    const section = await screen.findByRole('region', { name: 'AI 供應商' });
    expect(await within(section).findByText('讀不到組織的 AI 設定')).toBeInTheDocument();
    expect(within(section).queryByText(/組織目前不允許/)).not.toBeInTheDocument();
    serveAiOptions(ALLOWED);

    // Act
    await user.click(within(section).getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('button', { name: '新增 AI 帳號' })).toBeInTheDocument();
  });

  it('組織允許：出現，沒有連線時說明，有新增的按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();

    // Act
    renderApp('/account/ai');

    // Assert
    const section = await screen.findByRole('region', { name: 'AI 供應商' });
    expect(await within(section).findByText('還沒有接任何 AI 帳號。')).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '新增 AI 帳號' })).toBeInTheDocument();
  });

  it('組織不允許但自己有連線：照樣列出，說明不允許，只能刪（沒有編輯、新增與重新讀取）', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions({ canAddPersonal: false }));
    const backend = servePersonalAi({ connections: [examplePersonalConnection()] });
    const { user } = renderApp('/account/ai');

    // Act
    const section = await screen.findByRole('region', { name: 'AI 供應商' });

    // Assert
    expect(await within(section).findByText(/組織目前不允許使用自己的 AI 帳號。已經接的還在/)).toBeInTheDocument();
    expect(within(section).getByText('我的 OpenRouter')).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /編輯/ })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '新增 AI 帳號' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /更多動作/ })).not.toBeInTheDocument();

    // Act
    await user.click(within(section).getByRole('button', { name: '刪除 我的 OpenRouter' }));
    await user.click(await screen.findByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(backend.deleted).toEqual(['pc-1']);
    });
    // 刪光之後不是空白頁：換成一塊說明。
    expect(await screen.findByText(/要用的話請組織的擁有者或管理員在「AI 設定」開啟/)).toBeInTheDocument();
    expect(screen.queryByText('我的 OpenRouter')).not.toBeInTheDocument();
  });
});

describe('AI 帳號：AI 供應商（清單與動作）', () => {
  it('每個連線列出名稱、金鑰的最後四個字、開放幾個模型與本月用量', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi({ connections: [examplePersonalConnection()] });

    // Act
    renderApp('/account/ai');

    // Assert
    const section = await screen.findByRole('region', { name: 'AI 供應商' });
    expect(await within(section).findByText('金鑰 …f9e8 · 開放 1 個模型')).toBeInTheDocument();
    expect(within(section).getByText(/本月 12 則回覆/)).toBeInTheDocument();
  });

  it('更多：重新讀取模型、刪除（打的是自己的端點）', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    const backend = servePersonalAi({ connections: [examplePersonalConnection()] });
    const { user } = renderApp('/account/ai');
    await screen.findByRole('region', { name: 'AI 供應商' });

    // Act
    await user.click(await screen.findByRole('button', { name: '「我的 OpenRouter」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '重新讀取模型' }));

    // Assert
    expect(await screen.findByText(/已重新讀取/)).toBeInTheDocument();
    expect(backend.refreshed).toEqual(['pc-1']);

    // Act
    await user.click(screen.getByRole('button', { name: '「我的 OpenRouter」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    await user.click(await screen.findByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(backend.deleted).toEqual(['pc-1']);
    });
  });

  it('新增：同一個連線對話框，存到自己的端點；存了之後「成員能選的 AI」重新讀', async () => {
    // Arrange
    signedInAs(exampleOwner);
    let optionsReads = 0;
    const backend = servePersonalAi();
    server.use(
      http.get('/v1/ai/options', () => {
        optionsReads += 1;
        return ok(ALLOWED);
      }),
    );
    const { user } = renderApp('/account/ai');
    await user.click(await screen.findByRole('button', { name: '新增 AI 帳號' }));
    const dialog = await screen.findByRole('dialog', { name: '新增 AI 帳號' });
    await waitFor(() => {
      expect(optionsReads).toBeGreaterThan(0);
    });
    const readsBefore = optionsReads;

    // Act
    await user.type(within(dialog).getByLabelText('API 金鑰'), 'sk-ant-abcd');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.savedConnections).toHaveLength(1);
    expect(backend.savedConnections[0]?.input).toMatchObject({ name: 'Anthropic', apiKey: 'sk-ant-abcd' });
    expect(await screen.findByText('Anthropic')).toBeInTheDocument();
    await waitFor(() => {
      expect(optionsReads).toBeGreaterThan(readsBefore);
    });
  });

  it('新增時組織不允許（403 ai.personal_disabled）：原因顯示在對話框裡', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    const backend = servePersonalAi();
    backend.rejectNext('connection', { status: 403, code: 'ai.personal_disabled', message: '組織目前不允許使用自己的 AI 帳號。' });
    const { user } = renderApp('/account/ai');
    await user.click(await screen.findByRole('button', { name: '新增 AI 帳號' }));
    const dialog = await screen.findByRole('dialog', { name: '新增 AI 帳號' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('組織目前不允許使用自己的 AI 帳號。')).toBeInTheDocument();
  });

  it('新增時選 OpenRouter：登入送出的 scope 是個人的', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    const backend = servePersonalAi();
    const { user } = renderApp('/account/ai');
    await user.click(await screen.findByRole('button', { name: '新增 AI 帳號' }));
    const dialog = await screen.findByRole('dialog', { name: '新增 AI 帳號' });
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));

    // Assert
    await waitFor(() => {
      expect(navigateTo).toHaveBeenCalledOnce();
    });
    expect(backend.loginScopes).toEqual(['personal']);
  });
});

describe('我的帳號：對話框標題', () => {
  it('個人的對話框叫「新增 AI 帳號」，不叫連線', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi();
    const { user } = renderApp('/account/ai');

    // Act
    await user.click(await screen.findByRole('button', { name: '新增 AI 帳號' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '新增 AI 帳號' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: '新增連線' })).not.toBeInTheDocument();
  });
});

describe('我的帳號：?aiConnection=<連線編號>', () => {
  it('打開那個連線的編輯對話框，網址上的參數拿掉', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(ALLOWED);
    servePersonalAi({ connections: [examplePersonalConnection(), examplePersonalConnection({ id: 'pc-2', name: '另一個' })] });

    // Act
    const { router } = renderApp('/account/ai?aiConnection=pc-2');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '編輯 AI 帳號' });
    expect(within(dialog).getByLabelText('名稱')).toHaveValue('另一個');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/account/ai');
    });
  });
});
