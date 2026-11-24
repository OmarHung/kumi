import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { savedViewOf, serveSavedViews } from '../../test/savedViews';

type App = ReturnType<typeof renderApp>;

async function openAgy(path: string, views = [savedViewOf('本週要交', { due: 'week', priority: 'high' })], options?: Parameters<typeof serveSavedViews>[2]) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const backend = serveSavedViews('AGY', views, options);
  const app = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...app, backend };
}

async function openMenu(app: App) {
  await app.user.click(await screen.findByRole('button', { name: /^檢視/ }));
  return screen.findByRole('menu', { name: '檢視' });
}

async function chooseItem(app: App, name: string | RegExp) {
  const menu = await openMenu(app);
  await app.user.click(entryOf(menu, name));
}

/** 檢視是「選一個」的項目（menuitemradio），其他是動作（menuitem）。 */
function entryOf(menu: HTMLElement, name: string | RegExp): HTMLElement {
  return within(menu).queryByRole('menuitemradio', { name }) ?? within(menu).getByRole('menuitem', { name });
}

function labels(menu: HTMLElement): string[] {
  return Array.from(menu.querySelectorAll('[role^="menuitem"]')).map((item) => item.textContent);
}

describe('檢視選單：套用', () => {
  it('列出自己的檢視，點了套用：條件導到網址，上一頁回到原本的條件', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?q=官網', [savedViewOf('本週要交', { due: 'week', priority: 'high', sort: 'due_date:asc' }), savedViewOf('沒人認領', { assignee: 'unassigned' })]);
    const menu = await openMenu(app);
    expect(labels(menu)).toEqual(['本週要交', '沒人認領', '另存成檢視…']);

    // Act
    await app.user.click(entryOf(menu, '本週要交'));

    // Assert
    expect(currentPath(app.router)).toBe(`/projects/AGY?${new URLSearchParams('due=week&priority=high&sort=due_date:asc').toString()}`);
    expect(screen.getByRole('combobox', { name: '到期' })).toHaveTextContent('本週到期');
    expect(screen.getByRole('button', { name: '檢視，目前是「本週要交」' })).toHaveFocus();
    await app.router.navigate(-1);
    await waitFor(() => {
      expect(currentPath(app.router)).toBe('/projects/AGY?q=官網');
    });
  });

  it('存了目前的週期的檢視：套用之後網址是 cycle=current，下拉選「目前的週期」', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY', [savedViewOf('這個衝刺', { cycle: 'current', milestone: 'none' })]);

    // Act
    await chooseItem(app, '這個衝刺');

    // Assert
    expect(currentPath(app.router)).toBe('/projects/AGY?cycle=current&milestone=none');
  });

  it('看板的檢視切到看板，只帶看板有的條件', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY', [savedViewOf('看板上我的', { assignee: 'me', priority: 'urgent' }, { layout: 'board' })]);

    // Act
    await chooseItem(app, '看板上我的');

    // Assert
    await screen.findByRole('region', { name: '看板' });
    expect(currentPath(app.router)).toBe(`/projects/AGY?${new URLSearchParams('view=board&assignee=me&priority=urgent').toString()}`);
  });

  it('用鍵盤：方向鍵移到檢視、Enter 套用', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY');
    await app.user.click(await screen.findByRole('button', { name: '檢視' }));
    await screen.findByRole('menu', { name: '檢視' });

    // Act
    await app.user.keyboard('{Enter}');

    // Assert
    expect(currentPath(app.router)).toContain('due=week');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('檢視裡藍圖已經沒有的類型略過，其他條件照套用', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY', [savedViewOf('舊的', { type: 'gone', due: 'today' })]);

    // Act
    await chooseItem(app, '舊的');

    // Assert
    expect(currentPath(app.router)).toBe('/projects/AGY?due=today');
  });
});

describe('檢視選單：目前的檢視', () => {
  it('條件和某個檢視完全一樣時，按鈕寫它的名稱，選單多了「重新命名」與「刪除」，沒有「更新」', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(screen.getByRole('button', { name: '檢視，目前是「本週要交」' })).toBeInTheDocument();
    expect(labels(menu)).toEqual(['本週要交', '另存成檢視…', '重新命名…', '刪除「本週要交」']);
    expect(within(menu).getByRole('menuitemradio', { name: '本週要交' })).toHaveAttribute('aria-checked', 'true');
    expect(within(menu).getByRole('menuitemradio', { name: '本週要交' }).querySelector('[data-icon="check"]')).not.toBeNull();
  });

  it('條件不一樣時按鈕只寫「檢視」，選單沒有重新命名與刪除', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week');

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(screen.getByRole('button', { name: '檢視' })).toBeInTheDocument();
    expect(labels(menu)).toEqual(['本週要交', '另存成檢視…']);
  });

  it('套用了某個檢視又改了條件：多一個「更新成目前的條件」，按了用目前的版面與條件整個換掉', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY');
    await chooseItem(app, '本週要交');
    await app.user.click(within(screen.getByRole('group', { name: '篩選項目' })).getByRole('button', { name: '清除條件' }));
    const filters = screen.getByRole('group', { name: '篩選項目' });
    await app.user.click(within(filters).getByRole('combobox', { name: '優先度' }));
    await app.user.click(await screen.findByRole('option', { name: '緊急' }));
    const menu = await openMenu(app);
    expect(labels(menu)).toEqual(['本週要交', '更新「本週要交」成目前的條件', '另存成檢視…']);

    // Act
    await app.user.click(entryOf(menu, '更新「本週要交」成目前的條件'));

    // Assert
    await waitFor(() => {
      expect(app.backend.replaced).toHaveLength(1);
    });
    expect(app.backend.replaced[0]?.body).toEqual({
      name: '本週要交',
      layout: 'table',
      query: { type: null, state: null, assignee: null, q: null, sort: null, due: null, priority: 'urgent', cycle: null, milestone: null, fields: null, group: null, category: null, project: null },
    });
    expect(await screen.findByRole('button', { name: '檢視，目前是「本週要交」' })).toBeInTheDocument();
  });

  it('更新失敗時說明原因，可以再試一次', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY', undefined, { rejectWith: { status: 503, code: 'server.not_ready', message: 'starting' } });
    await chooseItem(app, '本週要交');
    await app.user.click(within(screen.getByRole('group', { name: '篩選項目' })).getByRole('button', { name: '清除條件' }));
    await app.user.click(within(screen.getByRole('group', { name: '篩選項目' })).getByRole('combobox', { name: '優先度' }));
    await app.user.click(await screen.findByRole('option', { name: '緊急' }));
    await chooseItem(app, '更新「本週要交」成目前的條件');

    // Act
    const dialog = await screen.findByRole('dialog', { name: '無法更新檢視「本週要交」' });

    // Assert
    expect(dialog).toHaveTextContent('伺服器暫時無法處理');
    await app.user.click(within(dialog).getByRole('button', { name: '取消' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('檢視按鈕與「更新」那一項', () => {
  it('按鈕看得到的字永遠是「檢視」（寬度不隨狀態變），目前的檢視只給報讀軟體', async () => {
    // Arrange
    await openAgy('/projects/AGY?due=week&priority=high');

    // Act
    const button = await screen.findByRole('button', { name: '檢視，目前是「本週要交」' });

    // Assert
    expect(button.querySelector('.visually-hidden')).toHaveTextContent('，目前是「本週要交」');
    expect(button.firstElementChild).toHaveTextContent(/^檢視$/);
  });

  it('目前沒有任何條件（沒篩選、沒搜尋、預設排序）時不顯示「更新」：不會把檢視清成空的', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY');
    await chooseItem(app, '本週要交');
    await app.user.click(within(screen.getByRole('group', { name: '篩選項目' })).getByRole('button', { name: '清除條件' }));

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(labels(menu)).toEqual(['本週要交', '另存成檢視…']);
  });

  it('版面和檢視不同時，「更新」註明現在的版面（更新後版面也會換）', async () => {
    // Arrange：套用表格的檢視，再切到看板、加一個條件
    const app = await openAgy('/projects/AGY');
    await chooseItem(app, '本週要交');
    await app.user.click(screen.getByRole('link', { name: '看板' }));
    await screen.findByRole('region', { name: '看板' });
    await app.user.click(within(screen.getByRole('group', { name: '篩選看板' })).getByRole('combobox', { name: '優先度' }));
    await app.user.click(await screen.findByRole('option', { name: '緊急' }));

    // Act
    const menu = await openMenu(app);

    // Assert
    expect(labels(menu)).toContain('更新「本週要交」成目前的條件（看板）');
  });
});
