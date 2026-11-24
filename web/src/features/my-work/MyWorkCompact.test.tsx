import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatDate } from '../../domain/dates';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { appProject, daysFromToday, exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

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

describe('窄螢幕的我的工作', () => {
  it('是清單：一筆兩行，第二行有狀態、專案與到期日；照到期日排，連到項目頁', async () => {
    // Arrange
    stubCompactScreen();
    const overdue = makeItem({ number: 3, title: '首頁改版需求', dueDate: daysFromToday(-2) });
    const later = makeItem({ projectKey: 'APP', number: 2, title: '登入流程', dueDate: daysFromToday(3) });
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [later, overdue] });

    // Act
    renderApp('/my-work');

    // Assert
    const list = await screen.findByRole('list', { name: '已過期' });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const links = screen.getAllByRole('link', { name: /^[A-Z]+-\d+ / });
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/projects/WEB/items/WEB-3', '/projects/APP/items/APP-2']);
    expect(links[0]).toHaveAccessibleName(/^WEB-3 首頁改版需求/);
    expect(await within(list).findByText('官網改版')).toBeInTheDocument();
    expect(links[0]).toHaveTextContent('待辦');
    expect(links[0]).toHaveTextContent(formatDate(overdue.dueDate ?? ''));
    expect(links[0]).toHaveTextContent('已過期');
  });
});
