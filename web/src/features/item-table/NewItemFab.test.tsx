import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

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

describe('窄螢幕右下角的「新增項目」', () => {
  it('表格、看板、行事曆、時間軸都有一顆；工具列裡沒有新增，按了打開對話框', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK');
    await screen.findByRole('list', { name: '項目' });

    // Act
    const buttons = screen.getAllByRole('button', { name: '新增項目' });

    // Assert
    expect(buttons).toHaveLength(1);
    expect(buttons[0]?.closest('[data-scrollable]')).toBeNull();
    expect(buttons[0]?.className).toMatch(/fab/);

    // Act
    await user.click(buttons[0] as HTMLElement);

    // Assert
    expect(screen.getByRole('dialog', { name: '新增項目' })).toBeInTheDocument();
  });

  it.each(['board', 'calendar', 'timeline'])('%s 檢視也有同一顆按鈕', async (view) => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp(`/projects/SMK?view=${view}`);

    // Assert
    expect(await screen.findByRole('button', { name: '新增項目' })).toBeInTheDocument();
  });

  it.each(['board', 'calendar', 'timeline'])('%s 檢視新增之後重新讀項目，新的項目出現在畫面上；不帶表格看不到的條件', async (view) => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveItems('SMK', [smkItem({ number: 1 })]);
    const bodies: unknown[] = [];
    server.use(
      http.post('/v1/projects/SMK/items', async ({ request }) => {
        bodies.push(await request.json());
        return ok(smkItem({ number: 2, title: '新的項目' }), 201);
      }),
    );
    const { user } = renderApp(`/projects/SMK?view=${view}&assignee=me`);
    await user.click(await screen.findByRole('button', { name: '新增項目' }));
    const dialog = screen.getByRole('dialog', { name: '新增項目' });
    await waitFor(() => {
      expect(requests.length).toBeGreaterThan(0);
    });
    const before = requests.length;

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '標題' }), '新的項目');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已新增 SMK-2');
    await waitFor(() => {
      expect(requests.length).toBeGreaterThan(before);
    });
    expect(bodies[0]).not.toHaveProperty('assigneeId', exampleOwner.personId);
  });

  it('沒有建立權限時沒有按鈕', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    await screen.findByRole('list', { name: '項目' });
    expect(screen.queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
  });

  it('寬螢幕沒有圓形按鈕，照舊是表格上方的快速新增', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    await screen.findByRole('table', { name: '項目' });
    expect(screen.queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
    expect(screen.getByRole('form', { name: '新增項目' })).toBeInTheDocument();
  });
});
