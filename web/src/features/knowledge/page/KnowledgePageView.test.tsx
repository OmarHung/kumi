import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { knowledgeSpaceOf, pageNodeOf, serveKnowledge } from '../../../test/knowledge';
import { renderApp } from '../../../test/renderApp';
import { server } from '../../../test/server';

const BODY = '## 申請流程\n\n到 [入口](https://example.com/portal) 填表。';

function openPage(path: string, options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge({ bodies: { 'DOCS/2': BODY }, ...options });
  return { backend, ...renderApp(path) };
}

const readOnlySpace = knowledgeSpaceOf({ myPermissions: { canEdit: false, canComment: false, canManage: false } });

describe('看一頁', () => {
  it('標題是 h1，內容照 Markdown 畫，連結是連結', async () => {
    // Arrange & Act
    openPage('/knowledge/DOCS/2');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '帳號申請' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: '申請流程' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '入口' })).toHaveAttribute('href', 'https://example.com/portal');
  });

  it('頁列的位置：知識庫、空間、上層們，各自連到那裡，最後是這一頁', async () => {
    // Arrange & Act
    openPage('/knowledge/DOCS/2');

    // Assert
    const trail = await screen.findByRole('navigation', { name: '位置' });
    expect(within(trail).getByRole('link', { name: '知識庫' })).toHaveAttribute('href', '/knowledge');
    expect(within(trail).getByRole('link', { name: '團隊文件' })).toHaveAttribute('href', '/knowledge/DOCS');
    expect(within(trail).getByRole('link', { name: '新人到職' })).toHaveAttribute('href', '/knowledge/DOCS/1');
    expect(within(trail).getByText('帳號申請')).toHaveAttribute('aria-current', 'page');
  });

  it('標題下面一行：最後是誰更新的', async () => {
    // Arrange & Act
    openPage('/knowledge/DOCS/2');

    // Assert
    expect(await screen.findByText(/最後由 範例使用者 更新於/)).toBeInTheDocument();
  });

  it('自己設了限制：寫「只有指定的人看得到」', async () => {
    // Arrange
    const tree = [pageNodeOf({ number: 1, title: '人事', isRestricted: true })];

    // Act
    openPage('/knowledge/DOCS/1', { trees: { DOCS: tree } });

    // Assert
    expect(await screen.findByText(/限制：只有指定的人看得到/)).toBeInTheDocument();
  });

  it('繼承來的限制：寫繼承自哪一頁，連到那一頁', async () => {
    // Arrange
    const tree = [pageNodeOf({ number: 1, title: '人事', isRestricted: true }), pageNodeOf({ number: 2, title: '考核', parentId: 'page-1' })];

    // Act
    openPage('/knowledge/DOCS/2', { trees: { DOCS: tree } });

    // Assert
    expect(await screen.findByText(/限制：繼承自/)).toBeInTheDocument();
    const note = screen.getByText(/限制：繼承自/);
    expect(within(note).getByRole('link', { name: '「人事」' })).toHaveAttribute('href', '/knowledge/DOCS/1');
  });

  it('找不到的頁面：說明並回到空間首頁，左邊的樹照舊', async () => {
    // Arrange & Act
    openPage('/knowledge/DOCS/99');

    // Assert
    expect(await screen.findByRole('heading', { name: '找不到這一頁' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到空間首頁' })).toHaveAttribute('href', '/knowledge/DOCS');
    expect(screen.getByRole('tree', { name: '頁面樹' })).toBeInTheDocument();
  });

  it('網址上的編號不合格（0、文字）：當成找不到，不發請求問那一頁', async () => {
    // Arrange & Act
    const { backend } = openPage('/knowledge/DOCS/abc');

    // Assert
    expect(await screen.findByRole('heading', { name: '找不到這一頁' })).toBeInTheDocument();
    expect(backend.requests).toHaveLength(0);
  });

  it('沒有 canEdit：沒有「編輯」，空的頁面只說還沒有內容', async () => {
    // Arrange & Act
    openPage('/knowledge/DOCS/3', { spaces: [readOnlySpace] });

    // Assert
    expect(await screen.findByText('這一頁還沒有內容。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '編輯' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '開始寫這一頁' })).not.toBeInTheDocument();
  });

  it('能改的人：空的頁面整塊是「開始寫這一頁」', async () => {
    // Arrange
    const { user } = openPage('/knowledge/DOCS/3');

    // Act
    await user.click(await screen.findByRole('button', { name: /開始寫這一頁/ }));

    // Assert
    expect(await screen.findByRole('textbox', { name: '標題' })).toHaveValue('開發環境');
  });

  it('讀不到（伺服器錯誤）：說明並給重試，左邊的樹照舊', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge();
    server.use(http.get('/v1/knowledge/spaces/:key/pages/:number', () => failure(500, 'server.error', '伺服器出了問題。')));

    // Act
    renderApp('/knowledge/DOCS/2');

    // Assert
    expect(await screen.findByText('頁面載入失敗')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '重試' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('tree', { name: '頁面樹' })).toBeInTheDocument();
    });
  });
});
