import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import type { KnowledgeSpace } from '../../api/knowledgeTypes';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { knowledgeSpaceOf, serveKnowledge } from '../../test/knowledge';
import { renderApp } from '../../test/renderApp';

const agySpace = knowledgeSpaceOf({
  id: 'space-agy',
  key: 'AGY',
  name: '接案文件',
  project: { key: 'AGY', name: '接案測試', isArchived: false },
});

afterEach(() => {
  vi.unstubAllGlobals();
});

/** 要先登入再放知識庫的假後端：signedInAs 預設的空間清單是空的，後放的才優先。 */
async function openProject(spaces: readonly KnowledgeSpace[]) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveKnowledge({ spaces });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp('/projects/AGY');
  await screen.findByRole('heading', { level: 1 });
  return view;
}

describe('專案頁頁列的「知識庫」', () => {
  it('專案有空間時，頁列有連到那個空間的「知識庫」', async () => {
    // Arrange
    // Act
    await openProject([knowledgeSpaceOf(), agySpace]);

    // Assert
    const link = await within(screen.getByRole('main')).findByRole('link', { name: '知識庫' });
    expect(link).toHaveAttribute('href', '/knowledge/AGY');
  });

  it('專案沒有空間時不顯示（別的專案的空間、組織空間都不算）', async () => {
    // Arrange
    const spaces = [knowledgeSpaceOf(), knowledgeSpaceOf({ id: 'space-web', key: 'WEB', project: { key: 'WEB', name: '官網', isArchived: false } })];

    // Act
    await openProject(spaces);

    // Assert
    await within(screen.getByRole('main')).findByRole('link', { name: '資訊' });
    expect(within(screen.getByRole('main')).queryByRole('link', { name: '知識庫' })).not.toBeInTheDocument();
  });

  it('窄螢幕：收在「專案的動作」裡，選了進到空間', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    const { user, router } = await openProject([agySpace]);

    // Act
    await user.click(await screen.findByRole('button', { name: '專案的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '知識庫' }));

    // Assert
    expect(router.state.location.pathname).toBe('/knowledge/AGY');
  });
});

describe('側欄的「知識庫」', () => {
  it('在「專案」上面，連到空間清單；在知識庫底下的頁面是目前所在', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [knowledgeSpaceOf()] });

    // Act
    renderApp('/knowledge/DOCS');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    const links = within(nav).getAllByRole('link').map((link) => link.textContent);
    const knowledge = within(nav).getByRole('link', { name: '知識庫' });
    expect(knowledge).toHaveAttribute('href', '/knowledge');
    expect(knowledge).toHaveAttribute('aria-current', 'page');
    expect(links.indexOf('知識庫')).toBeLessThan(links.indexOf('專案'));
    expect(within(nav).getByRole('link', { name: '專案' })).not.toHaveAttribute('aria-current');
  });
});
