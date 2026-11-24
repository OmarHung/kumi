import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { formatDate } from '../../domain/dates';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

/** 窄螢幕：讓 COMPACT_QUERY 符合（測試環境沒有 matchMedia，預設是寬螢幕）。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function serveCreate(projectKey: string, created: Item): unknown[] {
  const bodies: unknown[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items`, async ({ request }) => {
      bodies.push(await request.json());
      return ok(created, 201);
    }),
  );
  return bodies;
}

function urlParams(router: ReturnType<typeof renderApp>['router']): URLSearchParams {
  return new URLSearchParams(router.state.location.search);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('窄螢幕的專案表格', () => {
  it('換成清單：一筆兩行，第一行編號與標題、第二行狀態、負責人、到期日；連到項目頁', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '首頁設計稿', dueDate: '2026-12-01' }), smkItem({ number: 2, title: '切版', assignee: null })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const list = await screen.findByRole('list', { name: '項目' });
    expect(screen.queryByRole('table', { name: '項目' })).not.toBeInTheDocument();
    const first = within(list).getByRole('link', { name: /^SMK-1 首頁設計稿/ });
    expect(first).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
    expect(first).toHaveTextContent('待辦');
    expect(first).toHaveTextContent('範例使用者');
    expect(first).toHaveTextContent(formatDate('2026-12-01'));
    expect(within(list).getByRole('link', { name: /^SMK-2 切版/ })).toHaveTextContent('沒有負責人');
    expect(screen.getByText('已載入 2 個項目')).toBeInTheDocument();
  });

  it('點一筆就進到項目頁', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '首頁設計稿' })]);
    const { user, router } = renderApp('/projects/SMK');
    const list = await screen.findByRole('list', { name: '項目' });

    // Act
    await user.click(within(list).getByRole('link', { name: /^SMK-1/ }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-1');
  });

  it('篩選收在「篩選」裡，按鈕說明目前有幾個條件；排序是裡面的下拉，條件記在網址', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const { user, router } = renderApp('/projects/AGY?type=requirement&q=官網');
    await screen.findByRole('list', { name: '項目' });
    const toggle = screen.getByRole('button', { name: '篩選' });
    expect(toggle).toHaveAccessibleDescription('目前有 2 個條件');
    expect(screen.queryByRole('search')).not.toBeInTheDocument();

    // Act
    await user.click(toggle);
    const panel = screen.getByRole('dialog', { name: '篩選' });
    expect(within(panel).getByRole('searchbox', { name: '搜尋' })).toHaveValue('官網');
    expect(within(panel).getByRole('combobox', { name: '類型' })).toHaveTextContent('需求');
    await chooseOption(user, within(panel).getByRole('combobox', { name: '狀態' }), '開發');
    await chooseOption(user, within(panel).getByRole('combobox', { name: '排序' }), '到期日（遞減）');

    // Assert
    await waitFor(() => {
      expect(requests.at(-1)?.searchParams.get('sort')).toBe('due_date:desc');
    });
    expect(urlParams(router).toString()).toBe(new URLSearchParams('type=requirement&state=developing&q=官網&sort=due_date:desc').toString());
    expect(toggle).toHaveAccessibleDescription('目前有 3 個條件');

    // Act
    await user.click(within(panel).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(urlParams(router).toString()).toBe('sort=due_date%3Adesc');
    expect(toggle).not.toHaveAccessibleDescription();
  });

  it('排序選回預設（編號遞增）就從網址拿掉', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);
    const { user, router } = renderApp('/projects/SMK?sort=title:asc');
    await screen.findByRole('list', { name: '項目' });
    await user.click(screen.getByRole('button', { name: '篩選' }));
    const sort = within(screen.getByRole('dialog', { name: '篩選' })).getByRole('combobox', { name: '排序' });
    expect(sort).toHaveTextContent('標題（遞增）');

    // Act
    await chooseOption(user, sort, '編號（遞增）');

    // Assert
    expect(urlParams(router).get('sort')).toBeNull();
  });

  it('排序只有選了才換：方向鍵只移動反白、Esc 關上不換；Enter 選了才寫進網址', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveItems('SMK', [smkItem({ number: 1 })]);
    const { user, router } = renderApp('/projects/SMK');
    await screen.findByRole('list', { name: '項目' });
    await user.click(screen.getByRole('button', { name: '篩選' }));
    const sort = within(screen.getByRole('dialog', { name: '篩選' })).getByRole('combobox', { name: '排序' });
    const sentBefore = requests.length;

    // Act
    sort.focus();
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}');
    await screen.findByRole('listbox', { name: '排序' });
    await user.keyboard('{Escape}');

    // Assert
    expect(sort).toHaveAttribute('aria-expanded', 'false');
    expect(sort).toHaveTextContent('編號（遞增）');
    expect(screen.getByRole('dialog', { name: '篩選' })).toBeInTheDocument();
    expect(urlParams(router).get('sort')).toBeNull();
    expect(requests).toHaveLength(sentBefore);

    // Act
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    // Assert
    await waitFor(() => {
      expect(urlParams(router).get('sort')).toBe('number:desc');
    });
    expect(sort).toHaveTextContent('編號（遞減）');
  });

  it('「新增項目」開對話框：新增之後留著可以接著加，新的項目在清單最上面；關上後焦點回到按鈕', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '既有的項目' })]);
    const bodies = serveCreate('SMK', smkItem({ number: 2, title: '新的任務' }));
    const { user } = renderApp('/projects/SMK');
    const list = await screen.findByRole('list', { name: '項目' });
    expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    const open = screen.getByRole('button', { name: '新增項目' });

    // Act
    await user.click(open);
    const dialog = screen.getByRole('dialog', { name: '新增項目' });
    const title = within(dialog).getByRole('textbox', { name: '標題' });
    await user.type(title, '新的任務{Enter}');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已新增 SMK-2。');
    expect(bodies).toEqual([{ type: 'task', title: '新的任務' }]);
    expect(title).toHaveValue('');
    expect(title).toHaveFocus();
    const links = within(list).getAllByRole('link');
    expect(links[0]).toHaveAccessibleName(/^SMK-2 新的任務/);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));

    // Assert
    expect(screen.queryByRole('dialog', { name: '新增項目' })).not.toBeInTheDocument();
    expect(open).toHaveFocus();
  });

  it('多種類型時對話框裡先選類型；標題沒填時不送出', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', []);
    const bodies = serveCreate('AGY', agyItem({ number: 2, type: 'task', title: '切版' }));
    const { user } = renderApp('/projects/AGY');
    await user.click(await screen.findByRole('button', { name: '新增項目' }));
    const dialog = screen.getByRole('dialog', { name: '新增項目' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '標題' })).toHaveAccessibleDescription('請輸入標題。');
    expect(bodies).toHaveLength(0);

    // Act
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '類型' }), '任務');
    await user.type(within(dialog).getByRole('textbox', { name: '標題' }), '切版');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已新增 AGY-2。');
    expect(bodies).toEqual([{ type: 'task', title: '切版' }]);
  });

  it('沒有新增權限時沒有「新增項目」', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    await screen.findByRole('list', { name: '項目' });
    expect(screen.queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '篩選' })).toBeInTheDocument();
  });

  it('沒有項目時說明可以按「新增項目」', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText('這個專案還沒有項目')).toBeInTheDocument();
    expect(screen.getByText('按「新增項目」建立第一個項目。')).toBeInTheDocument();
  });
});

describe('寬螢幕的專案表格', () => {
  it('檢視、搜尋與篩選在同一列工具列；快速新增在表格上方；窄螢幕的「篩選」「新增項目」按鈕不出現', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    const views = screen.getByRole('navigation', { name: '版面' });
    const filters = screen.getByRole('group', { name: '篩選項目' });
    const quickAdd = screen.getByRole('form', { name: '新增項目' });
    const follows = (a: Node, b: Node) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
    expect(follows(views, filters)).toBe(true);
    expect(follows(filters, quickAdd)).toBe(true);
    expect(follows(quickAdd, table)).toBe(true);
    expect(screen.queryByRole('button', { name: '篩選' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
  });
});
