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

function stubCompact() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

describe('窄螢幕的時間軸', () => {
  it('不畫時間軸，換成依開始日排的清單，每一筆寫日期範圍', async () => {
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
    expect(screen.queryByText(/時間軸在寬螢幕上看/)).not.toBeInTheDocument();
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

  it('每一筆有一條軌道，位置是第幾天與幾天，過期的標「已過期」；軌道、刻度、今天只是裝飾', async () => {
    // Arrange：範圍 11/09～12/20（42 天），今天 11/18
    stubCompact();
    signedInAs(exampleOwner);
    serveItems('SMK', [
      smkItem({ number: 1, title: '範圍內', startDate: '2026-11-16', dueDate: '2026-11-22' }),
      smkItem({ number: 2, title: '早就開始', startDate: '2026-10-20', dueDate: '2026-11-12' }),
    ]);

    // Act
    const { container } = renderTimeline();
    const list = await screen.findByRole('list', { name: '項目' });
    const [early, inRange] = await within(list).findAllByRole('link');

    // Assert
    const [flat, bar] = Array.from(container.querySelectorAll<HTMLElement>('[data-category]'));
    expect(bar?.style.getPropertyValue('--col')).toBe('7');
    expect(bar?.style.getPropertyValue('--span')).toBe('7');
    expect(flat).toHaveAttribute('data-flat-start');
    expect(flat).not.toHaveAttribute('data-flat-end');
    expect(inRange).not.toHaveTextContent('已過期');
    expect(early).toHaveTextContent('已過期');
    expect(bar?.closest('[aria-hidden="true"]')).not.toBeNull();
  });

  it('今天在範圍內時畫刻度、今天的膠囊與線，不在範圍內不畫', async () => {
    // Arrange
    stubCompact();
    signedInAs(exampleOwner);
    serveItems('SMK', [smkItem({ number: 1, title: '項目', startDate: '2026-11-12', dueDate: '2026-11-20' })]);
    const { user } = renderTimeline();
    await screen.findByRole('link', { name: /SMK-1/ });

    // Assert
    const pill = screen.getByText('今天', { selector: 'div' });
    expect(pill.closest('[aria-hidden="true"]')).not.toBeNull();
    expect(screen.getByText('11/23')).toBeInTheDocument();

    // Act：往後兩次，範圍不再包含今天
    await user.click(screen.getByRole('link', { name: /往後/ }));
    await user.click(screen.getByRole('link', { name: /往後/ }));

    // Assert
    expect(screen.queryByText('今天', { selector: 'div' })).not.toBeInTheDocument();
  });
});

describe('窄螢幕的時間軸一律是週的刻度', () => {
  it('網址帶著寬螢幕選的「月」，手機上仍是 6 週的範圍（沒有切回週的控制項，月的刻度在手機上疊在一起）', async () => {
    // Arrange
    stubCompact();
    signedInAs(exampleOwner);
    serveItems('SMK', []);

    // Act
    renderTimeline({ path: '/projects/SMK?view=timeline&scale=month&from=2026-11-09' });

    // Assert
    expect(await screen.findByText('11/09～12/20')).toBeInTheDocument();
  });
});
