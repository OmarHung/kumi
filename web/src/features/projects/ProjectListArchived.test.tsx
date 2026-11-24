import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { appProject, exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

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

describe('專案清單：已封存的專案', () => {
  it('沒封存的專案在上面，封存的在「已封存的專案」下面，多一欄封存的日期', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, archivedApp] });

    // Act
    renderApp('/projects');

    // Assert
    const active = await screen.findByRole('table', { name: '專案' });
    expect(within(active).getByRole('link', { name: '官網改版' })).toBeInTheDocument();
    expect(within(active).queryByRole('link', { name: '會員 App' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: '已封存的專案' })).toBeInTheDocument();
    const archived = screen.getByRole('table', { name: '已封存的專案' });
    expect(within(archived).getByRole('link', { name: '會員 App' })).toHaveAttribute('href', '/projects/APP');
    expect(within(archived).getByRole('columnheader', { name: '封存於' })).toBeInTheDocument();
    expect(within(archived).getByRole('row', { name: /APP/ })).toHaveTextContent(/2026\/11\/18/);
  });

  it('沒有封存的專案時沒有「已封存的專案」', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/projects');

    // Assert
    await screen.findByRole('table', { name: '專案' });
    expect(screen.queryByRole('heading', { name: '已封存的專案' })).not.toBeInTheDocument();
  });

  it('專案都封存了：只有「已封存的專案」，第一筆是內容的開頭', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [archivedApp] });

    // Act
    renderApp('/projects');

    // Assert
    const archived = await screen.findByRole('table', { name: '已封存的專案' });
    expect(screen.queryByRole('table', { name: '專案' })).not.toBeInTheDocument();
    expect(within(archived).getByRole('row', { name: /APP/ })).toHaveAttribute('data-content-start');
  });

  it('窄螢幕：照清單的寫法，封存的日期在第二行', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject, archivedApp] });

    // Act
    renderApp('/projects');

    // Assert
    const main = await screen.findByRole('main');
    expect(within(main).queryByRole('table')).not.toBeInTheDocument();
    const list = await within(main).findByRole('list', { name: '已封存的專案' });
    expect(within(list).getByRole('link', { name: /APP 會員 App/ })).toHaveTextContent('封存於：2026/11/18');
    expect(within(main).getByRole('list', { name: '專案' })).not.toHaveTextContent('會員 App');
  });

  it('側欄不列封存的專案，專案清單頁與快速跳轉才找得到', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, archivedApp] });

    // Act
    renderApp('/my-work');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(await within(nav).findByRole('link', { name: /官網改版/ })).toBeInTheDocument();
    expect(within(nav).queryByRole('link', { name: /會員 App/ })).not.toBeInTheDocument();
  });

  it('專案都封存了：側欄寫「沒有進行中的專案」', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [archivedApp] });

    // Act
    renderApp('/my-work');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(await within(nav).findByText('沒有進行中的專案')).toBeInTheDocument();
  });
});
