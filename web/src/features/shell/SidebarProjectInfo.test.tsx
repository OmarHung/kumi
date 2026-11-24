import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { appProject, exampleOwner, smkDetail, smkProject, webProject } from '../../test/fixtures';
import { describedBy, serveDescriptionTemplates, serveProjectDescription } from '../../test/projectDescription';
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

async function sidebarNav() {
  return screen.findByRole('navigation', { name: '主要導覽' });
}

describe('側欄：專案的資訊連結', () => {
  it('每個專案有「<代號> 的資訊」連結，title 是「資訊」，和專案連結是兄弟（不在連結裡）', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject] });

    // Act
    renderApp('/my-work');

    // Assert
    const nav = await sidebarNav();
    const web = await within(nav).findByRole('link', { name: 'WEB 的資訊' });
    expect(web).toHaveAttribute('href', '/projects/WEB/info');
    expect(web).toHaveAttribute('title', '資訊');
    expect(web.closest('li')).toBe(within(nav).getByRole('link', { name: /^WEB\s*官網改版$/ }).closest('li'));
    expect(web.parentElement?.closest('a')).toBeNull();
    expect(within(nav).getByRole('link', { name: 'APP 的資訊' })).toHaveAttribute('href', '/projects/APP/info');
  });

  it('點了到資訊頁，側欄的那個專案仍是目前所在', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [smkProject], details: [smkDetail()] });
    serveDescriptionTemplates();
    serveProjectDescription('SMK', describedBy('內容'));
    const { user, router } = renderApp('/my-work');
    const nav = await sidebarNav();

    // Act
    await user.click(await within(nav).findByRole('link', { name: 'SMK 的資訊' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/info');
    });
    expect(within(nav).getByRole('link', { name: new RegExp(`^SMK\\s*${smkProject.name}$`) })).toHaveAttribute('aria-current', 'page');
  });

  it('窄螢幕的抽屜：點了圖示和點其他連結一樣關掉抽屜', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [smkProject], details: [smkDetail()] });
    serveDescriptionTemplates();
    serveProjectDescription('SMK', describedBy('內容'));
    const { user } = renderApp('/my-work');
    const menuButton = await screen.findByRole('button', { name: '選單' });
    await user.click(menuButton);
    expect(menuButton).toHaveAttribute('aria-expanded', 'true');

    // Act
    await user.click(await screen.findByRole('link', { name: 'SMK 的資訊' }));

    // Assert
    await waitFor(() => {
      expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    });
  });
});
