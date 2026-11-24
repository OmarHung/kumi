import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Item, NewItemRequest } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

// 2026-11-18 在「衝刺 2」（11/16～11/29）裡。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
});

const SPRINT_1 = '0190a000-0000-7000-8000-0000000000d1';
const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';

function serveCreate(base: Item): NewItemRequest[] {
  const bodies: NewItemRequest[] = [];
  server.use(
    http.post('/v1/projects/SMK/items', async ({ request }) => {
      const body = (await request.json()) as NewItemRequest;
      bodies.push(body);
      return ok({ ...base, title: body.title, cycleId: body.cycleId ?? null, milestoneId: body.milestoneId ?? null, description: null }, 201);
    }),
  );
  return bodies;
}

function servePlanning() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveCycles('SMK', [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 }), cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 })]);
  serveMilestones('SMK', [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })]);
}

async function addInTable(path: string, title: string) {
  servePlanning();
  serveItems('SMK', []);
  const bodies = serveCreate(smkItem({ number: 2 }));
  const view = renderApp(path);
  const form = await screen.findByRole('form', { name: '新增項目' });
  await view.user.type(within(form).getByRole('textbox', { name: '標題' }), `${title}{Enter}`);
  const status = await within(form).findByText(/^已新增/);
  return { bodies, status };
}

describe('表格的快速新增帶上篩的週期與里程碑', () => {
  it('篩「目前的週期」：新的項目排進今天所在的週期', async () => {
    // Arrange / Act
    const { bodies, status } = await addInTable('/projects/SMK?cycle=current', '新的任務');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '新的任務', cycleId: SPRINT_2 }]);
    expect(status).toHaveTextContent(/^已新增 SMK-2。$/);
  });

  it('篩某個週期與里程碑：兩個都帶上', async () => {
    // Arrange / Act
    const { bodies } = await addInTable(`/projects/SMK?cycle=${SPRINT_1}&milestone=${V1}`, '補作業');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '補作業', cycleId: SPRINT_1, milestoneId: V1 }]);
  });

  it('篩「沒有週期」：不帶（本來就是沒有）', async () => {
    // Arrange / Act
    const { bodies } = await addInTable('/projects/SMK?cycle=none&milestone=none', '待辦事項');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '待辦事項' }]);
  });
});

describe('看板的快速新增帶上篩的週期與里程碑', () => {
  it('篩目前的週期與里程碑：新的項目帶上，不再說它不符合條件', async () => {
    // Arrange
    servePlanning();
    serveItemsByQuery('SMK', []);
    const bodies = serveCreate(smkItem({ number: 2 }));
    const { user } = renderApp(`/projects/SMK?view=board&cycle=current&milestone=${V1}`);
    const board = await screen.findByRole('region', { name: '看板' });
    const todo = within(board).getByRole('region', { name: '待辦' });
    await user.click(within(todo).getByRole('button', { name: '新增項目' }));
    const form = within(todo).getByRole('form', { name: '新增項目' });

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Assert
    expect(await within(form).findByRole('status')).toHaveTextContent(/^已新增 SMK-2。$/);
    expect(bodies).toEqual([{ type: 'task', title: '新的任務', cycleId: SPRINT_2, milestoneId: V1 }]);
  });
});
