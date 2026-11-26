import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AiSettings } from '../../api/aiTypes';
import { signedInAs } from '../../test/api';
import { exampleConnection, serveAiSettings } from '../../test/aiSettings';
import { exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

// 審查（2026-11-23）找到的問題的測試：儲存結果不推版面、連線列的動作、對話框的焦點與關閉確認。

const SAVED: Partial<AiSettings> = {
  enabled: true,
  default: { connectionId: 'c-1', model: 'claude-opus-5-5', effort: 'high' },
  connections: [exampleConnection()],
};

async function openPage(initial: Partial<AiSettings> = {}) {
  signedInAs(exampleOwner);
  const backend = serveAiSettings(initial);
  const view = renderApp('/ai-settings');
  const save = await screen.findByRole('button', { name: '儲存' });
  return { ...view, backend, save };
}

describe('M3：儲存的結果不推動版面', () => {
  it('「已儲存。」與失敗的原因都和「儲存」按鈕在同一列', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SAVED);
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));

    // Act
    await user.click(save);

    // Assert
    const saved = await screen.findByText('已儲存。');
    expect(saved.parentElement).toBe(save.parentElement);

    // Act
    backend.rejectNext('settings', { status: 422, code: 'ai.settings_invalid', message: '預設的模型沒有開放。' });
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));
    await user.click(save);

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('預設的模型沒有開放。');
    expect(alert.parentElement).toBe(save.parentElement);
  });
});

describe('M4：連線列的動作', () => {
  it('列上只有「編輯」與「更多」；重新讀取與刪除在更多選單裡，刪除最後', async () => {
    // Arrange
    const { user } = await openPage(SAVED);

    // Assert
    expect(screen.getByRole('button', { name: '編輯 Anthropic' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '刪除 Anthropic' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '重新讀取 Anthropic 的模型' })).not.toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '「Anthropic」的更多動作' }));

    // Assert
    const items = await screen.findAllByRole('menuitem');
    expect(items.map((item) => item.textContent)).toEqual(['重新讀取模型', '刪除']);
  });

  it('從選單刪除：確認對話框用紅色實心的確認鈕', async () => {
    // Arrange
    const { user } = await openPage(SAVED);
    await user.click(screen.getByRole('button', { name: '「Anthropic」的更多動作' }));

    // Act
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '刪除 API 金鑰「Anthropic」？' });
    expect(within(dialog).getByRole('button', { name: '刪除' }).className).toMatch(/danger/);
  });

  it('從選單重新讀取：結果用提示說明', async () => {
    // Arrange
    const { user } = await openPage(SAVED);
    await user.click(screen.getByRole('button', { name: '「Anthropic」的更多動作' }));

    // Act
    await user.click(await screen.findByRole('menuitem', { name: '重新讀取模型' }));

    // Assert
    expect(await screen.findByText(/已重新讀取/)).toBeInTheDocument();
  });
});

describe('L10：連線列', () => {
  it('名稱和供應商名稱一樣時不重複寫供應商；不一樣照寫', async () => {
    // Arrange / Act
    await openPage({ connections: [exampleConnection(), exampleConnection({ id: 'c-2', name: '備用帳號' })] });

    // Assert
    const rows = screen.getAllByRole('listitem').filter((row) => within(row).queryByRole('button', { name: /^編輯 / }) !== null);
    expect(within(rows[0] as HTMLElement).getAllByText('Anthropic')).toHaveLength(1);
    expect(within(rows[1] as HTMLElement).getByText('備用帳號')).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('Anthropic')).toBeInTheDocument();
  });
});

describe('L5：新增連線的焦點', () => {
  it('打開時焦點在「名稱」', async () => {
    // Arrange
    const { user } = await openPage();

    // Act
    await user.click(screen.getByRole('button', { name: '新增 API 金鑰' }));

    // Assert
    await screen.findByRole('dialog', { name: '新增 API 金鑰' });
    expect(screen.getByRole('textbox', { name: '名稱' })).toHaveFocus();
  });
});

describe('L4：關閉有沒存的輸入的對話框', () => {
  it('填了金鑰按 Esc：先確認；留下來還在、放棄才關掉', async () => {
    // Arrange
    const { user } = await openPage();
    await user.click(screen.getByRole('button', { name: '新增 API 金鑰' }));
    await screen.findByRole('dialog', { name: '新增 API 金鑰' });
    await user.type(screen.getByLabelText('API 金鑰'), 'sk-secret');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    const confirm = await screen.findByRole('dialog', { name: '放棄還沒存的輸入？' });
    await user.click(within(confirm).getByRole('button', { name: '繼續編輯' }));
    expect(screen.getByRole('dialog', { name: '新增 API 金鑰' })).toBeInTheDocument();
    expect(screen.getByLabelText('API 金鑰')).toHaveValue('sk-secret');

    // Act
    await user.keyboard('{Escape}');
    await user.click(within(await screen.findByRole('dialog', { name: '放棄還沒存的輸入？' })).getByRole('button', { name: '放棄並關閉' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('什麼都沒改按 Esc：直接關掉', async () => {
    // Arrange
    const { user } = await openPage();
    await user.click(screen.getByRole('button', { name: '新增 API 金鑰' }));
    await screen.findByRole('dialog', { name: '新增 API 金鑰' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
