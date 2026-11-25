import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { http } from 'msw';
import { failure, ok, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { exampleKnowledgeTree, knowledgeSpaceOf, pageNodeOf, serveKnowledge } from '../../../test/knowledge';
import { currentPath, renderApp } from '../../../test/renderApp';
import { findEditor } from '../../../test/richText';
import { server } from '../../../test/server';

const STORAGE_KEY = 'knowledge.tree.expanded.DOCS';

function openTree(path = '/knowledge/DOCS', options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge(options);
  return { backend, ...renderApp(path) };
}

const readOnly = knowledgeSpaceOf({ myPermissions: { canEdit: false, canComment: false, canManage: false } });

afterEach(() => {
  vi.restoreAllMocks();
});

describe('頁面樹：結構與展開', () => {
  it('是 tree：最上層的頁面各一個 treeitem，有子頁面的標 aria-expanded，收合時看不到子頁面', async () => {
    // Arrange & Act
    openTree();

    // Assert
    const tree = await screen.findByRole('tree', { name: '頁面樹' });
    const items = within(tree).getAllByRole('treeitem');
    expect(items.map((item) => item.textContent)).toEqual(expect.arrayContaining([expect.stringContaining('新人到職'), expect.stringContaining('會議記錄')]));
    expect(items).toHaveLength(2);
    expect(within(tree).getByRole('treeitem', { name: /新人到職/ })).toHaveAttribute('aria-expanded', 'false');
    expect(within(tree).getByRole('treeitem', { name: /會議記錄/ })).not.toHaveAttribute('aria-expanded');
    expect(within(tree).getByRole('treeitem', { name: /新人到職/ })).toHaveAttribute('aria-level', '1');
  });

  it('按箭頭展開：看得到子頁面（aria-level 2），記在這個瀏覽器，重新整理後還是展開', async () => {
    // Arrange
    const { user, unmount } = openTree();
    await screen.findByRole('tree', { name: '頁面樹' });

    // Act
    await user.click(screen.getByRole('button', { name: '展開「新人到職」' }));

    // Assert
    expect(screen.getByRole('treeitem', { name: /帳號申請/ })).toHaveAttribute('aria-level', '2');
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]')).toEqual([1]);
    unmount();
    server.resetHandlers();
    openTree();
    expect(await screen.findByRole('treeitem', { name: /帳號申請/ })).toBeInTheDocument();
  });

  it('目前這一頁的上層一定展開，目前這一頁標 aria-current', async () => {
    // Arrange & Act
    openTree('/knowledge/DOCS/2');

    // Assert
    expect(await screen.findByRole('treeitem', { name: /帳號申請/ })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('treeitem', { name: /新人到職/ })).toHaveAttribute('aria-expanded', 'true');
  });

  it('自動展開的上層不寫進記憶：使用者自己展開、收合的才記', async () => {
    // Arrange
    const tree = [
      pageNodeOf({ number: 1, title: '新人到職', position: 0 }),
      pageNodeOf({ number: 2, title: '帳號申請', parentId: 'page-1', position: 0 }),
      pageNodeOf({ number: 5, title: '手冊', position: 1 }),
      pageNodeOf({ number: 6, title: '附錄', parentId: 'page-5', position: 0 }),
    ];
    const { user } = openTree('/knowledge/DOCS/2', { trees: { DOCS: tree } });
    await screen.findByRole('treeitem', { name: /帳號申請/ });
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();

    // Act
    await user.click(screen.getByRole('button', { name: '展開「手冊」' }));

    // Assert
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]')).toEqual([5]);
    expect(screen.getByRole('treeitem', { name: /帳號申請/ })).toBeInTheDocument();
  });

  it('localStorage 讀寫都失敗：樹照常畫、照常展開，不丟例外', async () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('不能用');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('不能用');
    });
    const { user } = openTree();
    await screen.findByRole('tree', { name: '頁面樹' });

    // Act
    await user.click(screen.getByRole('button', { name: '展開「新人到職」' }));

    // Assert
    expect(screen.getByRole('treeitem', { name: /帳號申請/ })).toBeInTheDocument();
  });

  it('樹上有一個一開始就掛著、還是空的即時區域（拖曳搬移的結果之後才填字）', async () => {
    // Arrange & Act
    openTree();

    // Assert
    const tree = await screen.findByRole('region', { name: '頁面' });
    // dnd-kit 自己也有一個即時區域（拖曳中的提示）；這裡看的是樹的那一個（放在最前面的段落）。
    expect(tree.querySelector('p[role="status"]')).toBeEmptyDOMElement();
  });

  it('有限制的頁面標「限制」', async () => {
    // Arrange
    const tree = [pageNodeOf({ number: 1, title: '薪資制度', isRestricted: true }), pageNodeOf({ number: 2, title: '公開的', position: 1 })];

    // Act
    openTree('/knowledge/DOCS', { trees: { DOCS: tree } });

    // Assert
    const restricted = await screen.findByRole('treeitem', { name: /薪資制度/ });
    expect(within(restricted).getByRole('img', { name: '限制' })).toBeInTheDocument();
    expect(within(screen.getByRole('treeitem', { name: /公開的/ })).queryByRole('img', { name: '限制' })).not.toBeInTheDocument();
  });

  it('列的名稱只有標題（有限制時後面加「（限制）」），不把展開、新增、更多的按鈕名稱串進來', async () => {
    // Arrange
    const tree = [
      pageNodeOf({ number: 1, title: '薪資制度', isRestricted: true }),
      pageNodeOf({ number: 2, title: '公開的', position: 1 }),
      pageNodeOf({ number: 3, title: '子頁面', parentId: pageNodeOf({ number: 2 }).id, position: 0 }),
    ];

    // Act
    openTree('/knowledge/DOCS', { trees: { DOCS: tree } });

    // Assert
    expect(await screen.findByRole('treeitem', { name: '薪資制度 （限制）' })).toBeInTheDocument();
    expect(screen.getByRole('treeitem', { name: '公開的' })).toBeInTheDocument();
  });
});

describe('頁面樹：鍵盤', () => {
  it('整棵樹只有一個 Tab 停靠點：目前這一頁，沒有目前這一頁就是第一列', async () => {
    // Arrange & Act
    openTree();

    // Assert
    const items = await screen.findAllByRole('treeitem');
    expect(items.filter((item) => item.tabIndex === 0)).toHaveLength(1);
    expect(items[0]).toHaveAttribute('tabindex', '0');
  });

  it('↑↓ 移動、Home／End 到頭尾，→ 展開再進第一個子頁面，← 回上層再收合', async () => {
    // Arrange
    const { user } = openTree();
    const first = await screen.findByRole('treeitem', { name: /新人到職/ });
    first.focus();

    // Act & Assert
    await user.keyboard('{ArrowDown}');
    expect(screen.getByRole('treeitem', { name: /會議記錄/ })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(first).toHaveFocus();
    await user.keyboard('{End}');
    expect(screen.getByRole('treeitem', { name: /會議記錄/ })).toHaveFocus();
    await user.keyboard('{Home}{ArrowRight}');
    expect(first).toHaveAttribute('aria-expanded', 'true');
    expect(first).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('treeitem', { name: /帳號申請/ })).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(first).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(first).toHaveAttribute('aria-expanded', 'false');
  });

  it('Enter 打開那一頁', async () => {
    // Arrange
    const { user, router } = openTree();
    const item = await screen.findByRole('treeitem', { name: /會議記錄/ });
    item.focus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/4');
    });
  });
});

describe('頁面樹：新增頁面', () => {
  it('按「＋」：在那一頁底下出現輸入框，Enter 建立、打開那一頁並直接進編輯', async () => {
    // Arrange
    const { user, router, backend } = openTree();
    await user.click(await screen.findByRole('button', { name: '在「新人到職」底下新增頁面' }));
    const input = await screen.findByRole('textbox', { name: '新頁面的標題' });
    // 輸入框在新人到職的最後一個子頁面後面、下一個最上層的頁面前面
    expect(within(screen.getByRole('treeitem', { name: /會議記錄/ }).previousElementSibling as HTMLElement).getByRole('textbox')).toBe(input);

    // Act
    await user.type(input, '  請假規定  {Enter}');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/5');
    });
    expect(backend.requests[0]?.body).toEqual({ parentNumber: 1, title: '請假規定', beforeNumber: null });
    expect(await findEditor('內容')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '標題' })).toHaveValue('請假規定');
    // 「直接進編輯」的記號用過就從這一筆紀錄拿掉：之後回到這一頁不會又自己進編輯
    await waitFor(() => {
      expect(router.state.location.state).toBeNull();
    });
    expect(screen.getByRole('textbox', { name: '標題' })).toBeInTheDocument();
  });

  it('最上面的「新增頁面」新增在最上層', async () => {
    // Arrange
    const { user, backend } = openTree();
    await user.click(await screen.findByRole('button', { name: '新增最上層的頁面' }));

    // Act
    await user.type(await screen.findByRole('textbox', { name: '新頁面的標題' }), '手冊{Enter}');

    // Assert
    await waitFor(() => {
      expect(backend.requests[0]?.body).toEqual({ parentNumber: null, title: '手冊', beforeNumber: null });
    });
  });

  it('Esc 取消：輸入框不見、沒有送請求', async () => {
    // Arrange
    const { user, backend } = openTree();
    await user.click(await screen.findByRole('button', { name: '在「會議記錄」底下新增頁面' }));
    const input = await screen.findByRole('textbox', { name: '新頁面的標題' });

    // Act
    await user.type(input, '半途{Escape}');

    // Assert
    expect(screen.queryByRole('textbox', { name: '新頁面的標題' })).not.toBeInTheDocument();
    expect(backend.requests).toHaveLength(0);
  });

  it('什麼都沒打就按 Enter：說要輸入標題，不送請求', async () => {
    // Arrange
    const { user, backend } = openTree();
    await user.click(await screen.findByRole('button', { name: '新增最上層的頁面' }));

    // Act
    await user.type(await screen.findByRole('textbox', { name: '新頁面的標題' }), '{Enter}');

    // Assert
    expect(await screen.findByText('請輸入頁面的標題。')).toBeInTheDocument();
    expect(backend.requests).toHaveLength(0);
  });

  it('伺服器擋下（超過層級）：原因寫在輸入框上，輸入框還在', async () => {
    // Arrange
    const { user } = openTree();
    server.use(http.post('/v1/knowledge/spaces/:key/pages', () => failure(422, 'knowledge.page_too_deep', '頁面最多 10 層。')));
    await user.click(await screen.findByRole('button', { name: '新增最上層的頁面' }));

    // Act
    await user.type(await screen.findByRole('textbox', { name: '新頁面的標題' }), '太深{Enter}');

    // Assert
    expect(await screen.findByText('頁面最多 10 層。')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '新頁面的標題' })).toBeInTheDocument();
  });

  it('送出中輸入框唯讀，再按 Enter 不會重送', async () => {
    // Arrange
    const { user, backend } = openTree();
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('/v1/knowledge/spaces/:key/pages', async () => {
        await gate;
        return failure(409, 'knowledge.page_limit_reached', '頁面數量到上限了。');
      }),
    );
    await user.click(await screen.findByRole('button', { name: '新增最上層的頁面' }));
    const input = await screen.findByRole('textbox', { name: '新頁面的標題' });

    // Act
    await user.type(input, '慢慢來{Enter}{Enter}');

    // Assert
    expect(input).toHaveAttribute('readonly');
    release();
    expect(await screen.findByText('頁面數量到上限了。')).toBeInTheDocument();
    expect(backend.requests).toHaveLength(0);
  });
});

describe('頁面樹：列上的動作', () => {
  it('更多選單：新增子頁面、搬移…、限制…、刪除…（刪除在最後）', async () => {
    // Arrange
    const { user } = openTree();

    // Act
    await user.click(await screen.findByRole('button', { name: '「會議記錄」的更多動作' }));

    // Assert
    const menu = await screen.findByRole('menu', { name: '「會議記錄」的更多動作' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['新增子頁面', '搬移…', '限制…', '刪除…']);
  });

  it('沒有 canEdit：沒有任何新增、更多的按鈕，頁面照樣是連結', async () => {
    // Arrange & Act
    openTree('/knowledge/DOCS', { spaces: [readOnly] });

    // Assert
    const tree = await screen.findByRole('tree', { name: '頁面樹' });
    expect(within(tree).getByRole('link', { name: '會議記錄' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增最上層的頁面' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /更多動作/ })).not.toBeInTheDocument();
  });

  it('已經在第 10 層：「新增子頁面」停用並說原因', async () => {
    // Arrange
    const chain = Array.from({ length: 10 }, (_, index) =>
      pageNodeOf({ number: index + 1, title: `第 ${String(index + 1)} 層`, parentId: index === 0 ? null : `page-${String(index)}` }),
    );
    const { user } = openTree('/knowledge/DOCS/10', { trees: { DOCS: chain } });

    // Act
    await user.click(await screen.findByRole('button', { name: '「第 10 層」的更多動作' }));

    // Assert
    const add = await screen.findByRole('menuitem', { name: /新增子頁面/ });
    expect(add).toHaveAttribute('aria-disabled', 'true');
    expect(add).toHaveTextContent('已經是最深的一層');
    expect(screen.getByRole('button', { name: '在「第 10 層」底下新增頁面' })).toBeDisabled();
  });
});

describe('頁面樹：搬移…', () => {
  async function openMove(title: string) {
    const view = openTree();
    await view.user.click(await screen.findByRole('button', { name: `「${title}」的更多動作` }));
    await view.user.click(await screen.findByRole('menuitem', { name: '搬移…' }));
    return { ...view, dialog: await screen.findByRole('dialog', { name: `搬移「${title}」` }) };
  }

  it('預設是現在的位置；沒換位置就送出只是關掉，不送請求', async () => {
    // Arrange
    const { user, dialog, backend } = await openMove('會議記錄');
    expect(within(dialog).getByRole('combobox', { name: '上層頁面' })).toHaveTextContent('（最上層）');
    expect(within(dialog).getByRole('combobox', { name: '位置' })).toHaveTextContent('放在最後');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '搬移' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toHaveLength(0);
  });

  it('選上層與位置送出：POST move，樹換成新的位置', async () => {
    // Arrange
    const { user, dialog } = await openMove('會議記錄');
    const sent: unknown[] = [];
    server.use(
      http.post('/v1/knowledge/spaces/:key/pages/:number/move', async ({ request }) => {
        sent.push(await request.json());
        return ok({
          space: knowledgeSpaceOf(),
          pages: [
            pageNodeOf({ number: 1, title: '新人到職' }),
            pageNodeOf({ number: 4, title: '會議記錄', parentId: 'page-1', position: 0 }),
            pageNodeOf({ number: 2, title: '帳號申請', parentId: 'page-1', position: 1 }),
            pageNodeOf({ number: 3, title: '開發環境', parentId: 'page-1', position: 2 }),
          ],
        });
      }),
    );

    // Act
    await user.click(within(dialog).getByRole('combobox', { name: '上層頁面' }));
    await user.click(await screen.findByRole('option', { name: '新人到職' }));
    await user.click(within(dialog).getByRole('combobox', { name: '位置' }));
    await user.click(await screen.findByRole('option', { name: '放在「帳號申請」前面' }));
    await user.click(within(dialog).getByRole('button', { name: '搬移' }));

    // Assert
    await waitFor(() => {
      expect(sent).toEqual([{ parentNumber: 1, beforeNumber: 2 }]);
    });
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('treeitem', { name: /新人到職/ })).toHaveAttribute('aria-expanded', 'false');
  });

  it('不能放的上層停用並寫原因：自己、自己底下', async () => {
    // Arrange
    const { user, dialog } = await openMove('新人到職');

    // Act
    await user.click(within(dialog).getByRole('combobox', { name: '上層頁面' }));

    // Assert
    const options = await screen.findAllByRole('option');
    const optionOf = (text: string) => options.find((option) => option.textContent.includes(text));
    expect(optionOf('自己')).toHaveAttribute('aria-disabled', 'true');
    expect(optionOf('新人到職 › 帳號申請')).toHaveTextContent('在這一頁底下');
    expect(optionOf('新人到職 › 帳號申請')).toHaveAttribute('aria-disabled', 'true');
    expect(optionOf('會議記錄')).not.toHaveAttribute('aria-disabled', 'true');
  });

  it('伺服器擋下：原因寫在對話框裡，樹換回原樣', async () => {
    // Arrange
    const { user, dialog } = await openMove('會議記錄');
    server.use(http.post('/v1/knowledge/spaces/:key/pages/:number/move', () => failure(409, 'knowledge.space_archived', '這個空間已經封存。')));

    // Act
    await user.click(within(dialog).getByRole('combobox', { name: '上層頁面' }));
    await user.click(await screen.findByRole('option', { name: '新人到職' }));
    await user.click(within(dialog).getByRole('button', { name: '搬移' }));

    // Assert
    expect(await within(dialog).findByText('這個空間已經封存。')).toBeInTheDocument();
    expect(screen.getByRole('treeitem', { name: /會議記錄/ })).toHaveAttribute('aria-level', '1');
  });

  it('搬出別人設的限制（403 restriction_owner_required）：照伺服器的訊息寫在對話框裡', async () => {
    // Arrange
    const { user, dialog } = await openMove('會議記錄');
    const message = '只有設定這個限制的人或空間的管理者能放寬（拿掉限制、加人、搬出有限制的範圍）。';
    server.use(http.post('/v1/knowledge/spaces/:key/pages/:number/move', () => failure(403, 'knowledge.restriction_owner_required', message)));

    // Act
    await user.click(within(dialog).getByRole('combobox', { name: '上層頁面' }));
    await user.click(await screen.findByRole('option', { name: '新人到職' }));
    await user.click(within(dialog).getByRole('button', { name: '搬移' }));

    // Assert
    expect(await within(dialog).findByText(message)).toBeInTheDocument();
    expect(screen.getByRole('treeitem', { name: /會議記錄/ })).toHaveAttribute('aria-level', '1');
  });
});

describe('頁面樹：刪除…', () => {
  async function openDelete(title: string, path = '/knowledge/DOCS') {
    const view = openTree(path);
    await view.user.click(await screen.findByRole('button', { name: `「${title}」的更多動作` }));
    await view.user.click(await screen.findByRole('menuitem', { name: '刪除…' }));
    return view;
  }

  it('確認說出後果：連同底下幾頁、放進垃圾桶、可以復原', async () => {
    // Arrange & Act
    await openDelete('新人到職');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '刪除「新人到職」？' });
    expect(within(dialog).getByText('連同底下 2 頁一起放進垃圾桶，可以從垃圾桶復原。')).toBeInTheDocument();
  });

  it('沒有子頁面的說法不一樣', async () => {
    // Arrange & Act
    await openDelete('會議記錄');

    // Assert
    expect(await screen.findByText('這一頁會放進垃圾桶，可以從垃圾桶復原。')).toBeInTheDocument();
  });

  it('垃圾桶滿了（409 trash_full）：照伺服器的訊息寫在對話框裡，頁面還在', async () => {
    // Arrange
    const { user } = await openDelete('會議記錄');
    const dialog = await screen.findByRole('dialog', { name: '刪除「會議記錄」？' });
    const message = '垃圾桶已經有 1,000 批，請空間的管理者先清掉 30 天以上的，或永久刪除不要的。';
    server.use(http.delete('/v1/knowledge/spaces/:key/pages/:number', () => failure(409, 'knowledge.trash_full', message)));
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: '刪除' })).not.toHaveAttribute('aria-disabled');
    });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(dialog).findByText(message)).toBeInTheDocument();
    expect(screen.getByRole('treeitem', { name: /會議記錄/ })).toBeInTheDocument();
  });

  it('確認後：樹上少了這一頁（連同底下的），上方說已放進垃圾桶並給「復原」', async () => {
    // Arrange
    const { user, backend } = await openDelete('新人到職');
    const dialog = await screen.findByRole('dialog', { name: '刪除「新人到職」？' });
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: '刪除' })).not.toHaveAttribute('aria-disabled');
    });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('已把「新人到職」放進垃圾桶。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '復原' })).toBeInTheDocument();
    expect(screen.queryByRole('treeitem', { name: /新人到職/ })).not.toBeInTheDocument();
    expect(backend.requests.some((request) => request.method === 'DELETE')).toBe(true);
  });

  it('底下有看不到的頁面（canDelete 是 false）：選單照樣有「刪除…」，打開說明原因，不能確認', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = serveKnowledge();
    server.use(
      http.get('/v1/knowledge/spaces/:key/pages/:number', () =>
        ok({
          id: 'page-1',
          space: knowledgeSpaceOf(),
          number: 1,
          title: '新人到職',
          body: '',
          revision: 1,
          ancestors: [],
          isRestricted: false,
          restrictedAncestors: [],
          createdAt: '2026-11-25T02:00:00+00:00',
          createdBy: { personId: 'p-owner', displayName: '範例使用者' },
          updatedAt: '2026-11-25T02:00:00+00:00',
          updatedBy: { personId: 'p-owner', displayName: '範例使用者' },
          versionCount: 1,
          myPermissions: { canEdit: true, canComment: true, canDelete: false },
        }),
      ),
    );
    const { user } = renderApp('/knowledge/DOCS');
    await user.click(await screen.findByRole('button', { name: '「新人到職」的更多動作' }));

    // Act
    await user.click(await screen.findByRole('menuitem', { name: '刪除…' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '刪除「新人到職」？' });
    expect(await within(dialog).findByText('底下有你看不到的頁面，不能刪除。')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));
    expect(backend.requests).toHaveLength(0);
  });

  it('正在看的頁面被刪了：回到上層，最上層就回空間首頁', async () => {
    // Arrange
    const { user, router } = await openDelete('帳號申請', '/knowledge/DOCS/2');
    const dialog = await screen.findByRole('dialog', { name: '刪除「帳號申請」？' });
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: '刪除' })).not.toHaveAttribute('aria-disabled');
    });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/1');
    });
    expect(await screen.findByText('已把「帳號申請」放進垃圾桶。')).toBeInTheDocument();
  });

  it('按「復原」：換成「已復原」加連結，焦點在提示上', async () => {
    // Arrange
    const { user } = await openDelete('會議記錄');
    const dialog = await screen.findByRole('dialog', { name: '刪除「會議記錄」？' });
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: '刪除' })).not.toHaveAttribute('aria-disabled');
    });
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));
    server.use(http.post('/v1/knowledge/spaces/:key/pages/:number/restore', () => ok({})));

    // Act
    await user.click(await screen.findByRole('button', { name: '復原' }));

    // Assert
    expect(await screen.findByText('已復原「會議記錄」。')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '看這一頁' })).toHaveAttribute('href', '/knowledge/DOCS/4');
  });

  it('復原失敗：提示與「復原」留著，下面說原因', async () => {
    // Arrange
    const { user } = await openDelete('會議記錄');
    const dialog = await screen.findByRole('dialog', { name: '刪除「會議記錄」？' });
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: '刪除' })).not.toHaveAttribute('aria-disabled');
    });
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));
    server.use(http.post('/v1/knowledge/spaces/:key/pages/:number/restore', () => failure(409, 'knowledge.page_limit_reached', '頁面數量到上限了。')));

    // Act
    await user.click(await screen.findByRole('button', { name: '復原' }));

    // Assert
    expect(await screen.findByText('頁面數量到上限了。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '復原' })).toBeInTheDocument();
  });
});

describe('頁面樹：樹的結構從伺服器來', () => {
  it('範例的樹（先序）：新人到職、會議記錄在最上層', () => {
    // Arrange & Act
    const tree = exampleKnowledgeTree();

    // Assert
    expect(tree.filter((node) => node.parentId === null)).toHaveLength(2);
  });
});
