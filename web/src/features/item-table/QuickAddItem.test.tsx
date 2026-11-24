import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

function serveCreate(projectKey: string, created: Item): unknown[] {
  const bodies: unknown[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items`, async ({ request }) => {
      bodies.push(await request.json());
      return ok(created, 201);
    }),
  );
  return bodies;
}

async function findQuickAdd() {
  return screen.findByRole('form', { name: '新增項目' });
}

describe('新增項目', () => {
  it('只有一種類型時只要填標題；建立後出現在表格最上面，標題框清空、焦點留著可以接著打', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const listRequests = serveItems('SMK', [smkItem({ number: 1, title: '既有的項目' })]);
    const created = smkItem({ number: 2, title: '新的任務' });
    const bodies = serveCreate('SMK', created);
    const { user } = renderApp('/projects/SMK');
    const form = await findQuickAdd();
    await screen.findByRole('table', { name: '項目' });

    // Act
    expect(within(form).queryByRole('combobox', { name: '類型' })).not.toBeInTheDocument();
    const title = within(form).getByRole('textbox', { name: '標題' });
    await user.type(title, '  新的任務  {Enter}');

    // Assert
    const table = screen.getByRole('table', { name: '項目' });
    await within(table).findByRole('row', { name: /SMK-2/ });
    const keys = within(table)
      .getAllByRole('row')
      .slice(1)
      .map((row) => within(row).getAllByRole('cell')[1]?.textContent);
    expect(keys).toEqual(['SMK-2', 'SMK-1']);
    expect(bodies).toEqual([{ type: 'task', title: '新的任務' }]);
    expect(title).toHaveValue('');
    expect(title).toHaveFocus();
    expect(within(form).getByRole('status')).toHaveTextContent('已新增 SMK-2');
    expect(listRequests).toHaveLength(1);
  });

  it('注音選字的 Enter（Safari 是 keyCode 229）不會送出表單；選好字再按 Enter 才新增', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);
    const bodies = serveCreate('SMK', smkItem({ number: 2, title: '新的任務' }));
    const { user } = renderApp('/projects/SMK');
    const form = await findQuickAdd();
    const title = within(form).getByRole('textbox', { name: '標題' });
    await user.type(title, '新的任務');

    // Act
    const isSubmitAllowed = fireEvent.keyDown(title, { key: 'Enter', code: 'Enter', keyCode: 229 });
    await user.keyboard('{Enter}');

    // Assert
    expect(isSubmitAllowed).toBe(false);
    expect(await within(form).findByRole('status')).toHaveTextContent('已新增 SMK-2');
    expect(bodies).toEqual([{ type: 'task', title: '新的任務' }]);
  });

  it('新增之後再「載入更多」，後面那頁又帶到剛新增的項目時只出現一次', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const first = smkItem({ number: 1, title: '第一個' });
    const second = smkItem({ number: 2, title: '第二個' });
    const createdItem = smkItem({ number: 3, title: '新的任務' });
    serveItems('SMK', [first], [second]);
    serveCreate('SMK', createdItem);
    const { user } = renderApp('/projects/SMK');
    const form = await findQuickAdd();
    const table = await screen.findByRole('table', { name: '項目' });
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');
    await within(table).findByRole('row', { name: /SMK-3/ });
    serveItems('SMK', [first], [second, createdItem]);

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    await within(table).findByRole('row', { name: /SMK-2/ });
    const keys = within(table)
      .getAllByRole('row')
      .slice(1)
      .map((row) => within(row).getAllByRole('cell')[1]?.textContent);
    expect(keys).toEqual(['SMK-1', 'SMK-2', 'SMK-3']);
    expect(screen.getByText('已載入 3 個項目')).toBeInTheDocument();
  });

  it('標題沒填時不送出', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);
    const bodies = serveCreate('SMK', smkItem());
    const { user } = renderApp('/projects/SMK');
    const form = await findQuickAdd();

    // Act
    await user.click(within(form).getByRole('button', { name: '新增' }));

    // Assert
    expect(within(form).getByRole('textbox', { name: '標題' })).toHaveAccessibleDescription('請輸入標題。');
    expect(bodies).toHaveLength(0);
  });

  it('「請輸入標題。」之後開始打字，錯誤就拿掉（錯誤在輸入框裡，不和打的字並排）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);
    serveCreate('SMK', smkItem());
    const { user } = renderApp('/projects/SMK');
    const form = await findQuickAdd();
    await user.click(within(form).getByRole('button', { name: '新增' }));
    const title = within(form).getByRole('textbox', { name: '標題' });

    // Act
    await user.type(title, '修');

    // Assert
    expect(title).not.toHaveAttribute('aria-invalid');
    expect(within(form).queryByText('請輸入標題。')).toBeNull();
  });

  it('多種類型時先選類型；有上層限制的類型可以選上層項目', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const requirement = agyItem({ number: 1, title: '官網需求' });
    serveItems('AGY', [requirement]);
    const bodies = serveCreate('AGY', agyItem({ number: 2, type: 'task', title: '切版', parentKey: 'AGY-1' }));
    const { user } = renderApp('/projects/AGY');
    const form = await findQuickAdd();

    // Act
    expect(within(form).queryByRole('combobox', { name: '上層項目' })).not.toBeInTheDocument();
    await chooseOption(user, within(form).getByRole('combobox', { name: '類型' }), '任務');
    const parent = within(form).getByRole('combobox', { name: '上層項目' });
    await chooseOption(user, parent, 'AGY-1 官網需求');
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '切版');
    await user.click(within(form).getByRole('button', { name: '新增' }));

    // Assert
    expect(await within(screen.getByRole('table', { name: '項目' })).findByRole('row', { name: /AGY-2/ })).toBeInTheDocument();
    expect(bodies).toEqual([{ type: 'task', title: '切版', parentKey: 'AGY-1' }]);
  });

  it('後端的欄位錯誤顯示在對應的欄位旁，對不到的顯示在上方', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', []);
    server.use(
      http.post('/v1/projects/AGY/items', () =>
        failure(422, 'item.parent_not_allowed', '不能這樣放。', [
          { code: 'item.unknown_parent', path: 'parent', message: '找不到上層項目「AGY-999」。' },
          { code: 'item.other', path: null, message: '還有別的問題。' },
        ]),
      ),
    );
    const { user } = renderApp('/projects/AGY');
    const form = await findQuickAdd();

    // Act
    await chooseOption(user, within(form).getByRole('combobox', { name: '類型' }), '任務');
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '切版{Enter}');

    // Assert
    await waitFor(() => {
      expect(within(form).getByRole('combobox', { name: '上層項目' })).toHaveAccessibleDescription(
        '找不到上層項目「AGY-999」。',
      );
    });
    expect(within(form).getByRole('alert')).toHaveTextContent('還有別的問題。');
    expect(within(form).getByRole('textbox', { name: '標題' })).toHaveValue('切版');
  });

  it('沒有新增權限時不顯示', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });
    serveItems('SMK', []);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText('這個專案還沒有項目')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    expect(screen.getByText('有人新增項目後會出現在這裡。')).toBeInTheDocument();
  });
});
