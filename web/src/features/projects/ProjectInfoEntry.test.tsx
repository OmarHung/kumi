import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { appProject, exampleOwner, smkDetail, smkProject, webProject } from '../../test/fixtures';
import { describedBy, serveDescriptionTemplates, serveProjectDescription } from '../../test/projectDescription';
import { serveProjectSearch } from '../../test/projectSearch';
import { currentPath, renderApp } from '../../test/renderApp';

const SLOW = { timeout: 3000 } as const;
const archivedApp = { ...appProject, archivedAt: '2026-11-18T08:00:00Z' };

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

describe('專案清單：直接開資訊頁', () => {
  it('進行中的專案每一列最後有「<名稱> 的資訊」連結，欄標題只給報讀軟體', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject] });

    // Act
    renderApp('/projects');

    // Assert
    const table = await screen.findByRole('table', { name: '專案' });
    expect(within(table).getByRole('columnheader', { name: '動作' })).toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '官網改版 的資訊' })).toHaveAttribute('href', '/projects/WEB/info');
    expect(within(table).getByRole('link', { name: '會員 App 的資訊' })).toHaveAttribute('href', '/projects/APP/info');
  });

  it('已封存的專案那一組也有；封存的專案資訊頁還是進得去', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, archivedApp] });

    // Act
    renderApp('/projects');

    // Assert
    const archived = await screen.findByRole('table', { name: '已封存的專案' });
    expect(within(archived).getByRole('link', { name: '會員 App 的資訊' })).toHaveAttribute('href', '/projects/APP/info');
  });

  it('搜尋結果的每一列也有', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, archivedApp] });
    serveProjectSearch([{ project: archivedApp, snippet: '主機：web01.example.com' }]);

    // Act
    renderApp('/projects?q=web01');

    // Assert
    const results = await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    expect(within(results).getByRole('link', { name: '會員 App 的資訊' })).toHaveAttribute('href', '/projects/APP/info');
  });

  it('搜尋結果同時有「說明裡的」「封存於」時，「動作」仍是最後一欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, archivedApp] });
    serveProjectSearch([{ project: archivedApp, snippet: '主機：web01.example.com' }]);

    // Act
    renderApp('/projects?q=web01');

    // Assert
    const results = await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    const headers = within(results).getAllByRole('columnheader');
    expect(headers.map((header) => header.textContent)).toEqual(['代號', '名稱', '我的角色', '說明裡的', '封存於', '動作']);
  });

  it('點了就到資訊頁，一步', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [smkProject], details: [smkDetail()] });
    serveDescriptionTemplates();
    serveProjectDescription('SMK', describedBy('內容'));
    const { user, router } = renderApp('/projects');

    // Act
    await user.click(await screen.findByRole('link', { name: `${smkProject.name} 的資訊` }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/info');
    });
  });

  it('窄螢幕的清單整筆是連結，不放這顆按鈕', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/projects');

    // Assert
    const main = await screen.findByRole('main');
    await within(main).findByRole('list', { name: '專案' });
    expect(within(main).queryByRole('link', { name: /的資訊/ })).not.toBeInTheDocument();
  });
});
