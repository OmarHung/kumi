import { screen, waitFor, within } from '@testing-library/react';
import { act } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { exampleConnection, serveAiSettings } from '../../test/aiSettings';
import { signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { servePersonalAi } from '../../test/personalAi';
import { currentPath, renderApp } from '../../test/renderApp';

const navigateTo = vi.hoisted(() => vi.fn<(url: string) => void>());
vi.mock('../../lib/navigateTo', () => ({ navigateTo }));

beforeEach(() => {
  navigateTo.mockReset();
});

async function openNewDialog(path = '/ai-settings') {
  signedInAs(exampleOwner);
  const backend = serveAiSettings();
  const personal = servePersonalAi();
  const view = renderApp(path);
  await view.user.click(await screen.findByRole('button', { name: '新增連線' }));
  const dialog = await screen.findByRole('dialog', { name: '新增連線' });
  return { ...view, backend, personal, dialog };
}

describe('AI 設定：允許成員使用自己的 AI 帳號', () => {
  it('說明也提到成員自己電腦上的 Claude Code／Codex', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();

    // Act
    renderApp('/ai-settings');

    // Assert
    const checkbox = await screen.findByRole('checkbox', { name: /允許成員使用自己的 AI 帳號/ });
    expect(checkbox).toHaveAccessibleDescription(/也包括成員自己電腦上的 Claude Code／Codex/);
  });

  it('預設沒勾；勾了之後和「啟用 AI 助理」一起存，存了之後再讀還是勾著', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = serveAiSettings();
    const { user } = renderApp('/ai-settings');
    const checkbox = await screen.findByRole('checkbox', { name: /允許成員使用自己的 AI 帳號/ });
    expect(checkbox).not.toBeChecked();

    // Act
    await user.click(checkbox);
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(backend.savedSettings).toEqual([{ enabled: false, allowPersonalConnections: true, default: null }]);
    expect(backend.current().allowPersonalConnections).toBe(true);
    expect(screen.getByRole('checkbox', { name: /允許成員使用自己的 AI 帳號/ })).toBeChecked();
  });

  it('只勾這一項也算有沒存的修改：離開前先問', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();
    const { user, router } = renderApp('/ai-settings');
    await user.click(await screen.findByRole('checkbox', { name: /允許成員使用自己的 AI 帳號/ }));

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '還有沒儲存的修改' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/ai-settings');
  });
});

describe('連線對話框：OpenRouter', () => {
  it('選 OpenRouter：主要的是「用 OpenRouter 登入」，沒有名稱、金鑰與模型；按了送出組織的 scope 並導到回傳的網址', async () => {
    // Arrange
    const { user, dialog, personal } = await openNewDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));

    // Assert
    expect(within(dialog).queryByLabelText('API 金鑰')).not.toBeInTheDocument();
    expect(within(dialog).queryByLabelText('名稱')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: '讀取模型' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: '儲存' })).not.toBeInTheDocument();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));

    // Assert
    await waitFor(() => {
      expect(navigateTo).toHaveBeenCalledExactlyOnceWith('https://openrouter.ai/auth?callback_url=example&state=s1');
    });
    expect(personal.loginScopes).toEqual(['organization']);
  });

  it('「或貼上 API 金鑰」展開名稱、金鑰與模型，照其他供應商存', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '或貼上 API 金鑰' }));
    await user.type(within(dialog).getByLabelText('API 金鑰'), 'sk-or-v1-abcd');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.savedConnections[0]?.input).toMatchObject({ kind: 'openrouter', name: 'OpenRouter', apiKey: 'sk-or-v1-abcd', baseUrl: null });
    expect(within(dialog).queryByRole('button', { name: '或貼上 API 金鑰' })).not.toBeInTheDocument();
  });

  it('有沒存的輸入：先確認；「回來編輯」不登入，「繼續登入」才登入', async () => {
    // Arrange
    const { user, dialog } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));
    await user.click(within(dialog).getByRole('button', { name: '或貼上 API 金鑰' }));
    await user.type(within(dialog).getByLabelText('API 金鑰'), 'sk-or-v1-abcd');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));
    const confirm = await screen.findByRole('dialog', { name: '先離開去登入 OpenRouter？' });
    await user.click(within(confirm).getByRole('button', { name: '回來編輯' }));

    // Assert
    expect(navigateTo).not.toHaveBeenCalled();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));
    await user.click(within(await screen.findByRole('dialog', { name: '先離開去登入 OpenRouter？' })).getByRole('button', { name: '繼續登入' }));

    // Assert
    await waitFor(() => {
      expect(navigateTo).toHaveBeenCalledOnce();
    });
  });

  it('伺服器回的網址不是 http(s)：不導，說明改貼金鑰', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();
    servePersonalAi({ authorizeUrl: 'javascript:alert(1)' });
    const { user } = renderApp('/ai-settings');
    await user.click(await screen.findByRole('button', { name: '新增連線' }));
    const dialog = await screen.findByRole('dialog', { name: '新增連線' });
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));

    // Assert
    expect(await within(dialog).findByText(/登入網址不能使用/)).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('網址不是 openrouter.ai 的 https：不導，說明改貼金鑰', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();
    servePersonalAi({ authorizeUrl: 'https://openrouter.ai.example.com/auth' });
    const { user } = renderApp('/ai-settings');
    await user.click(await screen.findByRole('button', { name: '新增連線' }));
    const dialog = await screen.findByRole('dialog', { name: '新增連線' });
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));

    // Assert
    expect(await within(dialog).findByText(/登入網址不能使用/)).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('從 bfcache 還原（pageshow persisted）：登入鈕恢復，取消可以關掉對話框', async () => {
    // Arrange
    const { user, dialog } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));
    expect(await within(dialog).findByRole('button', { name: '前往 OpenRouter…' })).toBeInTheDocument();

    // Act
    act(() => {
      window.dispatchEvent(Object.assign(new Event('pageshow'), { persisted: true }));
    });

    // Assert
    expect(await within(dialog).findByRole('button', { name: '用 OpenRouter 登入' })).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: '取消' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('不是從 bfcache 還原的 pageshow：維持前往中', async () => {
    // Arrange
    const { user, dialog } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));
    await within(dialog).findByRole('button', { name: '前往 OpenRouter…' });

    // Act
    act(() => {
      window.dispatchEvent(Object.assign(new Event('pageshow'), { persisted: false }));
    });

    // Assert
    expect(within(dialog).getByRole('button', { name: '前往 OpenRouter…' })).toBeInTheDocument();
  });

  it('開始登入被拒（403）：原因顯示在按鈕下面', async () => {
    // Arrange
    const { user, dialog, personal } = await openNewDialog();
    personal.rejectNext('login', { status: 403, code: 'ai.forbidden', message: '只有管理員可以這樣做。' });
    await user.click(within(dialog).getByRole('button', { name: 'OpenRouter' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '用 OpenRouter 登入' }));

    // Assert
    expect(await within(dialog).findByText('只有管理員可以這樣做。')).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
  });
});

describe('AI 設定：?edit=<連線編號>', () => {
  it('打開那個連線的編輯對話框，網址上的參數拿掉', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings({ connections: [exampleConnection(), exampleConnection({ id: 'c-2', name: '公司的 OpenRouter', kind: 'openrouter', models: [] })] });

    // Act
    const { router } = renderApp('/ai-settings?edit=c-2');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '編輯連線' });
    expect(within(dialog).getByLabelText('名稱')).toHaveValue('公司的 OpenRouter');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/ai-settings');
    });
  });

  it('找不到的編號：不開對話框，參數照樣拿掉', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings({ connections: [exampleConnection()] });

    // Act
    const { router } = renderApp('/ai-settings?edit=gone');

    // Assert
    await screen.findByRole('button', { name: '編輯 Anthropic' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(currentPath(router)).toBe('/ai-settings');
    });
  });
});
