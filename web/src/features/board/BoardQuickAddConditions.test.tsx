import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { Item, NewItemRequest } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { exampleOwner, ownerAsAdmin, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

function serveCreate(base: Item): NewItemRequest[] {
  const bodies: NewItemRequest[] = [];
  server.use(
    http.post('/v1/projects/SMK/items', async ({ request }) => {
      const body = (await request.json()) as NewItemRequest;
      bodies.push(body);
      const assignee = body.assigneeId === ownerAsAdmin.personId ? { personId: ownerAsAdmin.personId, displayName: ownerAsAdmin.displayName } : null;
      return ok({ ...base, title: body.title, assignee, description: null }, 201);
    }),
  );
  return bodies;
}

async function openQuickAdd(path: string) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItemsByQuery('SMK', []);
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  const todo = within(board).getByRole('region', { name: '待辦' });
  await view.user.click(within(todo).getByRole('button', { name: '新增項目' }));
  const form = within(todo).getByRole('form', { name: '新增項目' });
  return { ...view, todo, form };
}

describe('看板的快速新增沿用條件', () => {
  it('負責人篩「我」：新的項目負責人是我，出現在第一欄', async () => {
    // Arrange
    const { user, todo, form } = await openQuickAdd('/projects/SMK?view=board&assignee=me');
    const bodies = serveCreate(smkItem({ number: 2 }));

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Assert
    expect(await within(todo).findByRole('article', { name: '新的任務' })).toBeInTheDocument();
    expect(bodies).toEqual([{ type: 'task', title: '新的任務', assigneeId: exampleOwner.personId }]);
    expect(within(form).getByRole('status')).toHaveTextContent(/^已新增 SMK-2。$/);
    expect(within(form).getByRole('link', { name: '開啟 SMK-2' })).toHaveAttribute('href', '/projects/SMK/items/SMK-2');
  });

  it('不符合搜尋時訊息補一句', async () => {
    // Arrange
    const { user, form } = await openQuickAdd('/projects/SMK?view=board&q=首頁');
    serveCreate(smkItem({ number: 2 }));

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Assert
    expect(await within(form).findByRole('status')).toHaveTextContent('已新增 SMK-2。它不符合目前的條件，清除條件之後可以看到。');
  });

  it('篩了優先度與「今天到期」：新的項目帶那個優先度與今天的日期', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 10, 18, 9));
    const { user, form } = await openQuickAdd('/projects/SMK?view=board&priority=urgent&due=today');
    const bodies = serveCreate(smkItem({ number: 2, priority: 'urgent', dueDate: '2026-11-18' }));

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Assert
    await within(form).findByRole('status');
    expect(bodies).toEqual([{ type: 'task', title: '新的任務', priority: 'urgent', dueDate: '2026-11-18' }]);
    vi.useRealTimers();
  });
});
