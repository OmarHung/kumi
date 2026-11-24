import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderInRouter } from '../../test/renderInRouter';
import { IconLink } from './IconButton';

describe('IconLink', () => {
  it('長得和 IconButton 一樣的連結：label 是名稱與滑過時的提示，圖示不唸', () => {
    // Act
    renderInRouter(<IconLink to="/projects/AGY/items/AGY-3" icon="expand" label="展開成整頁" />);

    // Assert
    const link = screen.getByRole('link', { name: '展開成整頁' });
    expect(link).toHaveAttribute('href', '/projects/AGY/items/AGY-3');
    expect(link).toHaveAttribute('title', '展開成整頁');
    expect(link).toHaveAttribute('data-size', 'md');
    expect(link).toHaveTextContent(/^$/);
  });

  it('點了照連結換頁，帶著 state', async () => {
    // Arrange
    const state = { returnTo: '/projects/AGY?view=board' };
    const { user, router } = renderInRouter(
      <IconLink to="/projects/AGY/items/AGY-3" state={state} icon="expand" label="展開成整頁" size="sm" />,
    );

    // Act
    await user.click(screen.getByRole('link', { name: '展開成整頁' }));

    // Assert
    expect(router.state.location.pathname).toBe('/projects/AGY/items/AGY-3');
    expect(router.state.location.state).toEqual(state);
  });
});
