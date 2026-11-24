import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { appProject, exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

/** 窄螢幕：只有窄螢幕的查詢符合。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

describe('專案清單：窄螢幕', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('專案是清單而不是表格：每一筆連到專案頁，說出我的角色；第一筆是內容的開頭', async () => {
    // Arrange
    stubCompactScreen();
    const unjoined = { ...appProject, key: 'OPS', name: '維運', myRole: null };
    signedInAs(exampleOwner, { projects: [webProject, unjoined] });

    // Act
    renderApp('/projects');

    // Assert
    const main = await screen.findByRole('main');
    const list = await within(main).findByRole('list', { name: '專案' });
    expect(within(main).queryByRole('table')).not.toBeInTheDocument();
    const [web, ops] = within(list).getAllByRole('listitem');
    // 整筆是一個連結（components/List）：名稱是代號、名稱，再接我的角色。
    expect(within(web as HTMLElement).getByRole('link', { name: 'WEB 官網改版 我的角色：管理員' })).toHaveAttribute('href', '/projects/WEB');
    expect(web).toHaveTextContent('我的角色：管理員');
    expect(ops).toHaveTextContent('我的角色：未加入');
    expect(web).toHaveAttribute('data-content-start');
  });
});
