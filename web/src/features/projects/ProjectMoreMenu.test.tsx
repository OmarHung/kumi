import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { archivedDetail } from '../../test/projects';
import { currentPath, renderApp } from '../../test/renderApp';

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

async function openProject(detail: ProjectDetail) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp('/projects/AGY');
  await screen.findByRole('heading', { level: 1 });
  return view;
}

async function openMenu(user: Awaited<ReturnType<typeof openProject>>['user'], trigger: string, menu: string) {
  await user.click(screen.getByRole('button', { name: trigger }));
  return screen.findByRole('menu', { name: menu });
}

function labels(menu: HTMLElement): (string | null)[] {
  return within(menu).getAllByRole('menuitem').map((item) => item.textContent);
}

describe('專案頁的「更多」（寬螢幕）', () => {
  it('全部權限：匯入項目、已刪除的項目，分隔線，重新命名、封存專案', async () => {
    // Arrange
    const { user } = await openProject(agyDetail());

    // Act
    const menu = await openMenu(user, '專案的更多動作', '專案的更多動作');

    // Assert
    expect(labels(menu)).toEqual(['匯入項目…', '已刪除的項目', '重新命名…', '封存專案']);
    expect(within(menu).getAllByRole('separator')).toHaveLength(1);
  });

  it('封存的專案：只剩重新命名與取消封存（沒有 item.create 與 item.delete，也就沒有匯入與已刪除）', async () => {
    // Arrange
    const { user } = await openProject(archivedDetail(agyDetail()));

    // Act
    const menu = await openMenu(user, '專案的更多動作', '專案的更多動作');

    // Assert
    expect(labels(menu)).toEqual(['重新命名…', '取消封存']);
    expect(within(menu).queryByRole('separator')).not.toBeInTheDocument();
  });

  it('只有 project.manage：沒有分隔線在最前面，也沒有匯入與已刪除', async () => {
    // Arrange
    const { user } = await openProject(agyDetail({ myPermissions: ['item.read', 'project.manage'] }));

    // Act
    const menu = await openMenu(user, '專案的更多動作', '專案的更多動作');

    // Assert
    expect(labels(menu)).toEqual(['重新命名…', '封存專案']);
    expect(within(menu).queryByRole('separator')).not.toBeInTheDocument();
  });

  it('沒有 project.manage：只有匯入與已刪除，沒有改名與封存', async () => {
    // Arrange
    const { user } = await openProject(agyDetail({ myPermissions: ['item.read', 'item.create', 'item.delete'] }));

    // Act
    const menu = await openMenu(user, '專案的更多動作', '專案的更多動作');

    // Assert
    expect(labels(menu)).toEqual(['匯入項目…', '已刪除的項目']);
  });

  it('選單是空的（只能看的人）就沒有「更多」按鈕', async () => {
    // Arrange & Act
    await openProject(agyDetail({ myPermissions: ['item.read'] }));

    // Assert
    expect(screen.queryByRole('button', { name: '專案的更多動作' })).not.toBeInTheDocument();
  });

  it('選「匯入項目…」到匯入頁，選「已刪除的項目」到已刪除的項目頁', async () => {
    // Arrange
    const { user, router } = await openProject(agyDetail());
    await user.click(screen.getByRole('button', { name: '專案的更多動作' }));

    // Act
    await user.click(await screen.findByRole('menuitem', { name: '已刪除的項目' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY/deleted');
  });
});

describe('專案頁的「專案的動作」（窄螢幕）', () => {
  it('依序是設定、規劃、成員，匯入、已刪除的項目，重新命名、封存專案，三組之間有分隔線', async () => {
    // Arrange
    stubCompactScreen();
    const { user } = await openProject(agyDetail());

    // Act
    const menu = await openMenu(user, '專案的動作', '專案的動作');

    // Assert
    expect(labels(menu)).toEqual(['設定', '規劃', '成員', '匯入項目…', '已刪除的項目', '重新命名…', '封存專案']);
    expect(within(menu).getAllByRole('separator')).toHaveLength(2);
  });

  it('封存的專案：設定、規劃、成員，重新命名、取消封存', async () => {
    // Arrange
    stubCompactScreen();
    const { user } = await openProject(archivedDetail(agyDetail()));

    // Act
    const menu = await openMenu(user, '專案的動作', '專案的動作');

    // Assert
    expect(labels(menu)).toEqual(['設定', '規劃', '成員', '重新命名…', '取消封存']);
    expect(within(menu).getAllByRole('separator')).toHaveLength(1);
  });

  it('沒有更多動作時仍然有設定、規劃、成員，沒有分隔線', async () => {
    // Arrange
    stubCompactScreen();
    const { user } = await openProject(agyDetail({ myPermissions: ['item.read', 'blueprint.read'] }));

    // Act
    const menu = await openMenu(user, '專案的動作', '專案的動作');

    // Assert
    expect(labels(menu)).toEqual(['設定', '規劃', '成員']);
    expect(within(menu).queryByRole('separator')).not.toBeInTheDocument();
  });
});
