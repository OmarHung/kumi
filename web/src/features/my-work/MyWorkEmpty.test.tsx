import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

describe('我的工作沒有項目時給下一步', () => {
  it('連到專案：「到專案看看」', async () => {
    // Arrange
    signedInAs(exampleMember, { myWork: [] });
    const { user, router } = renderApp('/my-work');
    const main = within(await screen.findByRole('main'));
    await main.findByText('目前沒有你負責的工作');

    // Act
    await user.click(main.getByRole('link', { name: '到專案看看' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects');
    });
  });

  it('一般成員不能開專案：沒有「新增專案」', async () => {
    // Arrange & Act
    signedInAs(exampleMember, { myWork: [] });
    renderApp('/my-work');
    const main = within(await screen.findByRole('main'));
    await main.findByText('目前沒有你負責的工作');

    // Assert
    expect(main.queryByRole('button', { name: '新增專案' })).not.toBeInTheDocument();
  });

  it('組織的管理員多一顆「新增專案」：當場打開新增專案的對話框，關上後焦點回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { myWork: [] });
    const { user } = renderApp('/my-work');
    const main = within(await screen.findByRole('main'));
    await main.findByText('目前沒有你負責的工作');
    const create = main.getByRole('button', { name: '新增專案' });

    // Act
    await user.click(create);
    const dialog = await screen.findByRole('dialog', { name: '新增專案' });
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog', { name: '新增專案' })).not.toBeInTheDocument();
    expect(create).toHaveFocus();
  });
});
