import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ITEMS = [
  smkItem({ number: 1, title: '修登入', fields: { category: 'bug', blocked: true } }),
  smkItem({ number: 2, title: '做報表', fields: { category: 'feature' } }),
];

const VIEWS = [
  { view: 'board', group: '篩選看板', ready: { role: 'region', name: '看板' } },
  { view: 'calendar', group: '篩選行事曆', ready: { role: 'region', name: '行事曆' } },
  { view: 'timeline', group: '篩選時間軸', ready: { role: 'region', name: '時間軸' } },
] as const;

async function open(view: string, search = '') {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const server = serveItemsByQuery('SMK', ITEMS);
  const rendered = renderApp(`/projects/SMK?view=${view}${search}`);
  await screen.findByRole('group', { name: VIEWS.find((candidate) => candidate.view === view)?.group });
  return { ...rendered, server };
}

async function openMore(user: ReturnType<typeof renderApp>['user'], group: string): Promise<HTMLElement> {
  await user.click(await within(await screen.findByRole('group', { name: group })).findByRole('button', { name: '更多條件' }));
  return screen.findByRole('dialog', { name: '更多條件' });
}

describe.each(VIEWS)('$view 依自訂欄位篩選', ({ view, group }) => {
  it('「更多條件」裡有欄位的下拉；選了就篩：網址與請求都帶 field.<key>，數字加一', async () => {
    // Arrange
    const { user, router, server } = await open(view);
    const more = await openMore(user, group);

    // Act
    await chooseOption(user, within(more).getByRole('combobox', { name: '分類' }), '錯誤');

    // Assert
    await waitFor(() => { expect(server.requests.at(-1)?.searchParams.getAll('field.category')).toEqual(['bug']); });
    expect(new URLSearchParams(router.state.location.search).get('field.category')).toBe('bug');
    expect(new URLSearchParams(router.state.location.search).get('view')).toBe(view);
    expect(within(screen.getByRole('group', { name: group })).getByRole('button', { name: '更多條件' })).toHaveTextContent('1');
  });

  it('「清除條件」把欄位條件一起清掉，版面留著', async () => {
    // Arrange
    const { user, router } = await open(view, '&field.blocked=true');

    // Act
    await user.click(await within(screen.getByRole('group', { name: group })).findByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => { expect(new URLSearchParams(router.state.location.search).has('field.blocked')).toBe(false); });
    expect(new URLSearchParams(router.state.location.search).get('view')).toBe(view);
  });

  it('網址上已經不在藍圖裡的欄位，送出前拿掉', async () => {
    // Arrange / Act
    const { server } = await open(view, '&field.gone=x&field.category=bug');

    // Assert
    await waitFor(() => { expect(server.requests.length).toBeGreaterThan(0); });
    const params = server.requests.at(-1)?.searchParams;
    expect(params?.has('field.gone')).toBe(false);
    expect(params?.get('field.category')).toBe('bug');
  });
});

describe('看板在窄螢幕', () => {
  it('欄位條件在「篩選」浮層裡，數字算進去', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === COMPACT_QUERY, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const server = serveItemsByQuery('SMK', ITEMS);
    const { user } = renderApp('/projects/SMK?view=board');

    // Act
    await user.click(await screen.findByRole('button', { name: '篩選' }));
    const dialog = await screen.findByRole('dialog', { name: '篩選' });
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '卡住了' }), '已勾選');

    // Assert
    await waitFor(() => { expect(server.requests.at(-1)?.searchParams.get('field.blocked')).toBe('true'); });
    expect(screen.getByRole('button', { name: '篩選' })).toHaveTextContent('1');
  });
});
