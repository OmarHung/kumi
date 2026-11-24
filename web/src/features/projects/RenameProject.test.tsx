import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { serveProjectLifecycle } from '../../test/projects';
import { renderApp } from '../../test/renderApp';

async function openRenameDialog(options: Parameters<typeof serveProjectLifecycle>[1] = {}) {
  const detail = agyDetail();
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const backend = serveProjectLifecycle(detail, options);
  const view = renderApp('/projects/AGY');
  await screen.findByRole('heading', { level: 1, name: '接案測試' });
  await view.user.click(screen.getByRole('button', { name: '專案的更多動作' }));
  await view.user.click(await screen.findByRole('menuitem', { name: '重新命名…' }));
  const dialog = await screen.findByRole('dialog', { name: '重新命名專案' });
  return { ...view, backend, dialog };
}

describe('專案改名', () => {
  it('對話框預設是目前的名稱；改好按「儲存」之後，頁列、側欄與麵包屑都換成新的名稱', async () => {
    // Arrange
    const { user, backend, dialog } = await openRenameDialog();
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(name).toHaveValue('接案測試');

    // Act
    await user.clear(name);
    await user.type(name, '接案測試（第二期）');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '接案測試（第二期）' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(backend.renames).toEqual(['接案測試（第二期）']);
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    expect(await within(nav).findByRole('link', { name: /接案測試（第二期）/ })).toBeInTheDocument();
  });

  it('名稱前後的空白會去掉再送出', async () => {
    // Arrange
    const { user, backend, dialog } = await openRenameDialog();
    const name = within(dialog).getByRole('textbox', { name: '名稱' });

    // Act
    await user.clear(name);
    await user.type(name, '  新名稱  ');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => { expect(backend.renames).toEqual(['新名稱']); });
  });

  it('名稱空白：在欄位旁說明，不送出請求', async () => {
    // Arrange
    const { user, backend, dialog } = await openRenameDialog();
    const name = within(dialog).getByRole('textbox', { name: '名稱' });

    // Act
    await user.clear(name);
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveAccessibleDescription('請輸入專案名稱。');
    expect(backend.renames).toEqual([]);
  });

  it('伺服器回 422：訊息放在名稱欄位旁，對話框留著、焦點回到名稱', async () => {
    // Arrange
    const { user, dialog } = await openRenameDialog({
      renameFailure: { status: 422, code: 'project.name_too_long', message: '名稱太長了。' },
    });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });

    // Act
    await user.type(name, '加幾個字');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => { expect(name).toHaveAccessibleDescription('名稱太長了。'); });
    expect(screen.getByRole('dialog', { name: '重新命名專案' })).toBeInTheDocument();
    expect(name).toHaveFocus();
  });

  it('名稱沒改就按「儲存」：直接關上，不送請求', async () => {
    // Arrange
    const { user, backend, dialog } = await openRenameDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(backend.renames).toEqual([]);
  });

  it('按「取消」關上對話框，名稱不變、焦點回到「更多」', async () => {
    // Arrange
    const { user, backend, dialog } = await openRenameDialog();

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '亂打的字');
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(backend.renames).toEqual([]);
    expect(screen.getByRole('heading', { level: 1, name: '接案測試' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '專案的更多動作' })).toHaveFocus();
  });
});
