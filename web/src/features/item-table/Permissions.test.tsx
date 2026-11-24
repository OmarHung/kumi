import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleMember, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const item = smkItem({ number: 1, title: '首頁設計稿', fields: { blocked: true, summary: '摘要' } });

async function rowWith(permissions: Parameters<typeof smkDetail>[0]) {
  signedInAs(exampleMember, { details: [smkDetail(permissions)] });
  serveItems('SMK', [item]);
  renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  return within(table).getByRole('row', { name: /SMK-1/ });
}

describe('權限', () => {
  it('只能看的人：每一格都是唯讀，勾選也不能改，沒有新增', async () => {
    // Arrange / Act
    const row = await rowWith({ myPermissions: ['item.read'] });

    // Assert
    expect(within(row).queryAllByRole('button')).toHaveLength(0);
    expect(within(row).getByRole('checkbox', { name: '卡住了' })).toBeDisabled();
    expect(within(row).getByText('首頁設計稿')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
  });

  it('能改欄位但不能改狀態的人：狀態是唯讀，其他格子可以編輯', async () => {
    // Arrange / Act
    const row = await rowWith({ myPermissions: ['item.read', 'item.update'] });

    // Assert
    expect(within(row).queryByRole('button', { name: /^狀態/ })).not.toBeInTheDocument();
    expect(within(row).getByText('待辦')).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: '首頁設計稿' })).toBeInTheDocument();
  });

  it('能改狀態但不能改欄位的人：只有狀態可以編輯', async () => {
    // Arrange / Act
    const row = await rowWith({ myPermissions: ['item.read', 'item.transition'] });

    // Assert
    expect(within(row).getAllByRole('button').map((button) => button.getAttribute('aria-label'))).toEqual([
      '狀態：待辦',
    ]);
  });
});
