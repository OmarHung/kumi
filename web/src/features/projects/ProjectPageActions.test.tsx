import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BELOW_WIDE_QUERY, COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';

/** 窄螢幕：只有窄螢幕的查詢符合。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

/** 平板（48～64rem）：只有「不到 64rem」成立。 */
function stubTabletScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === BELOW_WIDE_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openProject(details = [agyDetail()]) {
  signedInAs(exampleOwner, { details });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp('/projects/AGY');
  await screen.findByRole('heading', { level: 1 });
  return view;
}

describe('專案頁頁列的動作', () => {
  it('寬螢幕：設定、規劃、成員與「更多」在頁列上，其他動作收在「更多」裡，沒有「專案的動作」', async () => {
    // Arrange / Act
    await openProject();

    // Assert
    const main = screen.getByRole('main');
    expect(within(main).getByRole('link', { name: '資訊' })).toHaveAttribute('href', '/projects/AGY/info');
    expect(within(main).getByRole('link', { name: '設定' })).toBeInTheDocument();
    expect(within(main).getByRole('link', { name: '規劃' })).toBeInTheDocument();
    expect(within(main).getByRole('button', { name: '成員' })).toBeInTheDocument();
    expect(within(main).getByRole('button', { name: '專案的更多動作' })).toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: '已刪除' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('button', { name: '專案的動作' })).not.toBeInTheDocument();
  });

  it('寬螢幕：「更多」排在「成員」後面', async () => {
    // Arrange & Act
    await openProject();

    // Assert
    const members = screen.getByRole('button', { name: '成員' });
    const more = screen.getByRole('button', { name: '專案的更多動作' });
    expect(members.compareDocumentPosition(more) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('窄螢幕：動作收進一顆「專案的動作」，頁列上沒有那幾顆，專案名稱不被擠', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    await openProject();

    // Assert
    const main = screen.getByRole('main');
    expect(within(main).getByRole('button', { name: '專案的動作' })).toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: '資訊' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: '設定' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: '規劃' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('button', { name: '成員' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: '已刪除' })).not.toBeInTheDocument();
  });

  it('窄螢幕：選單的項目照順序（設定、規劃、成員，匯入、已刪除，改名、封存）', async () => {
    // Arrange
    stubCompactScreen();
    const { user } = await openProject();

    // Act
    await user.click(screen.getByRole('button', { name: '專案的動作' }));

    // Assert
    const menu = await screen.findByRole('menu', { name: '專案的動作' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      '設定',
      '規劃',
      '成員',
      '匯入項目…',
      '已刪除的項目',
      '重新命名…',
      '封存專案',
    ]);
  });

  it('窄螢幕：選「規劃」導到規劃頁', async () => {
    // Arrange
    stubCompactScreen();
    const { user, router } = await openProject();

    // Act
    await user.click(screen.getByRole('button', { name: '專案的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '規劃' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY/planning');
  });

  it('窄螢幕：選「已刪除的項目」導到已刪除的項目頁', async () => {
    // Arrange
    stubCompactScreen();
    const { user, router } = await openProject();

    // Act
    await user.click(screen.getByRole('button', { name: '專案的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '已刪除的項目' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY/deleted');
  });

  it('窄螢幕：沒有 item.delete 的人，選單裡沒有「已刪除的項目」', async () => {
    // Arrange
    stubCompactScreen();
    const { user } = await openProject([agyDetail({ myPermissions: ['item.read', 'blueprint.read'] })]);

    // Act
    await user.click(screen.getByRole('button', { name: '專案的動作' }));

    // Assert
    const menu = await screen.findByRole('menu', { name: '專案的動作' });
    expect(within(menu).queryByRole('menuitem', { name: '已刪除的項目' })).not.toBeInTheDocument();
  });

  it('窄螢幕：選「設定」導到設定頁', async () => {
    // Arrange
    stubCompactScreen();
    const { user, router } = await openProject();

    // Act
    await user.click(screen.getByRole('button', { name: '專案的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '設定' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY/settings');
  });

  it('窄螢幕：選「成員」打開成員的對話框，關上後焦點回到「專案的動作」', async () => {
    // Arrange
    stubCompactScreen();
    const { user } = await openProject();
    const trigger = screen.getByRole('button', { name: '專案的動作' });

    // Act
    await user.click(trigger);
    await user.click(await screen.findByRole('menuitem', { name: '成員' }));
    const dialog = await screen.findByRole('dialog', { name: /成員/ });
    await user.keyboard('{Escape}');

    // Assert
    expect(dialog).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('窄螢幕：看不到藍圖的人，選單裡沒有「設定」', async () => {
    // Arrange
    stubCompactScreen();
    const detail = agyDetail({ myPermissions: ['item.read'] });
    const { user } = await openProject([detail]);

    // Act
    await user.click(screen.getByRole('button', { name: '專案的動作' }));

    // Assert
    const menu = await screen.findByRole('menu', { name: '專案的動作' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['規劃', '成員']);
  });

  it('寬螢幕：我的角色是標題旁的標籤，畫面上只有角色名稱，報讀名稱是「我的角色：…」', async () => {
    // Arrange / Act
    await openProject();

    // Assert
    const main = screen.getByRole('main');
    const readerText = within(main).getByText(/^我的角色：/);
    expect(readerText.nextElementSibling).toHaveAttribute('aria-hidden', 'true');
    expect(readerText.nextElementSibling).toHaveTextContent(/^[^：]+$/);
    expect(screen.getByRole('heading', { level: 1 })).not.toContainElement(readerText);
  });

  it('寬螢幕：頁面入口是無外框的 ghost 按鈕，「更多」前面有一條細直線', async () => {
    // Arrange / Act
    await openProject();

    // Assert
    const info = screen.getByRole('link', { name: '資訊' });
    expect(info.className).toMatch(/ghost/);
    expect(screen.getByRole('button', { name: '成員' }).className).toMatch(/ghost/);
    expect(document.querySelector('header span[aria-hidden="true"]:empty')).not.toBeNull();
  });

  it('平板：「資訊」是圖示按鈕（名稱帶代號），其餘收進「專案的動作」，頁列上沒有「專案的更多動作」', async () => {
    // Arrange
    stubTabletScreen();
    const { user } = await openProject();

    // Act
    const main = screen.getByRole('main');
    await user.click(within(main).getByRole('button', { name: '專案的動作' }));

    // Assert
    expect(within(main).getByRole('link', { name: 'AGY 的資訊' })).toHaveAttribute('href', '/projects/AGY/info');
    expect(within(main).queryByRole('link', { name: '設定' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('button', { name: '成員' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('button', { name: '專案的更多動作' })).not.toBeInTheDocument();
    const menu = await screen.findByRole('menu', { name: '專案的動作' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      '設定',
      '規劃',
      '成員',
      '匯入項目…',
      '已刪除的項目',
      '重新命名…',
      '封存專案',
    ]);
  });

  it('窄螢幕：「資訊」也是圖示按鈕', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    await openProject();

    // Assert
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'AGY 的資訊' })).toHaveAttribute('href', '/projects/AGY/info');
  });
});
