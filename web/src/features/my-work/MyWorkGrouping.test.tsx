import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { appProject, daysFromToday, exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

// 「這一週」要看今天是星期幾（docs 的規則：一週從週一開始），在 domain/dueGroups.test.ts 注入今天測；
// 這裡只用和星期幾無關的日子：前天（已過期）、今天、8 天後（一定在下一週之後）、沒有到期日。
const overdueWeb = makeItem({ number: 3, title: '首頁改版需求', dueDate: daysFromToday(-2) });
const overdueApp = makeItem({ projectKey: 'APP', number: 7, title: '登入流程', dueDate: daysFromToday(-2) });
const todayWeb = makeItem({ number: 1, title: '寄出報價', dueDate: daysFromToday(0) });
const laterWeb = makeItem({ number: 8, title: '上線檢查', dueDate: daysFromToday(8) });
const undatedWeb = makeItem({ number: 5, title: '整理素材' });
const all = [undatedWeb, laterWeb, todayWeb, overdueWeb, overdueApp];

function headingNames(): (string | null)[] {
  return screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent);
}

function rowKeys(table: HTMLElement): (string | null | undefined)[] {
  return within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]?.textContent);
}

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('我的工作：依急迫程度分組（預設）', () => {
  it('每一組有標題（h2）與數量，照已過期、今天、之後、沒有到期日排；空的組不出現', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: '已過期 2 項' })).toBeInTheDocument();
    expect(headingNames()).toEqual(['已過期 2 項', '今天 1 項', '之後 1 項', '沒有到期日 1 項']);
    expect(screen.queryByRole('heading', { level: 2, name: /^這一週/ })).not.toBeInTheDocument();
  });

  it('每一組是一張有名稱的表格（名稱是組名），組內照到期日再照編號；第一筆內容是第一組的第一列', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });

    // Act
    renderApp('/my-work');

    // Assert
    const overdue = await screen.findByRole('table', { name: '已過期' });
    expect(rowKeys(overdue)).toEqual(['APP-7', 'WEB-3']);
    expect(rowKeys(screen.getByRole('table', { name: '今天' }))).toEqual(['WEB-1']);
    expect(rowKeys(screen.getByRole('table', { name: '之後' }))).toEqual(['WEB-8']);
    expect(rowKeys(screen.getByRole('table', { name: '沒有到期日' }))).toEqual(['WEB-5']);
    expect(within(overdue).getAllByRole('row')[1]).toHaveAttribute('data-content-start');
    expect(within(overdue).getByRole('columnheader', { name: '專案' })).toBeInTheDocument();
  });

  it('分組是工具列右邊的下拉，預設選的是「依急迫程度」', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdueWeb] });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('combobox', { name: '分組' })).toHaveTextContent('依急迫程度');
    expect(screen.queryByRole('navigation', { name: '分組' })).not.toBeInTheDocument();
  });
});

describe('我的工作：依專案分組', () => {
  it('切到「依專案」：網址記住 group=project，每個專案一組（照代號排），組名是專案名稱，不再重複專案那一欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });
    const { user, router } = renderApp('/my-work');
    const grouping = await screen.findByRole('combobox', { name: '分組' });

    // Act
    await chooseOption(user, grouping, '依專案');

    // Assert
    expect(currentPath(router)).toBe('/my-work?group=project');
    // 組名是專案名稱：兩個專案的內容各自讀回來，順序不一定，等兩組都有名稱。
    const app = await screen.findByRole('table', { name: '會員 App' });
    await waitFor(() => {
      expect(headingNames()).toEqual(['會員 App 1 項', '官網改版 4 項']);
    });
    expect(rowKeys(await screen.findByRole('table', { name: '官網改版' }))).toEqual(['WEB-3', 'WEB-1', 'WEB-8', 'WEB-5']);
    expect(within(app).queryByRole('columnheader', { name: '專案' })).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '分組' })).toHaveTextContent('依專案');
  });

  it('網址帶著 group=project（重新整理、分享）時就是依專案', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });

    // Act
    renderApp('/my-work?group=project');

    // Assert
    expect(await screen.findByRole('table', { name: '會員 App' })).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: '官網改版' })).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '已過期' })).not.toBeInTheDocument();
  });

  it('換分組時開著的項目面板留著；換回依急迫程度時網址拿掉 group', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdueWeb, undatedWeb] });
    serveItem(detailOf(overdueWeb, '首頁要改版的說明'));
    serveActivity(overdueWeb.key, []);
    serveItems('WEB', []);
    const { user, router } = renderApp('/my-work?item=WEB-3');
    expect(await screen.findByRole('complementary', { name: '項目 WEB-3' })).toBeInTheDocument();
    const grouping = screen.getByRole('combobox', { name: '分組' });

    // Act
    await chooseOption(user, grouping, '依專案');
    await screen.findByRole('table', { name: '官網改版' });
    await chooseOption(user, grouping, '依急迫程度');

    // Assert
    expect(await screen.findByRole('table', { name: '已過期' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/my-work?item=WEB-3');
    expect(screen.getByRole('complementary', { name: '項目 WEB-3' })).toBeInTheDocument();
  });
});

describe('我的工作：面板的上一個／下一個', () => {
  it('照畫面上分好組的順序走，跨過組的邊界', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [undatedWeb, overdueWeb] });
    serveItem(detailOf(overdueWeb, '首頁要改版的說明'));
    serveItem(detailOf(undatedWeb, '素材的說明'));
    serveActivity(overdueWeb.key, []);
    serveActivity(undatedWeb.key, []);
    serveItems('WEB', []);
    const { user, router } = renderApp('/my-work');
    const overdue = await screen.findByRole('table', { name: '已過期' });
    await user.click(within(overdue).getByRole('link', { name: '首頁改版需求' }));
    const panel = await screen.findByRole('complementary', { name: '項目 WEB-3' });
    await within(panel).findByText('首頁要改版的說明');

    // Act
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?item=WEB-5');
    });
    expect(await within(panel).findByText('素材的說明')).toBeInTheDocument();
  });
});

describe('窄螢幕的我的工作：分組', () => {
  it('清單也分組：每一組一份有名稱的清單，前面是標題與數量；第一筆內容是第一組的第一筆', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });

    // Act
    renderApp('/my-work');

    // Assert
    const overdue = await screen.findByRole('list', { name: '已過期' });
    expect(headingNames()).toEqual(['已過期 2 項', '今天 1 項', '之後 1 項', '沒有到期日 1 項']);
    expect(within(overdue).getAllByRole('link').map((link) => link.getAttribute('href'))).toEqual([
      '/projects/APP/items/APP-7',
      '/projects/WEB/items/WEB-3',
    ]);
    expect(within(overdue).getAllByRole('listitem')[0]).toHaveAttribute('data-content-start');
    expect(screen.getByRole('list', { name: '沒有到期日' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('依專案時第二行不再重複專案', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdueWeb] });

    // Act
    renderApp('/my-work?group=project');

    // Assert
    const list = await screen.findByRole('list', { name: '官網改版' });
    expect(within(list).getByRole('link')).toHaveAccessibleName(/^WEB-3 首頁改版需求 狀態：待辦 到期：/);
  });
});
