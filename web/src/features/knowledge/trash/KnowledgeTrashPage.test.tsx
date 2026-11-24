import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { KnowledgePage, KnowledgeTrashEntry } from '../../../api/knowledgeTypes';
import { failure, ok, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { exampleAuthor, knowledgeSpaceOf, pageOf, pageNodeOf, serveKnowledge } from '../../../test/knowledge';
import { renderApp } from '../../../test/renderApp';
import { server } from '../../../test/server';


const entry = (overrides: Partial<KnowledgeTrashEntry> = {}): KnowledgeTrashEntry => ({
  deletionId: 'deletion-1',
  page: { id: 'page-5', number: 5, title: '舊的流程' },
  pageCount: 3,
  formerParent: { id: 'page-1', number: 1, title: '新人到職' },
  deletedAt: '2026-11-25T02:00:00+00:00',
  deletedBy: exampleAuthor,
  ...overrides,
});

function openTrash(entries: readonly KnowledgeTrashEntry[], space = knowledgeSpaceOf()) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge({ spaces: [space] });
  backend.trash[space.key] = [...entries];
  return renderApp(`/knowledge/${space.key}/trash`);
}

describe('垃圾桶', () => {
  it('一批一列：標題、連同底下幾頁、原本在哪一頁底下、誰刪的', async () => {
    // Arrange、Act
    openTrash([entry(), entry({ deletionId: 'deletion-2', page: { id: 'page-6', number: 6, title: '單獨的頁面' }, pageCount: 1, formerParent: null })]);

    // Assert
    const list = await screen.findByRole('list', { name: '垃圾桶' });
    const [first, second] = within(list).getAllByRole('listitem');
    expect(first).toHaveTextContent('舊的流程');
    expect(first).toHaveTextContent('連同底下 2 頁');
    expect(first).toHaveTextContent('原本在「新人到職」底下');
    expect(first).toHaveTextContent('範例使用者');
    expect(second).toHaveTextContent('單獨的頁面');
    expect(second).not.toHaveTextContent('連同底下');
    expect(second).not.toHaveTextContent('原本在');
    expect(screen.getByRole('heading', { level: 1, name: '垃圾桶' })).toBeInTheDocument();
  });

  it('空的時候是空白狀態', async () => {
    // Arrange、Act
    openTrash([]);

    // Assert
    expect(await screen.findByText('垃圾桶是空的')).toBeInTheDocument();
  });

  it('復原之後那一列換成「已復原」加連結，不重抓清單、列不消失', async () => {
    // Arrange
    const restored: KnowledgePage = pageOf(
      serveKnowledge({ spaces: [knowledgeSpaceOf()] }),
      knowledgeSpaceOf(),
      pageNodeOf({ number: 5, title: '舊的流程' }),
    );
    const { user } = openTrash([entry()]);
    server.use(http.post('/v1/knowledge/spaces/DOCS/pages/5/restore', () => ok(restored)));

    // Act
    await user.click(await screen.findByRole('button', { name: '復原「舊的流程」' }));

    // Assert
    const link = await screen.findByRole('link', { name: '已復原「舊的流程」' });
    expect(link).toHaveAttribute('href', '/knowledge/DOCS/5');
    expect(screen.queryByRole('button', { name: /復原「/ })).not.toBeInTheDocument();
  });

  it('復原失敗時原因寫在那一列，按鈕還在可以再試', async () => {
    // Arrange
    const { user } = openTrash([entry()]);
    server.use(http.post('/v1/knowledge/spaces/DOCS/pages/5/restore', () => failure(409, 'knowledge.page_limit_reached', '這個空間的頁面已經到上限了。')));

    // Act
    await user.click(await screen.findByRole('button', { name: '復原「舊的流程」' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('這個空間的頁面已經到上限了。');
    await waitFor(() => { expect(screen.getByRole('button', { name: '復原「舊的流程」' })).toBeEnabled(); });
  });

  it('不能編輯的人（唯讀的空間）看得到清單，沒有復原的按鈕', async () => {
    // Arrange、Act
    openTrash([entry()], knowledgeSpaceOf({ myPermissions: { canEdit: false, canComment: false, canManage: false } }));

    // Assert
    expect(await screen.findByText('舊的流程')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /復原/ })).not.toBeInTheDocument();
  });

  it('找不到（或看不到）的空間說明並回到知識庫', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [] });

    // Act
    renderApp('/knowledge/NOPE/trash');

    // Assert
    expect(await screen.findByRole('heading', { name: '找不到這個空間' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到知識庫' })).toHaveAttribute('href', '/knowledge');
  });

  it('連按兩批的「復原」：第一批完成時，第二批還在送出，它的按鈕不會被放開', async () => {
    // Arrange
    const restored = (number: number, title: string): KnowledgePage =>
      pageOf(serveKnowledge({ spaces: [knowledgeSpaceOf()] }), knowledgeSpaceOf(), pageNodeOf({ number, title }));
    const { user } = openTrash([entry(), entry({ deletionId: 'deletion-2', page: { id: 'page-6', number: 6, title: '單獨的頁面' }, pageCount: 1, formerParent: null })]);
    const releases = new Map<number, () => void>();
    for (const [number, title] of [[5, '舊的流程'], [6, '單獨的頁面']] as const) {
      const gate = new Promise<void>((resolve) => {
        releases.set(number, resolve);
      });
      server.use(
        http.post(`/v1/knowledge/spaces/DOCS/pages/${String(number)}/restore`, async () => {
          await gate;
          return ok(restored(number, title));
        }),
      );
    }
    await user.click(await screen.findByRole('button', { name: '復原「舊的流程」' }));
    await user.click(await screen.findByRole('button', { name: '復原「單獨的頁面」' }));

    // Act
    releases.get(5)?.();

    // Assert
    expect(await screen.findByRole('link', { name: '已復原「舊的流程」' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '復原「單獨的頁面」' })).toBeDisabled();
    releases.get(6)?.();
    expect(await screen.findByRole('link', { name: '已復原「單獨的頁面」' })).toBeInTheDocument();
  });
});
