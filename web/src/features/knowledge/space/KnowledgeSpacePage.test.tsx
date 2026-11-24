import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, ok, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { exampleKnowledgeTree, knowledgeSpaceOf, serveKnowledge } from '../../../test/knowledge';
import { currentPath, renderApp } from '../../../test/renderApp';
import { server } from '../../../test/server';

function openSpace(path: string, options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge(options);
  return { backend, ...renderApp(path) };
}

describe('空間首頁', () => {
  it('寫出空間名稱、簡介、最上層的頁面與最近更新', async () => {
    // Arrange & Act
    openSpace('/knowledge/DOCS');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '團隊文件' })).toBeInTheDocument();
    expect(screen.getByText('操作手冊、會議記錄與決策紀錄。')).toBeInTheDocument();
    const top = screen.getByRole('region', { name: '最上層的頁面' });
    expect(within(top).getAllByRole('link').map((link) => link.textContent)).toEqual(['新人到職', '會議記錄']);
    const recent = screen.getByRole('region', { name: '最近更新' });
    expect(within(recent).getAllByRole('link')).toHaveLength(4);
  });

  it('空間裡沒有頁面：空白狀態，能寫的人有「新增第一頁」', async () => {
    // Arrange & Act
    openSpace('/knowledge/DOCS', { trees: { DOCS: [] } });

    // Assert
    expect(await screen.findByText('還沒有頁面')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增第一頁' })).toBeInTheDocument();
  });

  it('空間不存在：說明找不到並回到知識庫', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/knowledge/spaces/:key/tree', () => failure(404, 'knowledge.space_not_found', '找不到這個空間。')));

    // Act
    renderApp('/knowledge/NOPE');

    // Assert
    expect(await screen.findByRole('heading', { name: '找不到這個空間' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到知識庫' })).toBeInTheDocument();
  });

  it('網址上的代號用小寫也行：換成空間自己的代號', async () => {
    // Arrange：真的伺服器不分大小寫，假的後端分，所以小寫的那一條自己接
    signedInAs(exampleOwner);
    serveKnowledge();
    server.use(http.get('/v1/knowledge/spaces/docs/tree', () => ok({ space: knowledgeSpaceOf(), pages: exampleKnowledgeTree() })));

    // Act
    const { router } = renderApp('/knowledge/docs/2');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '帳號申請' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/knowledge/DOCS/2');
  });

  it('封存的空間：上方說只能看，沒有任何新增的按鈕', async () => {
    // Arrange
    const archived = knowledgeSpaceOf({
      archivedAt: '2026-11-25T03:00:00+00:00',
      myPermissions: { canEdit: false, canComment: false, canManage: true },
    });

    // Act
    openSpace('/knowledge/DOCS', { spaces: [archived] });

    // Assert
    expect(await screen.findByText('這個空間已經封存，只能看。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增頁面' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /底下新增頁面/ })).not.toBeInTheDocument();
  });

  it('綁的專案封存了：說專案已封存、只能看', async () => {
    // Arrange
    const space = knowledgeSpaceOf({
      project: { key: 'SMK', name: '冒煙測試', isArchived: true },
      myPermissions: { canEdit: false, canComment: false, canManage: false },
    });

    // Act
    openSpace('/knowledge/DOCS', { spaces: [space] });

    // Assert
    expect(await screen.findByText('這個空間綁的專案已經封存，只能看。')).toBeInTheDocument();
  });
});
