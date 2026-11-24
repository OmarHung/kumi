import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item, NewItemRequest } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, memberAsEditor, ownerAsAdmin, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';

const MEMBERS = [ownerAsAdmin, memberAsEditor];

/** 像後端一樣照內文建出項目：類型、標題、負責人（assigneeId）照送來的，其他用 base。 */
function serveCreate(projectKey: string, base: Item): NewItemRequest[] {
  const bodies: NewItemRequest[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items`, async ({ request }) => {
      const body = (await request.json()) as NewItemRequest;
      bodies.push(body);
      const person = MEMBERS.find((member) => member.personId === body.assigneeId);
      const assignee = person === undefined ? null : { personId: person.personId, displayName: person.displayName };
      return ok({ ...base, type: body.type, title: body.title, assignee, description: null }, 201);
    }),
  );
  return bodies;
}

async function openTable(path: string, detail = smkDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems(detail.project.key, []);
  const view = renderApp(path);
  const form = await screen.findByRole('form', { name: '新增項目' });
  return { ...view, form };
}

async function addTitled(view: Awaited<ReturnType<typeof openTable>>, title: string) {
  await view.user.type(within(view.form).getByRole('textbox', { name: '標題' }), `${title}{Enter}`);
  return within(view.form).findByText(/^已新增/);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('快速新增沿用目前的條件', () => {
  it('篩選只選一種類型時，類型預設就是它', async () => {
    // Arrange
    const view = await openTable('/projects/AGY?type=task', agyDetail());
    const bodies = serveCreate('AGY', agyItem({ number: 5, type: 'task', state: 'todo' }));

    // Act
    expect(within(view.form).getByRole('combobox', { name: '類型' })).toHaveTextContent('任務');
    await addTitled(view, '切版');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '切版' }]);
  });

  it('換了類型的篩選，快速新增的類型跟著換', async () => {
    // Arrange
    const view = await openTable('/projects/AGY?type=task', agyDetail());
    const filters = screen.getByRole('group', { name: '篩選項目' });

    // Act
    await chooseOption(view.user, within(filters).getByRole('combobox', { name: '類型' }), '需求');

    // Assert
    expect(within(view.form).getByRole('combobox', { name: '類型' })).toHaveTextContent('需求');
  });

  it('負責人篩「我」：新的項目負責人是我，留在清單裡，訊息不說「不符合」', async () => {
    // Arrange
    const view = await openTable('/projects/SMK?assignee=me');
    const bodies = serveCreate('SMK', smkItem({ number: 2 }));

    // Act
    const status = await addTitled(view, '新的任務');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '新的任務', assigneeId: exampleOwner.personId }]);
    expect(status).toHaveTextContent(/^已新增 SMK-2。$/);
    const row = await within(screen.getByRole('table', { name: '項目' })).findByRole('row', { name: /SMK-2/ });
    expect(within(row).getByRole('button', { name: /^負責人：範例使用者/ })).toBeInTheDocument();
  });

  it('負責人篩某個成員：新的項目指派給他', async () => {
    // Arrange
    const view = await openTable(`/projects/SMK?assignee=${memberAsEditor.personId}`);
    const bodies = serveCreate('SMK', smkItem({ number: 2 }));

    // Act
    await addTitled(view, '交給成員');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '交給成員', assigneeId: memberAsEditor.personId }]);
  });

  it('「沒有負責人」或不是成員的人（網址手打）不帶負責人', async () => {
    // Arrange
    const view = await openTable('/projects/SMK?assignee=0190a000-0000-7000-8000-0000000000ff');
    const bodies = serveCreate('SMK', smkItem({ number: 2 }));

    // Act
    await addTitled(view, '沒有指派');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '沒有指派' }]);
  });

  it('自己不是專案成員時（組織管理員），「我」不帶負責人', async () => {
    // Arrange
    const view = await openTable('/projects/SMK?assignee=me', smkDetail({ members: [memberAsEditor] }));
    const bodies = serveCreate('SMK', smkItem({ number: 2 }));

    // Act
    const status = await addTitled(view, '沒有指派');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '沒有指派' }]);
    expect(status).toHaveTextContent('已新增 SMK-2。它不符合目前的條件，清除條件之後可以看到。');
  });

  it('不符合搜尋或狀態的篩選時，訊息補一句，不讓它悄悄消失', async () => {
    // Arrange
    const view = await openTable('/projects/SMK?q=首頁&state=doing');
    serveCreate('SMK', smkItem({ number: 2, state: 'todo' }));

    // Act
    const status = await addTitled(view, '新的任務');

    // Assert
    expect(status).toHaveTextContent(/^已新增 SMK-2。它不符合目前的條件，清除條件之後可以看到。$/);
    expect(within(view.form).getByRole('status')).toBe(status);
  });

  it('符合搜尋時不補那一句', async () => {
    // Arrange
    const view = await openTable('/projects/SMK?q=任務');
    serveCreate('SMK', smkItem({ number: 2 }));

    // Act
    const status = await addTitled(view, '新的任務');

    // Assert
    expect(status).toHaveTextContent(/^已新增 SMK-2。$/);
  });

  it('訊息後面有「開啟」，連結指向新項目的項目頁（點了怎麼開見 CreatedStatus.test.tsx）', async () => {
    // Arrange
    const view = await openTable('/projects/SMK');
    serveCreate('SMK', smkItem({ number: 2, title: '新的任務' }));

    // Act
    await addTitled(view, '新的任務');

    // Assert
    const open = within(view.form).getByRole('link', { name: '開啟 SMK-2' });
    expect(open).toHaveAttribute('href', '/projects/SMK/items/SMK-2');
    expect(open).toHaveTextContent(/^開啟$/);
    expect(within(view.form).getByRole('status')).not.toContainElement(open);
  });

  it('窄螢幕的「新增項目」對話框也沿用篩選的類型與負責人', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', []);
    const bodies = serveCreate('AGY', agyItem({ number: 5, type: 'task', state: 'todo' }));
    const { user } = renderApp('/projects/AGY?type=task&assignee=me');
    await user.click(await screen.findByRole('button', { name: '新增項目' }));
    const dialog = screen.getByRole('dialog', { name: '新增項目' });

    // Act
    expect(within(dialog).getByRole('combobox', { name: '類型' })).toHaveTextContent('任務');
    await user.type(within(dialog).getByRole('textbox', { name: '標題' }), '切版{Enter}');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent(/^已新增 AGY-5。$/);
    expect(bodies).toEqual([{ type: 'task', title: '切版', assigneeId: exampleOwner.personId }]);
  });
});

describe('快速新增沿用優先度與到期日', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('篩了優先度：新的項目帶那個優先度', async () => {
    // Arrange
    const high = await openTable('/projects/SMK?priority=high');
    const bodies = serveCreate('SMK', smkItem({ number: 2, priority: 'high' }));

    // Act
    await addTitled(high, '很急');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '很急', priority: 'high' }]);
  });

  it('篩「今天到期」：新的項目到期日是今天', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 10, 18, 9));
    const view = await openTable('/projects/SMK?due=today');
    const bodies = serveCreate('SMK', smkItem({ number: 2, dueDate: '2026-11-18' }));

    // Act
    await addTitled(view, '今天要交');

    // Assert
    expect(bodies).toEqual([{ type: 'task', title: '今天要交', dueDate: '2026-11-18' }]);
  });

  it('篩了自訂欄位的值：新的項目帶著它；類型沒有這個欄位時不帶（後端會擋）', async () => {
    // Arrange
    const view = await openTable('/projects/AGY?type=change_request&field.impact=high', agyDetail());
    const bodies = serveCreate('AGY', agyItem({ number: 5, type: 'change_request', state: 'confirming' }));

    // Act
    await addTitled(view, '加購物車備註');
    await chooseOption(view.user, within(view.form).getByRole('combobox', { name: '類型' }), '任務');
    await addTitled(view, '切版');

    // Assert
    expect(bodies).toEqual([
      { type: 'change_request', title: '加購物車備註', fields: { impact: 'high' } },
      { type: 'task', title: '切版' },
    ]);
  });

  it('「沒有填」與「沒勾選」不當成預設值', async () => {
    // Arrange
    const view = await openTable('/projects/AGY?type=change_request&field.impact=_none', agyDetail());
    const bodies = serveCreate('AGY', agyItem({ number: 5, type: 'change_request', state: 'confirming' }));

    // Act
    await addTitled(view, '加購物車備註');

    // Assert
    expect(bodies).toEqual([{ type: 'change_request', title: '加購物車備註' }]);
  });
});
