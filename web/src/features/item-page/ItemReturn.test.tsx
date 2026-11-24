import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { readRecentItems } from '../jump/recentItems';
import { readProjectView, saveProjectView } from '../projects/projectViewPreference';
import { created, serveActivity } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { makeChangeSet, makeChangeSetDetail, serveChangeSet, serveChangeSets } from '../../test/changesets';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveDeleteItem, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const first = smkItem({ number: 1, title: '首頁設計稿' });
const second = smkItem({ number: 2, title: '聯絡表單' });

function serveProject(items: readonly Item[] = [first, second]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  items.forEach((item) => {
    serveItem(detailOf(item, `${item.title}的說明`));
    serveActivity(item.key, [created()]);
  });
}

function stubCompact() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(max-width: 48rem)',
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function locationCrumbs(): HTMLElement {
  return screen.getByRole('navigation', { name: '位置' });
}

describe('回得去：從整頁回到清單', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('窄螢幕從清單進到整頁，用上一頁回來時焦點放回那一筆', async () => {
    // Arrange
    stubCompact();
    serveProject();
    serveItems('SMK', [first, second]);
    // 清單不是這份文件的第一筆紀錄（第一筆的 key 恆為 default，不記，見 returnFocus.ts）：先從我的工作換過去。
    const { user, router } = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });
    await router.navigate('/projects/SMK');
    const list = await screen.findByRole('list', { name: '項目' });
    await user.click(within(list).getByRole('link', { name: /^SMK-2 聯絡表單/ }));
    await screen.findByText('聯絡表單的說明');

    // Act
    await router.navigate(-1);

    // Assert
    const again = await screen.findByRole('list', { name: '項目' });
    await waitFor(() => {
      expect(within(again).getByRole('link', { name: /^SMK-2 聯絡表單/ })).toHaveFocus();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
  });

  it('重新載入清單（第一筆紀錄的 key 是 default）時，不會被以前記下的焦點拉走', async () => {
    // Arrange
    sessionStorage.setItem('item.returnFocus', JSON.stringify([['default', 'SMK-2']]));
    serveProject();
    serveItems('SMK', [first, second]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(within(table).getByRole('link', { name: '聯絡表單' })).not.toHaveFocus();
  });

  it('「展開成整頁」：麵包屑的專案連結回到原本的看板與條件（不帶面板），上一頁回到開著的面板', async () => {
    // Arrange
    serveProject();
    serveItemsByQuery('SMK', [first, second]);
    const { user, router } = renderApp('/projects/SMK?view=board&q=表單&item=SMK-2');
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-2' });
    const expand = within(panel).getByRole('link', { name: '展開成整頁' });
    expect(expand).toHaveAttribute('href', '/projects/SMK/items/SMK-2');

    // Act
    await user.click(expand);

    // Assert
    await screen.findByRole('heading', { level: 1, name: /聯絡表單/ });
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-2');
    const crumb = within(locationCrumbs()).getByRole('link', { name: '冒煙測試' });
    expect(decodeURI(crumb.getAttribute('href') ?? '')).toBe('/projects/SMK?view=board&q=表單');
    await router.navigate(-1);
    expect(await screen.findByRole('complementary', { name: '項目 SMK-2' })).toBeInTheDocument();
    expect(new URLSearchParams(router.state.location.search).get('q')).toBe('表單');
  });

  it('整頁顯示的項目記進最近看過的項目', async () => {
    // Arrange
    serveProject();

    // Act
    renderApp('/projects/SMK/items/SMK-1');

    // Assert
    await screen.findByText('首頁設計稿的說明');
    expect(readRecentItems()).toEqual([{ key: 'SMK-1', projectKey: 'SMK', title: '首頁設計稿' }]);
  });
});

describe('記住每個專案上次用的檢視', () => {
  it('用過看板之後，側欄的專案連結開看板；換回表格就開表格', async () => {
    // Arrange
    serveProject();
    serveItemsByQuery('SMK', [first, second]);
    const { user } = renderApp('/projects/SMK?view=board');
    await screen.findByRole('region', { name: '看板' });
    const nav = screen.getByRole('navigation', { name: '主要導覽' });

    // Assert
    expect(within(nav).getByRole('link', { name: /冒煙測試/ })).toHaveAttribute('href', '/projects/SMK?view=board');
    expect(within(nav).getByRole('link', { name: /冒煙測試/ })).toHaveAttribute('aria-current', 'page');

    // Act
    await user.click(within(screen.getByRole('navigation', { name: '版面' })).getByRole('link', { name: '表格' }));

    // Assert
    await screen.findByRole('table', { name: '項目' });
    expect(within(nav).getByRole('link', { name: /冒煙測試/ })).toHaveAttribute('href', '/projects/SMK');
  });

  it('直接打開項目頁時，麵包屑的專案連結照上次用的檢視開', async () => {
    // Arrange
    serveProject();
    saveProjectView('SMK', 'board');

    // Act
    renderApp('/projects/SMK/items/SMK-1');

    // Assert
    await screen.findByText('首頁設計稿的說明');
    expect(within(locationCrumbs()).getByRole('link', { name: '冒煙測試' })).toHaveAttribute('href', '/projects/SMK?view=board');
  });

  it('不帶 view 的連結（例如別人貼的網址）開的表格不會洗掉記住的看板', async () => {
    // Arrange
    serveProject();
    serveItems('SMK', [first, second]);
    saveProjectView('SMK', 'board');

    // Act
    renderApp('/projects/SMK');

    // Assert
    await screen.findByRole('table', { name: '項目' });
    expect(readProjectView('SMK')).toBe('board');
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    expect(within(nav).getByRole('link', { name: /冒煙測試/ })).toHaveAttribute('href', '/projects/SMK?view=board');
  });

  it('專案清單的專案連結照上次用的檢視開', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    saveProjectView('SMK', 'board');

    // Act
    renderApp('/projects');

    // Assert
    const table = await screen.findByRole('table', { name: '專案' });
    expect(within(table).getByRole('link', { name: '冒煙測試' })).toHaveAttribute('href', '/projects/SMK?view=board');
  });

  it('設定頁、變更集的清單與預覽：麵包屑的專案連結照上次用的檢視開', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', []);
    serveChangeSet('SMK', makeChangeSetDetail(simpleTasksBlueprint, { changeSet: makeChangeSet({ number: 3 }) }));
    saveProjectView('SMK', 'board');
    const projectCrumb = () => within(locationCrumbs()).getByRole('link', { name: '冒煙測試' });

    // Act
    const { router } = renderApp('/projects/SMK/settings/workflows');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '設定' })).toBeInTheDocument();
    expect(projectCrumb()).toHaveAttribute('href', '/projects/SMK?view=board');
    await router.navigate('/projects/SMK/changesets');
    expect(await screen.findByRole('heading', { level: 1, name: '變更集' })).toBeInTheDocument();
    expect(projectCrumb()).toHaveAttribute('href', '/projects/SMK?view=board');
    await router.navigate('/projects/SMK/changesets/3');
    await waitFor(() => {
      expect(within(locationCrumbs()).getByText('#3')).toBeInTheDocument();
    });
    expect(projectCrumb()).toHaveAttribute('href', '/projects/SMK?view=board');
  });

  it('直接打開的項目頁：刪除之後與找不到時「回到專案」都照上次用的檢視', async () => {
    // Arrange
    serveProject();
    serveItemsByQuery('SMK', [second]);
    serveDeleteItem(first);
    server.use(http.get('/v1/items/SMK-99', () => failure(404, 'item.not_found', '找不到這個項目。')));
    saveProjectView('SMK', 'board');
    const { user, router } = renderApp('/projects/SMK/items/SMK-1');
    await screen.findByText('首頁設計稿的說明');

    // Act
    await user.click(screen.getByRole('button', { name: 'SMK-1 的更多動作' }));

    // Act
    await user.click(screen.getByRole('menuitem', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK?view=board');
    });
    await router.navigate('/projects/SMK/items/SMK-99');
    expect(await screen.findByRole('link', { name: '回到專案' })).toHaveAttribute('href', '/projects/SMK?view=board');
  });

  it('網址上有 view 時以網址為準：記著看板，網址說表格就是表格', async () => {
    // Arrange
    serveProject();
    serveItems('SMK', [first, second]);
    saveProjectView('SMK', 'board');

    // Act
    renderApp('/projects/SMK?view=table');

    // Assert
    expect(await screen.findByRole('table', { name: '項目' })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    expect(within(nav).getByRole('link', { name: /冒煙測試/ })).toHaveAttribute('href', '/projects/SMK');
  });
});
