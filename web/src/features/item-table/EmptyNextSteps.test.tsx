import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Person } from '../../api/types';
import { signedInAs } from '../../test/api';
import { exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin, smkDetail } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { serveMembers } from '../../test/members';
import { renderApp } from '../../test/renderApp';

const people: Person[] = [
  { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false },
  { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false },
];

async function openEmptyProject(detail = smkDetail(), path = '/projects/SMK') {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('SMK', []);
  serveMembers(detail, { people });
  const view = renderApp(path);
  await screen.findByText(/這個專案還沒有項目|沒有符合條件的項目/);
  const empty = within(screen.getByRole('region', { name: '項目' }));
  return { ...view, empty };
}

describe('新專案沒有項目時給下一步', () => {
  it('能管理專案的人有「加入成員」：打開成員對話框，關上後焦點回到這顆按鈕', async () => {
    // Arrange
    const { user, empty } = await openEmptyProject();
    const add = empty.getByRole('button', { name: '加入成員' });

    // Act
    await user.click(add);
    const dialog = await screen.findByRole('dialog', { name: '成員' });
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));

    // Assert
    expect(screen.queryByRole('dialog', { name: '成員' })).not.toBeInTheDocument();
    expect(add).toHaveFocus();
  });

  it('看得到設定的人有「看看流程設定」，連到設定頁的流程', async () => {
    // Arrange & Act
    const { empty } = await openEmptyProject();

    // Assert
    expect(empty.getByRole('link', { name: '看看流程設定' })).toHaveAttribute('href', '/projects/SMK/settings/workflows');
    expect(empty.getByText('在上方輸入標題，按「新增」建立第一個項目。')).toBeInTheDocument();
  });

  it('照權限：不能管理專案、看不到設定時兩個都沒有', async () => {
    // Arrange & Act
    const { empty } = await openEmptyProject(smkDetail({ myPermissions: ['item.read', 'item.create'] }));

    // Assert
    expect(empty.queryByRole('button', { name: '加入成員' })).not.toBeInTheDocument();
    expect(empty.queryByRole('link', { name: '看看流程設定' })).not.toBeInTheDocument();
  });

  it('照權限：只看得到設定時只有「看看流程設定」', async () => {
    // Arrange & Act
    const { empty } = await openEmptyProject(smkDetail({ myPermissions: ['item.read', 'blueprint.read'] }));

    // Assert
    expect(empty.queryByRole('button', { name: '加入成員' })).not.toBeInTheDocument();
    expect(empty.getByRole('link', { name: '看看流程設定' })).toBeInTheDocument();
  });

  it('篩選後沒有結果時只給「清除條件」，不給新專案的下一步', async () => {
    // Arrange & Act
    const { empty } = await openEmptyProject(smkDetail(), '/projects/SMK?q=找不到的字');

    // Assert
    expect(empty.getByRole('button', { name: '清除條件' })).toBeInTheDocument();
    expect(empty.queryByRole('button', { name: '加入成員' })).not.toBeInTheDocument();
    expect(empty.queryByRole('link', { name: '看看流程設定' })).not.toBeInTheDocument();
  });
});
