import { act, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { readRecentItems } from '../jump/recentItems';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveDeleteItem, serveItem, serveItems, servePatch } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const first = smkItem({ number: 1, title: '首頁設計稿', dueDate: '2026-11-20' });
const second = smkItem({ number: 2, title: '聯絡表單' });
const third = smkItem({ number: 3, title: '上線檢查' });
const ITEMS = [first, second, third];

function serveProject(items: readonly Item[] = ITEMS) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const listRequests = serveItems('SMK', items);
  items.forEach((item) => {
    serveItem(detailOf(item, `${item.title}的說明`));
    serveActivity(item.key, [created()]);
  });
  return listRequests;
}

async function openTable(path = '/projects/SMK') {
  const view = renderApp(path);
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

function keyLink(table: HTMLElement, item: Item): HTMLElement {
  return within(table).getByRole('link', { name: item.title });
}

async function findPanel(item: Item): Promise<HTMLElement> {
  return screen.findByRole('complementary', { name: `項目 ${item.key}` });
}

describe('項目面板：從表格打開', () => {
  it('點編號在右邊開面板：網址多了 item，連結照樣指向整頁，清單上那一筆標成目前的', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable();

    // Act
    await user.click(keyLink(table, first));

    // Assert
    const panel = await findPanel(first);
    expect(currentPath(router)).toBe('/projects/SMK?item=SMK-1');
    expect(keyLink(table, first)).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
    expect(keyLink(table, first)).toHaveAttribute('aria-current', 'true');
    expect(keyLink(table, second)).not.toHaveAttribute('aria-current');
    expect(within(panel).getByRole('heading', { level: 2, name: '任務 SMK-1' })).toHaveFocus();
    expect(await within(panel).findByText('首頁設計稿的說明')).toBeInTheDocument();
    // 面板的標題列是 h2，項目的標題跟著降一層：一頁只有一個 h1（專案的名稱）。
    const title = within(panel).getAllByRole('heading', { level: 2 }).find((heading) => within(heading).queryByRole('button', { name: '標題：首頁設計稿' }));
    expect(title).toBeDefined();
    expect(screen.getAllByRole('heading', { level: 1 }).map((heading) => heading.textContent)).toEqual(['冒煙測試']);
    expect(screen.getByRole('table', { name: '項目' })).toBe(table);
  });

  it('面板裡的標題層級：標題列 h2、項目的標題 h2、各區塊 h3；不重複畫「類型 編號」那一行', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();

    // Act
    await user.click(keyLink(table, first));

    // Assert
    const panel = await findPanel(first);
    await within(panel).findByText('首頁設計稿的說明');
    expect(within(panel).getByRole('heading', { level: 3, name: '說明' })).toBeInTheDocument();
    expect(within(panel).getByRole('heading', { level: 3, name: '活動與留言' })).toBeInTheDocument();
    expect(within(panel).getByRole('heading', { level: 3, name: '欄位' })).toBeInTheDocument();
    expect(within(panel).queryByRole('heading', { level: 2, name: '說明' })).not.toBeInTheDocument();
    // 「任務 SMK-1」只在標題列出現一次。
    expect(within(panel).getAllByText('SMK-1')).toHaveLength(1);
  });

  it('面板裡標題上面有連到專案的連結（專案代號與名稱），點了到那個專案、面板關上', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable('/projects/SMK?view=table&sort=title:asc');
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);

    // Act
    const link = await within(panel).findByRole('link', { name: `SMK ${smkDetail().project.name}` });
    await user.click(link);

    // Assert
    expect(link).toHaveAttribute('href', '/projects/SMK');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
    expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
  });

  it('按著 Ctrl、⌘、Shift 或用中鍵點，照瀏覽器原本的行為，不開面板', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable();
    // jsdom 不會真的開新分頁，只會說沒有實作換頁。
    const virtualConsole = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    // Act
    for (const modifier of ['Control', 'Meta', 'Shift']) {
      await user.keyboard(`{${modifier}>}`);
      await user.click(keyLink(table, first));
      await user.keyboard(`{/${modifier}}`);
    }
    await user.pointer({ keys: '[MouseMiddle]', target: keyLink(table, first) });

    // Assert
    expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK');
    virtualConsole.mockRestore();
  });

  it('Esc 關上面板，網址回到原本的，焦點回到清單上那一筆', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable();
    await user.click(keyLink(table, first));
    await within(await findPanel(first)).findByText('首頁設計稿的說明');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
    expect(keyLink(table, first)).toHaveFocus();
    expect(keyLink(table, first)).not.toHaveAttribute('aria-current');
  });

  it('面板裡編輯中的欄位按 Esc 先取消編輯，再按一次才關上面板', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    await user.click(within(fields).getByRole('button', { name: '到期日：2026/11/20' }));

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(within(fields).getByRole('button', { name: '到期日：2026/11/20' })).toHaveFocus();
    expect(screen.getByRole('complementary', { name: '項目 SMK-1' })).toBe(panel);
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
    });
  });

  it('關閉按鈕與瀏覽器的上一頁都關上面板', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable();
    await user.click(keyLink(table, first));

    // Act
    await user.click(within(await findPanel(first)).getByRole('button', { name: '關閉' }));

    // Assert
    await waitFor(() => {
      expect(keyLink(table, first)).toHaveFocus();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
    await user.click(keyLink(table, second));
    await findPanel(second);
    await router.navigate(-1);
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 SMK-2' })).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
  });

  it('Alt+↓／Alt+↑ 照表格的順序換項目：面板不重新掛上、焦點留著，也不多一筆瀏覽紀錄', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);
    const heading = within(panel).getByRole('heading', { level: 2, name: '任務 SMK-1' });

    // Act
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    expect(await within(panel).findByText('上線檢查的說明')).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: '項目 SMK-3' })).toBe(panel);
    expect(heading).toHaveFocus();
    expect(heading).toHaveTextContent('任務 SMK-3');
    expect(currentPath(router)).toBe('/projects/SMK?item=SMK-3');
    expect(keyLink(table, third)).toHaveAttribute('aria-current', 'true');
    expect(within(panel).getByRole('button', { name: '下一個項目' })).toHaveAttribute('aria-disabled', 'true');
    await user.keyboard('{Alt>}{ArrowUp}{/Alt}');
    expect(await within(panel).findByText('聯絡表單的說明')).toBeInTheDocument();
    await router.navigate(-1);
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
  });

  it('換了幾次項目，標題列的「關注」「分享」照樣各一顆（不會越換越多）', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);
    await within(panel).findByRole('button', { name: '關注' });

    // Act
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await within(panel).findByText('聯絡表單的說明');
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await within(panel).findByText('上線檢查的說明');
    await user.keyboard('{Alt>}{ArrowUp}{/Alt}');
    await within(panel).findByText('聯絡表單的說明');

    // Assert
    await waitFor(() => {
      expect(within(panel).getAllByRole('button', { name: '關注' })).toHaveLength(1);
    });
    expect(within(panel).getAllByRole('button', { name: '分享' })).toHaveLength(1);
  });

  it('上一個、下一個的按鈕：焦點留在按的按鈕上，第一個沒有上一個', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);
    expect(within(panel).getByRole('button', { name: '上一個項目' })).toHaveAttribute('aria-disabled', 'true');
    const next = within(panel).getByRole('button', { name: '下一個項目' });

    // Act
    await user.click(next);

    // Assert
    expect(await within(panel).findByText('聯絡表單的說明')).toBeInTheDocument();
    expect(next).toHaveFocus();
    expect(next).toHaveAttribute('aria-keyshortcuts', expect.stringMatching(/^Alt\+ArrowDown$/));
    expect(within(panel).getByRole('button', { name: '上一個項目' })).not.toHaveAttribute('aria-disabled');
  });

  it('在面板裡改欄位，表格裡的那一格跟著變，不重抓清單', async () => {
    // Arrange
    const listRequests = serveProject();
    servePatch(detailOf(first, '首頁設計稿的說明'));
    const { user, table } = await openTable();
    await user.click(keyLink(table, first));
    const fields = await within(await findPanel(first)).findByRole('region', { name: '欄位' });

    // Act
    await user.click(within(fields).getByRole('button', { name: '到期日：2026/11/20' }));
    const input = within(fields).getByLabelText('到期日');
    await user.clear(input);
    await user.type(input, '2026-12-24');
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(fields).findByRole('button', { name: '到期日：2026/12/24' })).toBeInTheDocument();
    expect(within(table).getByRole('button', { name: '到期日：2026/12/24' })).toBeInTheDocument();
    expect(listRequests).toHaveLength(1);
  });

  it('切換表格與看板時面板留著；換條件也留著', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', ITEMS);
    serveItem(detailOf(first, '首頁設計稿的說明'));
    serveActivity(first.key, [created()]);
    const { user, router } = renderApp('/projects/SMK?q=首頁&item=SMK-1');
    const panel = await findPanel(first);
    const views = screen.getByRole('navigation', { name: '版面' });
    expect(within(views).getByRole('link', { name: '看板' })).toHaveAttribute('href', '/projects/SMK?view=board&q=%E9%A6%96%E9%A0%81&item=SMK-1');

    // Act
    await user.click(within(views).getByRole('link', { name: '看板' }));

    // Assert
    const board = await screen.findByRole('region', { name: '看板' });
    // 面板不在「看板」區域裡（區域只包看板的內容）。
    expect(within(board).queryByRole('complementary')).not.toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: '項目 SMK-1' })).toBe(panel);
    expect(new URLSearchParams(router.state.location.search).get('item')).toBe('SMK-1');
    await user.clear(screen.getByRole('searchbox', { name: '搜尋' }));
    await waitFor(() => {
      expect(new URLSearchParams(router.state.location.search).get('q')).toBeNull();
    });
    expect(new URLSearchParams(router.state.location.search).get('item')).toBe('SMK-1');
    expect(screen.getByRole('complementary', { name: '項目 SMK-1' })).toBe(panel);
  });

  it('開著的那一筆不在清單上（網址直接帶著 item）：關上之後焦點到頁面的標題', async () => {
    // Arrange
    serveProject([first, second]);
    serveItems('SMK', [second]);
    const { user } = await openTable('/projects/SMK?item=SMK-1');
    await within(await findPanel(first)).findByText('首頁設計稿的說明');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1, name: '冒煙測試' })).toHaveFocus();
    });
  });

  it('在面板裡刪除之後，瀏覽紀錄裡沒有多一筆一樣的清單：上一頁回到更早的那一頁', async () => {
    // Arrange
    serveProject();
    const { user, router } = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });
    await router.navigate('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);
    await user.click(await within(panel).findByRole('button', { name: 'SMK-1 的更多動作' }));
    serveItems('SMK', [second, third]);
    serveDeleteItem(first);

    // Act
    await user.click(screen.getByRole('menuitem', { name: '刪除' }));
    await screen.findByText('已刪除 SMK-1「首頁設計稿」。');

    // Act
    await router.navigate(-1);

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });

  it('網址直接帶著 item 打開時面板就在；找不到的項目在面板裡說明', async () => {
    // Arrange
    serveProject();
    server.use(http.get('/v1/items/SMK-99', () => failure(404, 'item.not_found', '找不到這個項目。')));

    // Act
    const { router } = await openTable('/projects/SMK?item=SMK-99');

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-99' });
    expect(await within(panel).findByText('找不到這個項目')).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK?item=SMK-99');
  });

  it('在面板裡刪除：關上面板、留在清單，清單上方說明刪了什麼，焦點到頁面的標題', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable('/projects/SMK?q=首頁');
    await user.click(keyLink(table, first));
    const panel = await findPanel(first);
    await user.click(within(panel).getByRole('button', { name: 'SMK-1 的更多動作' }));
    serveItems('SMK', [second, third]);
    serveDeleteItem(first);

    // Act
    await user.click(screen.getByRole('menuitem', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('已刪除 SMK-1「首頁設計稿」。')).toHaveAttribute('role', 'status');
    expect(currentPath(router)).toBe('/projects/SMK?q=首頁');
    expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '冒煙測試' })).toHaveFocus();
  });

  it('快速新增之後的「開啟」在旁邊開新項目的面板', async () => {
    // Arrange
    const added = smkItem({ number: 4, title: '新的任務' });
    serveProject([...ITEMS, added]);
    serveItems('SMK', ITEMS);
    server.use(http.post('/v1/projects/SMK/items', () => ok(detailOf(added), 201)));
    const { user, router } = await openTable();
    const form = screen.getByRole('form', { name: '新增項目' });
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Act
    await user.click(await within(form).findByRole('link', { name: '開啟 SMK-4' }));

    // Assert
    expect(await findPanel(added)).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK?item=SMK-4');
  });

  it('面板顯示的項目記進最近看過的項目', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();

    // Act
    await user.click(keyLink(table, second));
    await within(await findPanel(second)).findByText('聯絡表單的說明');

    // Assert
    expect(readRecentItems()).toEqual([{ key: 'SMK-2', projectKey: 'SMK', title: '聯絡表單' }]);
  });
});

describe('項目面板：窄螢幕', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('網址帶著 item 時直接換到整頁的項目頁（窄螢幕不用面板）', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === '(max-width: 48rem)',
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    serveProject();

    // Act
    const { router } = renderApp('/projects/SMK?view=board&item=SMK-2');

    // Assert
    const title = await screen.findByRole('button', { name: '標題：聯絡表單' });
    expect(title.closest('h1')).not.toBeNull();
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-2');
    expect(within(screen.getByRole('navigation', { name: '位置' })).getByRole('link', { name: '冒煙測試' })).toHaveAttribute(
      'href',
      '/projects/SMK?view=board',
    );
    // 換到整頁是新的一筆：上一頁回到原本的清單（不帶 item，不會又被換回整頁）。
    await router.navigate(-1);
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK?view=board');
    });
  });

  it('換到整頁時 hash 一起帶過去（由留言產生的通知指著那一則留言）', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === '(max-width: 48rem)',
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    serveProject();

    // Act
    const { router } = renderApp('/projects/SMK?item=SMK-2#comment-abc');

    // Assert
    await screen.findByRole('button', { name: '標題：聯絡表單' });
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-2');
    expect(router.state.location.hash).toBe('#comment-abc');
  });

  it('開著面板時視窗變窄（跨過 48rem）：整頁是新的一筆，上一頁回到清單；瀏覽紀錄裡沒有重複的清單', async () => {
    // Arrange
    let isCompact = false;
    const listeners = new Set<() => void>();
    vi.stubGlobal('matchMedia', (query: string) => ({
      get matches() {
        return query === '(max-width: 48rem)' && isCompact;
      },
      media: query,
      addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
    }));
    serveProject();
    const { user, router } = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });
    await router.navigate('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(keyLink(table, second));
    await findPanel(second);

    // Act
    act(() => {
      isCompact = true;
      listeners.forEach((listener) => {
        listener();
      });
    });

    // Assert
    expect(await screen.findByRole('button', { name: '標題：聯絡表單' })).toBeInTheDocument();
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/items/SMK-2');
    });
    await router.navigate(-1);
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
    await router.navigate(-1);
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });
});
