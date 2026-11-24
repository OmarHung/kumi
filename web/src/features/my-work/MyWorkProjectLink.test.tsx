import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { appProject, daysFromToday, exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

const overdueWeb = makeItem({ number: 3, title: '首頁改版需求', dueDate: daysFromToday(-2) });
const overdueApp = makeItem({ projectKey: 'APP', number: 7, title: '登入流程', dueDate: daysFromToday(-2) });

afterEach(() => {
  localStorage.clear();
});

describe('我的工作：點專案名稱到那個專案（docs/DESIGN.md 8.3「我的工作」）', () => {
  it('表格的專案名稱是連到專案頁的連結，照那個專案上次用的檢視開；點了就到專案頁', async () => {
    // Arrange
    localStorage.setItem('project.view.WEB', 'board');
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [overdueWeb, overdueApp] });
    const { user, router } = renderApp('/my-work');
    const table = await screen.findByRole('table', { name: '已過期' });
    const link = await within(table).findByRole('link', { name: '官網改版' });

    // Act
    await user.click(link);

    // Assert
    expect(within(table).getByRole('link', { name: '會員 App' })).toHaveAttribute('href', '/projects/APP');
    expect(currentPath(router)).toBe('/projects/WEB?view=board');
  });

  it('依專案分組時，組名是連到那個專案的連結', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [overdueWeb, overdueApp] });

    // Act
    renderApp('/my-work?group=project');

    // Assert
    const heading = await screen.findByRole('heading', { level: 2, name: /官網改版/ });
    expect(await within(heading).findByRole('link', { name: '官網改版' })).toHaveAttribute('href', '/projects/WEB');
  });
});
