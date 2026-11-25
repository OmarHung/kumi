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

/** 收到的永久刪除請求的內文（照順序）。 */
const purgesOf = (backend: ReturnType<typeof serveKnowledge>) =>
  backend.requests.filter((request) => request.path.endsWith('/trash/purge')).map((request) => request.body);

function openTrash(entries: readonly KnowledgeTrashEntry[], space = knowledgeSpaceOf()) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge({ spaces: [space] });
  backend.trash[space.key] = [...entries];
  return { ...renderApp(`/knowledge/${space.key}/trash`), backend };
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
    // 先造好：在處理函式裡才呼叫 serveKnowledge 會換掉整個假後端，重抓清單時就是空的。
    const space = knowledgeSpaceOf();
    const fake = serveKnowledge({ spaces: [space] });
    const pages: ReadonlyMap<number, KnowledgePage> = new Map([
      [5, pageOf(fake, space, pageNodeOf({ number: 5, title: '舊的流程' }))],
      [6, pageOf(fake, space, pageNodeOf({ number: 6, title: '單獨的頁面' }))],
    ]);
    const { user } = openTrash([entry(), entry({ deletionId: 'deletion-2', page: { id: 'page-6', number: 6, title: '單獨的頁面' }, pageCount: 1, formerParent: null })]);
    const releases = new Map<number, () => void>();
    for (const number of [5, 6]) {
      const gate = new Promise<void>((resolve) => {
        releases.set(number, resolve);
      });
      server.use(
        http.post(`/v1/knowledge/spaces/DOCS/pages/${String(number)}/restore`, async () => {
          await gate;
          return ok(pages.get(number) ?? null);
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

describe('垃圾桶：分頁與容量', () => {
  const many = (count: number) =>
    Array.from({ length: count }, (_, index) =>
      entry({ deletionId: `deletion-${String(index)}`, page: { id: `page-${String(index)}`, number: index + 10, title: `第 ${String(index)} 批` }, pageCount: 1, formerParent: null }),
    );

  it('一次讀一頁，「載入更多」接著讀下一頁，到底就沒有按鈕', async () => {
    // Arrange
    const { user } = openTrash(many(60));
    const list = await screen.findByRole('list', { name: '垃圾桶' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(50);

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    await waitFor(() => { expect(within(list).getAllByRole('listitem')).toHaveLength(60); });
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
  });

  it('管理者看得到「垃圾桶 N／1,000 批」，只有編輯權限的人看不到', async () => {
    // Arrange、Act
    openTrash([entry(), entry({ deletionId: 'deletion-2', page: { id: 'page-6', number: 6, title: '另一批' } })]);

    // Assert
    expect(await screen.findByText('垃圾桶 2／1,000 批')).toBeInTheDocument();
  });

  it('不是空間的管理者：沒有容量、沒有永久刪除的入口', async () => {
    // Arrange、Act
    openTrash([entry()], knowledgeSpaceOf({ myPermissions: { canEdit: true, canComment: true, canManage: false } }));

    // Assert
    expect(await screen.findByText('舊的流程')).toBeInTheDocument();
    expect(screen.queryByText(/垃圾桶 \d+／/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /永久刪除/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /清掉 30 天以上/ })).not.toBeInTheDocument();
  });

  it('垃圾桶滿了時說明：先清掉或永久刪除，才能再刪頁面', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = serveKnowledge({ spaces: [knowledgeSpaceOf()] });
    backend.trash.DOCS = many(3);
    backend.trashMaxBatches = 3;

    // Act
    renderApp('/knowledge/DOCS/trash');

    // Assert
    expect(await screen.findByText('垃圾桶 3／3 批')).toBeInTheDocument();
    expect(screen.getByText(/已經滿了/)).toBeInTheDocument();
  });
});

describe('垃圾桶：永久刪除', () => {
  const old = entry({ deletionId: 'deletion-old', page: { id: 'page-9', number: 9, title: '很久以前的頁面' }, pageCount: 4, deletedAt: '2026-01-01T00:00:00+00:00' });

  it('「永久刪除…」先確認，寫明不能復原與會刪幾頁；取消不送請求', async () => {
    // Arrange
    const { user, backend } = openTrash([entry()]);

    // Act
    await user.click(await screen.findByRole('button', { name: '永久刪除「舊的流程」…' }));

    // Assert
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('永久刪除之後不能復原，頁面、版本、附件一起刪掉');
    expect(dialog).toHaveTextContent('3 頁');
    await user.click(within(dialog).getByRole('button', { name: '取消' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(purgesOf(backend)).toHaveLength(0);
  });

  it('確認之後送那一批的 deletionId，那一列消失，顯示清了幾批、幾頁、幾個附件', async () => {
    // Arrange
    const { user, backend } = openTrash([entry(), entry({ deletionId: 'deletion-2', page: { id: 'page-6', number: 6, title: '另一批' }, pageCount: 1, formerParent: null })]);
    await user.click(await screen.findByRole('button', { name: '永久刪除「舊的流程」…' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '永久刪除' }));

    // Assert
    expect(await screen.findByText('已永久刪除 1 批、3 頁、1 個附件。')).toBeInTheDocument();
    expect(purgesOf(backend)).toEqual([{ deletionId: 'deletion-1' }]);
    await waitFor(() => { expect(screen.queryByText('舊的流程')).not.toBeInTheDocument(); });
    expect(screen.getByText('另一批')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('「清掉 30 天以上的…」確認之後送 expired: true，只清掉到期的', async () => {
    // Arrange
    const { user, backend } = openTrash([entry({ deletedAt: new Date().toISOString() }), old]);
    await user.click(await screen.findByRole('button', { name: '清掉 30 天以上的…' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('永久刪除之後不能復原，頁面、版本、附件一起刪掉');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '永久刪除' }));

    // Assert
    expect(await screen.findByText('已永久刪除 1 批、4 頁、1 個附件。')).toBeInTheDocument();
    expect(purgesOf(backend)).toEqual([{ expired: true }]);
    await waitFor(() => { expect(screen.queryByText('很久以前的頁面')).not.toBeInTheDocument(); });
    expect(screen.getByText('舊的流程')).toBeInTheDocument();
  });

  it('指定的一批底下還有留著的頁面（伺服器回 200、計數都是 0）：警告而不是成功，那一列還在', async () => {
    // Arrange
    const { user, backend } = openTrash([entry()]);
    backend.trashPinned.add('deletion-1');
    await user.click(await screen.findByRole('button', { name: '永久刪除「舊的流程」…' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '永久刪除' }));

    // Assert
    expect(await screen.findByText(/這一批現在不能永久刪除：它底下還有留著的頁面/)).toBeInTheDocument();
    expect(screen.getByText(/什麼都沒有刪/)).toBeInTheDocument();
    expect(screen.queryByText(/已永久刪除/)).not.toBeInTheDocument();
    expect(screen.getByText('舊的流程')).toBeInTheDocument();
  });

  it('「清掉 30 天以上的」沒有可以清的批：用中性的提示，不說成功', async () => {
    // Arrange
    const { user } = openTrash([entry({ deletedAt: new Date().toISOString() })]);
    await user.click(await screen.findByRole('button', { name: '清掉 30 天以上的…' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '永久刪除' }));

    // Assert
    expect(await screen.findByText('沒有刪除超過 30 天、而且可以清掉的批。')).toBeInTheDocument();
    expect(screen.queryByText(/已永久刪除/)).not.toBeInTheDocument();
  });

  it('伺服器說還有到期而這次沒清到的批：加一句還有更多', async () => {
    // Arrange
    const { user } = openTrash([old]);
    server.use(
      http.post('/v1/knowledge/spaces/DOCS/trash/purge', () =>
        ok({ purgedBatches: 200, purgedPages: 400, purgedAttachments: 3, remainingBatches: 12, hasMoreExpired: true }),
      ),
    );
    await user.click(await screen.findByRole('button', { name: '清掉 30 天以上的…' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '永久刪除' }));

    // Assert
    expect(await screen.findByText(/已永久刪除 200 批、400 頁、3 個附件。還有更多，可以再清一次/)).toBeInTheDocument();
  });

  it('送出進行中再點確認鈕：只送一次請求', async () => {
    // Arrange
    const { user } = openTrash([entry()]);
    let sent = 0;
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('/v1/knowledge/spaces/DOCS/trash/purge', async () => {
        sent += 1;
        await gate;
        return ok({ purgedBatches: 1, purgedPages: 3, purgedAttachments: 1, remainingBatches: 0 });
      }),
    );
    await user.click(await screen.findByRole('button', { name: '永久刪除「舊的流程」…' }));
    const confirm = within(await screen.findByRole('dialog')).getByRole('button', { name: '永久刪除' });

    // Act
    await user.click(confirm);
    await user.click(screen.getByRole('button', { name: /刪除中/ }));
    await user.click(screen.getByRole('button', { name: /刪除中/ }));
    release();

    // Assert
    expect(await screen.findByText(/已永久刪除 1 批/)).toBeInTheDocument();
    expect(sent).toBe(1);
  });

  it('復原成功之後垃圾桶的清單與容量重抓，已復原的那一列留在原位', async () => {
    // Arrange
    const restored: KnowledgePage = pageOf(
      serveKnowledge({ spaces: [knowledgeSpaceOf()] }),
      knowledgeSpaceOf(),
      pageNodeOf({ number: 5, title: '舊的流程' }),
    );
    const { user, backend } = openTrash([entry()]);
    server.use(
      http.post('/v1/knowledge/spaces/DOCS/pages/5/restore', () => {
        backend.trash.DOCS = [];
        return ok(restored);
      }),
    );
    expect(await screen.findByText('垃圾桶 1／1,000 批')).toBeInTheDocument();

    // Act
    await user.click(await screen.findByRole('button', { name: '復原「舊的流程」' }));

    // Assert
    expect(await screen.findByText('垃圾桶 0／1,000 批')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '已復原「舊的流程」' })).toBeInTheDocument();
  });

  it.each([
    [409, 'knowledge.space_archived', '這個空間已經封存了。'],
    [404, 'knowledge.page_not_found', '找不到這一頁。'],
    [403, 'knowledge.forbidden', '你沒有權限這樣做。'],
  ])('失敗（%d %s）：訊息寫在對話框裡，對話框留著', async (status, code, message) => {
    // Arrange
    const { user } = openTrash([entry()]);
    server.use(http.post('/v1/knowledge/spaces/DOCS/trash/purge', () => failure(status, code, message)));
    await user.click(await screen.findByRole('button', { name: '永久刪除「舊的流程」…' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '永久刪除' }));

    // Assert
    expect(await within(screen.getByRole('dialog')).findByText(message)).toBeInTheDocument();
    expect(screen.getByText('舊的流程')).toBeInTheDocument();
  });
});
});
