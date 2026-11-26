import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, Link, useSearchParams } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { describe, expect, it } from 'vitest';
import { queryKeys } from '../../api/queryKeys';
import { createQueryClient } from '../../app/queryClient';
import { CurrentUserProvider } from '../../auth/CurrentUser';
import { myWorkFiltersFromParams, myWorkLayoutOf } from '../../domain/myWorkView';
import { signedInAs } from '../../test/api';
import { agyDetail, exampleOwner } from '../../test/fixtures';
import { currentPath } from '../../test/renderApp';
import { MY_WORK_VIEW_LIMIT, savedViewOf, serveMyWorkViews } from '../../test/savedViews';
import { groupingOf } from '../my-work/myWorkGroups';
import { myWorkSortOf } from '../my-work/myWorkSort';
import { MyWorkViewMenu } from './MyWorkViewMenu';

/**
 * 最小的外殼：和「我的工作」頁一樣，目前的版面、條件、分組與排序都從網址讀出來再交給選單；
 * 「改成緊急」是改網址上的條件，用來檢查套用之後、改了條件之後選單的樣子。
 */
function Harness() {
  const [params] = useSearchParams();
  const current = {
    layout: myWorkLayoutOf(params),
    filters: myWorkFiltersFromParams(params),
    grouping: groupingOf(params),
    sort: myWorkSortOf(params),
  };
  return (
    <>
      <MyWorkViewMenu current={current} />
      <Link to="/my-work?priority=urgent">改成緊急</Link>
    </>
  );
}

async function openMyWork(path: string, views = [savedViewOf('本週要交', { due: 'week', priority: 'high' })], options?: Parameters<typeof serveMyWorkViews>[1]) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  const backend = serveMyWorkViews(views, options);
  const router = createMemoryRouter([{ path: '/my-work', element: <Harness /> }], { initialEntries: [path] });
  const user = userEvent.setup();
  const queryClient = createQueryClient({ queries: { retry: false } });
  render(
    <QueryClientProvider client={queryClient}>
      <CurrentUserProvider user={exampleOwner}>
        <RouterProvider router={router} />
      </CurrentUserProvider>
    </QueryClientProvider>,
  );
  await screen.findByRole('button', { name: /^檢視/ });
  return { router, user, backend, queryClient };
}

type App = Awaited<ReturnType<typeof openMyWork>>;

async function openMenu(app: App) {
  await app.user.click(await screen.findByRole('button', { name: /^檢視/ }));
  return screen.findByRole('menu', { name: '檢視' });
}

function entryOf(menu: HTMLElement, name: string | RegExp): HTMLElement {
  return within(menu).queryByRole('menuitemradio', { name }) ?? within(menu).getByRole('menuitem', { name });
}

async function chooseItem(app: App, name: string | RegExp) {
  await app.user.click(entryOf(await openMenu(app), name));
}

function labels(menu: HTMLElement): string[] {
  return Array.from(menu.querySelectorAll('[role^="menuitem"]')).map((item) => item.textContent);
}

const EMPTY_QUERY = {
  type: null, state: null, assignee: null, createdBy: null, mine: null, q: null, sort: null, due: null, priority: null,
  cycle: null, milestone: null, fields: null, category: null, project: null, group: null,
};

describe('「我的工作」的檢視選單：套用', () => {
  it('列出自己的檢視，點了導到 /my-work 加上那組條件，上一頁回到原本的', async () => {
    // Arrange
    const app = await openMyWork('/my-work?q=官網', [savedViewOf('完成的', { category: 'done' }, { layout: 'board' }), savedViewOf('舊的存法', { assignee: 'unassigned' })]);
    const menu = await openMenu(app);
    expect(labels(menu)).toEqual(['完成的', '舊的存法', '另存成檢視…']);

    // Act
    await app.user.click(entryOf(menu, '完成的'));

    // Assert
    expect(currentPath(app.router)).toBe(`/my-work?${new URLSearchParams('view=board&category=done').toString()}`);
    expect(await screen.findByRole('button', { name: '檢視，目前是「完成的」' })).toHaveFocus();
    await app.router.navigate(-1);
    await waitFor(() => {
      expect(currentPath(app.router)).toBe('/my-work?q=官網');
    });
  });

  it('檢視本身不存開著的面板；套用時和專案頁一樣，目前開著的面板留著', async () => {
    // Arrange
    const app = await openMyWork('/my-work?item=AGY-1');

    // Act
    await chooseItem(app, '本週要交');

    // Assert
    expect(currentPath(app.router)).toBe(`/my-work?${new URLSearchParams('due=week&priority=high&item=AGY-1').toString()}`);
  });

  it('依專案分組的表格檢視帶 group=project；專案的狀態一起帶', async () => {
    // Arrange
    const app = await openMyWork('/my-work', [savedViewOf('客戶那邊', { project: 'cm', state: 'waiting_client', group: 'project' })]);

    // Act
    await chooseItem(app, '客戶那邊');

    // Assert
    expect(currentPath(app.router)).toBe(`/my-work?${new URLSearchParams('state=waiting_client&project=CM&group=project').toString()}`);
  });

  it('網址已經一樣就不再導航', async () => {
    // Arrange
    const app = await openMyWork('/my-work?due=week&priority=high');
    const before = app.router.state.historyAction;

    // Act
    await chooseItem(app, '本週要交');

    // Assert
    expect(app.router.state.historyAction).toBe(before);
  });
});

describe('「我的工作」的檢視選單：目前的檢視', () => {
  it('條件和某個檢視完全一樣時按鈕寫它的名稱，選單多了「重新命名」與「刪除」', async () => {
    // Arrange
    const app = await openMyWork('/my-work?due=week&priority=high');

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(screen.getByRole('button', { name: '檢視，目前是「本週要交」' })).toBeInTheDocument();
    expect(labels(menu)).toEqual(['本週要交', '另存成檢視…', '重新命名…', '刪除「本週要交」']);
    expect(within(menu).getByRole('menuitemradio', { name: '本週要交' })).toHaveAttribute('aria-checked', 'true');
  });

  it('預設的條件（我負責、未結束）沒有檢視可比時，按鈕只寫「檢視」', async () => {
    // Arrange
    const app = await openMyWork('/my-work');

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(screen.getByRole('button', { name: '檢視' })).toBeInTheDocument();
    expect(labels(menu)).toEqual(['本週要交', '另存成檢視…']);
  });

  it('套用後改了條件：多一個「更新成目前的條件」，按了用目前的版面與條件整個換掉', async () => {
    // Arrange
    const app = await openMyWork('/my-work');
    await chooseItem(app, '本週要交');
    await app.user.click(screen.getByRole('link', { name: '改成緊急' }));
    const menu = await openMenu(app);
    expect(labels(menu)).toEqual(['本週要交', '更新「本週要交」成目前的條件', '另存成檢視…']);

    // Act
    await app.user.click(entryOf(menu, '更新「本週要交」成目前的條件'));

    // Assert
    await waitFor(() => {
      expect(app.backend.replaced).toHaveLength(1);
    });
    expect(app.backend.replaced[0]?.body).toEqual({ name: '本週要交', layout: 'table', query: { ...EMPTY_QUERY, priority: 'urgent' } });
    expect(await screen.findByRole('button', { name: '檢視，目前是「本週要交」' })).toBeInTheDocument();
  });

  it('更新失敗時說明原因，可以取消', async () => {
    // Arrange
    const app = await openMyWork('/my-work', undefined, { rejectWith: { status: 503, code: 'server.not_ready', message: 'starting' } });
    await chooseItem(app, '本週要交');
    await app.user.click(screen.getByRole('link', { name: '改成緊急' }));
    await chooseItem(app, '更新「本週要交」成目前的條件');

    // Act
    const dialog = await screen.findByRole('dialog', { name: '無法更新檢視「本週要交」' });
    await app.user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('目前沒有任何條件時不給「更新」，不會把檢視清成空的', async () => {
    // Arrange
    const app = await openMyWork('/my-work', [savedViewOf('預設的看板', {}, { layout: 'board' }), savedViewOf('本週要交', { due: 'week' })]);
    await chooseItem(app, '本週要交');
    await app.router.navigate('/my-work');

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(labels(menu)).toEqual(['本週要交', '預設的看板', '另存成檢視…']);
  });
});

describe('「我的工作」的檢視選單：另存', () => {
  it('名稱預設是目前條件的簡短說法，存下版面、條件與分組，按鈕寫新檢視的名稱', async () => {
    // Arrange
    const app = await openMyWork('/my-work?assignee=all&category=done&group=project', []);

    // Act
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(name).toHaveValue('完成・依專案');
    await app.user.clear(name);
    await app.user.type(name, '  做完的  {Enter}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(app.backend.created[0]?.body).toEqual({
      name: '做完的',
      layout: 'table',
      query: { ...EMPTY_QUERY, assignee: null, category: 'done', group: 'project' },
    });
    expect(await screen.findByRole('button', { name: '檢視，目前是「做完的」' })).toHaveFocus();
  });

  it('看板上另存：版面是看板，分組不存', async () => {
    // Arrange
    const app = await openMyWork('/my-work?view=board&due=week&priority=high&group=project', []);

    // Act
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('看板・本週到期・高');
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(app.backend.created).toHaveLength(1);
    });
    expect(app.backend.created[0]?.body).toMatchObject({ layout: 'board', query: { ...EMPTY_QUERY, due: 'week', priority: 'high' } });
  });

  it('名稱重複時留在欄位上，改了名稱就能再送', async () => {
    // Arrange
    const app = await openMyWork('/my-work?due=today', [savedViewOf('今天到期')]);
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    await app.user.clear(name);
    await app.user.type(name, '今天到期');

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('已經有同名的檢視。')).toBeInTheDocument();
    await app.user.type(name, '的');
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(app.backend.current().map((view) => view.name)).toContain('今天到期的');
  });

  it('檢視滿了時，說明放在表單上方', async () => {
    // Arrange
    const full = Array.from({ length: MY_WORK_VIEW_LIMIT }, (_, index) => savedViewOf(`檢視 ${String(index)}`));
    const app = await openMyWork('/my-work?due=today', full);
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('最多');
  });

  it('預設名稱裡的類型用名稱（不是 key）', async () => {
    // Arrange
    const app = await openMyWork('/my-work?type=requirement&category=done', []);
    await waitFor(() => {
      expect(app.queryClient.getQueryData(queryKeys.itemTypes)).toBeDefined();
    });

    // Act
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('需求・完成');
  });
});

describe('「我的工作」的檢視選單：重新命名與刪除', () => {
  it('重新命名只改名稱，版面與條件照舊', async () => {
    // Arrange
    const app = await openMyWork('/my-work?due=week&priority=high');
    await chooseItem(app, '重新命名…');
    const dialog = await screen.findByRole('dialog', { name: '重新命名檢視' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });

    // Act
    await app.user.clear(name);
    await app.user.type(name, '這週的高優先{Enter}');

    // Assert
    await waitFor(() => {
      expect(app.backend.replaced).toHaveLength(1);
    });
    expect(app.backend.replaced[0]?.body).toEqual({ name: '這週的高優先', layout: 'table', query: { ...EMPTY_QUERY, due: 'week', priority: 'high' } });
    expect(await screen.findByRole('button', { name: '檢視，目前是「這週的高優先」' })).toBeInTheDocument();
  });

  it('刪除前確認，確認後清單裡沒有了，網址的條件不變', async () => {
    // Arrange
    const app = await openMyWork('/my-work?due=week&priority=high');
    await chooseItem(app, '刪除「本週要交」');
    const dialog = await screen.findByRole('dialog', { name: '刪除檢視「本週要交」？' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(app.backend.deleted).toHaveLength(1);
    expect(app.backend.current()).toHaveLength(0);
    expect(currentPath(app.router)).toBe('/my-work?due=week&priority=high');
    expect(await screen.findByRole('button', { name: '檢視' })).toBeInTheDocument();
  });

  it('檢視在別處已經被刪掉：刪除失敗，原因留在對話框裡', async () => {
    // Arrange
    const app = await openMyWork('/my-work?due=week&priority=high');
    await chooseItem(app, '刪除「本週要交」');
    const dialog = await screen.findByRole('dialog', { name: '刪除檢視「本週要交」？' });
    app.backend.drop('view-本週要交');

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(dialog).findByText('找不到這個檢視。')).toBeInTheDocument();
  });
});
