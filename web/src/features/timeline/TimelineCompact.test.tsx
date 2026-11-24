import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { currentPath } from '../../test/renderApp';
import { renderTimeline } from '../../test/timeline';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('窄螢幕的時間軸', () => {
  it('不畫時間軸，換成依開始日排的清單，每一筆寫日期範圍，並說明要到寬螢幕看', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner);
    serveItems('SMK', [
      smkItem({ number: 2, title: '後面的', dueDate: '2026-11-25' }),
      smkItem({ number: 1, title: '前面的', startDate: '2026-11-12', dueDate: '2026-11-20' }),
    ]);

    // Act
    renderTimeline();

    // Assert
    const list = await screen.findByRole('list', { name: '項目' });
    expect(await within(list).findAllByRole('link')).toHaveLength(2);
    const [first, second] = within(list).getAllByRole('link');
    expect(first).toHaveTextContent('SMK-1');
    expect(first).toHaveTextContent('11/12～11/20');
    expect(second).toHaveTextContent('11/25');
    expect(first).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
    expect(screen.getByText(/時間軸在寬螢幕上看/)).toBeInTheDocument();
  });

  it('清單上方有範圍與「往前、今天、往後」，換得了時間範圍（沒有刻度）', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner);
    serveItems('SMK', [smkItem({ number: 1, title: '前面的', startDate: '2026-11-12', dueDate: '2026-11-20' })]);
    const { user, router } = renderTimeline();
    await screen.findByRole('link', { name: /SMK-1/ });

    expect(screen.getByText('11/09～12/20')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: /往後/ }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK?view=timeline&from=2026-12-07');
    expect(screen.getByRole('link', { name: '今天' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: '刻度' })).not.toBeInTheDocument();
    expect(await screen.findByText('2026/12/07～2027/01/17')).toBeInTheDocument();
  });

  it('點項目進整頁，不開面板', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner);
    serveItems('SMK', [smkItem({ number: 1, dueDate: '2026-11-20' })]);
    const { router } = renderTimeline();
    const link = await screen.findByRole('link', { name: /SMK-1/ });

    // Assert
    expect(link).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
    expect(currentPath(router)).toBe('/projects/SMK?view=timeline');
  });
});
