import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { failure, ok, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { knowledgeSpaceOf, pageNodeOf, pageOf, serveKnowledge } from '../../../test/knowledge';
import { currentPath, renderApp } from '../../../test/renderApp';
import { server } from '../../../test/server';
import { BELOW_WIDE_QUERY } from '../../../lib/useMediaQuery';
import { queryKeys } from '../../../api/queryKeys';

afterEach(() => {
  vi.unstubAllGlobals();
});

function stubBelowWide(isBelow: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: isBelow && query === BELOW_WIDE_QUERY,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function open(path: string, options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge(options);
  return { backend, ...renderApp(path) };
}

function gate() {
  let release: () => void = () => undefined;
  const closed = new Promise<void>((resolve) => {
    release = resolve;
  });
  return { closed, release };
}

async function deletePage(user: ReturnType<typeof open>['user'], title: string) {
  await user.click(await screen.findByRole('button', { name: `「${title}」的更多動作` }));
  await user.click(await screen.findByRole('menuitem', { name: '刪除…' }));
  const dialog = await screen.findByRole('dialog', { name: `刪除「${title}」？` });
  await waitFor(() => {
    expect(within(dialog).getByRole('button', { name: '刪除' })).not.toHaveAttribute('aria-disabled');
  });
  await user.click(within(dialog).getByRole('button', { name: '刪除' }));
}

describe('刪除正在看的頁面之後', () => {
  it('按上一頁回到它的網址：說找不到，不顯示快取裡的舊內容、也不能編輯', async () => {
    // Arrange
    stubBelowWide(false);
    const { user, router } = open('/knowledge/DOCS/2', { bodies: { 'DOCS/2': '舊的內容' } });
    expect(await screen.findByText('舊的內容')).toBeInTheDocument();
    await deletePage(user, '帳號申請');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/1');
    });

    // Act
    await router.navigate(-1);

    // Assert：頁面的資料在換頁後才清掉，清的時候這一頁的畫面可能重畫一次，所以等到穩定再看。
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1, name: '找不到這一頁' })).toBeInTheDocument();
    });
    expect(screen.queryByText('舊的內容')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '編輯' })).not.toBeInTheDocument();
  });

  it('手上有舊資料，重抓時伺服器說找不到這一頁：也當成找不到', async () => {
    // Arrange
    stubBelowWide(false);
    const { queryClient } = open('/knowledge/DOCS/2', { bodies: { 'DOCS/2': '舊的內容' } });
    expect(await screen.findByText('舊的內容')).toBeInTheDocument();
    server.use(http.get('/v1/knowledge/spaces/:key/pages/:number', () => failure(404, 'knowledge.page_not_found', '找不到這一頁。')));

    // Act
    await queryClient.invalidateQueries({ queryKey: queryKeys.knowledgePage('DOCS', 2) });

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '找不到這一頁' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '編輯' })).not.toBeInTheDocument();
  });
});

describe('刪除別的頁面', () => {
  it('正在編輯 A 頁時從樹刪 B 頁：留在原地、編輯的內容與焦點不動，提示（即時區域）就地顯示並有「復原」', async () => {
    // Arrange
    stubBelowWide(false);
    const { user, router } = open('/knowledge/DOCS/2', { bodies: { 'DOCS/2': '舊的內容' } });
    await user.click(await screen.findByRole('button', { name: '編輯' }));
    const title = await screen.findByRole('textbox', { name: '標題' });
    await user.type(title, '改');

    // Act
    await deletePage(user, '會議記錄');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('treeitem', { name: /會議記錄/ })).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/knowledge/DOCS/2');
    expect(screen.getByRole('textbox', { name: '標題' })).toHaveValue('帳號申請改');
    // 焦點沒有被搬到頁面的標題（編輯中的標題是視覺隱藏的 h1）。
    expect(screen.getByRole('heading', { level: 1 })).not.toHaveFocus();
    await waitFor(() => {
      expect(screen.getAllByRole('status').map((live) => live.textContent)).toContain('已把「會議記錄」放進垃圾桶。復原');
    });
    expect(screen.getByRole('button', { name: '復原' })).toBeInTheDocument();
  });
});

describe('新增頁面', () => {
  it('窄螢幕建立成功進到那一頁：頁面面板收起來', async () => {
    // Arrange
    stubBelowWide(true);
    const { user, router } = open('/knowledge/DOCS');
    await user.click(await screen.findByRole('button', { name: '頁面' }));
    await user.click(await screen.findByRole('button', { name: '新增最上層的頁面' }));

    // Act
    await user.type(await screen.findByRole('textbox', { name: '新頁面的標題' }), '新的一頁{Enter}');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/5');
    });
    await waitFor(() => {
      expect(screen.queryByRole('tree')).not.toBeInTheDocument();
    });
  });

  it('送出中再按「新增」或 Esc：忽略，輸入框照舊唯讀，請求只有一個、成功後照樣換頁', async () => {
    // Arrange
    stubBelowWide(false);
    const { user, router, backend } = open('/knowledge/DOCS');
    const slow = gate();
    server.use(
      http.post('/v1/knowledge/spaces/:key/pages', async ({ request }) => {
        await slow.closed;
        const input = (await request.clone().json()) as { title: string };
        return ok(pageOf(backend, knowledgeSpaceOf(), pageNodeOf({ number: 9, title: input.title })), 201);
      }),
    );
    await user.click(await screen.findByRole('button', { name: '新增最上層的頁面' }));
    const input = await screen.findByRole('textbox', { name: '新頁面的標題' });
    await user.type(input, '慢慢來{Enter}');
    expect(input).toHaveAttribute('readonly');

    // Act
    await user.click(screen.getByRole('button', { name: '新增最上層的頁面' }));
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.getByRole('textbox', { name: '新頁面的標題' })).toHaveAttribute('readonly');
    slow.release();
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/9');
    });
  });
});

describe('搬移之後', () => {
  async function moveUnder(user: ReturnType<typeof open>['user'], title: string, parent: string) {
    await user.click(await screen.findByRole('button', { name: `「${title}」的更多動作` }));
    await user.click(await screen.findByRole('menuitem', { name: '搬移…' }));
    const dialog = await screen.findByRole('dialog', { name: `搬移「${title}」` });
    await user.click(within(dialog).getByRole('combobox', { name: '上層頁面' }));
    await user.click(await screen.findByRole('option', { name: parent }));
    await user.click(within(dialog).getByRole('button', { name: '搬移' }));
  }

  function countGets(backend: ReturnType<typeof serveKnowledge>) {
    const counts = { tree: 0, page3: 0 };
    server.use(
      http.get('/v1/knowledge/spaces/:key/tree', () => {
        counts.tree += 1;
        return ok({ space: backend.spaces[0], pages: backend.trees.DOCS ?? [] });
      }),
      http.get('/v1/knowledge/spaces/:key/pages/:number', ({ params }) => {
        if (params.number === '3') counts.page3 += 1;
        const node = (backend.trees.DOCS ?? []).find((candidate) => candidate.number === Number(params.number));
        return node === undefined || backend.spaces[0] === undefined ? failure(404, 'knowledge.page_not_found', '找不到。') : ok(pageOf(backend, backend.spaces[0], node));
      }),
    );
    return counts;
  }

  it('失敗：把樹換回原樣之外，還重抓整個空間（別人可能同時改過）', async () => {
    // Arrange
    stubBelowWide(false);
    const { user, backend } = open('/knowledge/DOCS');
    const counts = countGets(backend);
    await screen.findByRole('tree', { name: '頁面樹' });
    await waitFor(() => {
      expect(counts.tree).toBeGreaterThan(0);
    });
    const before = counts.tree;
    server.use(http.post('/v1/knowledge/spaces/:key/pages/:number/move', () => failure(409, 'knowledge.page_too_deep', '頁面最多 10 層。')));

    // Act
    await moveUnder(user, '會議記錄', '新人到職');

    // Assert
    await waitFor(() => {
      expect(counts.tree).toBeGreaterThan(before);
    });
  });

  it('成功：底下子頁面的資料（上層、繼承的限制）也重抓，不等過期', async () => {
    // Arrange
    stubBelowWide(false);
    const { user, backend } = open('/knowledge/DOCS/3');
    const counts = countGets(backend);
    await screen.findByRole('heading', { level: 1, name: '開發環境' });
    const before = counts.page3;
    server.use(http.post('/v1/knowledge/spaces/:key/pages/:number/move', () => ok({ space: backend.spaces[0], pages: backend.trees.DOCS ?? [] })));

    // Act
    await moveUnder(user, '新人到職', '會議記錄');

    // Assert
    await waitFor(() => {
      expect(counts.page3).toBeGreaterThan(before);
    });
  });
});
