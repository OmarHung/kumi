import { screen, waitFor, within } from '@testing-library/react';
import { http, type JsonBodyType } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { formatDate } from '../../domain/dates';
import { failure, ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, daysFromToday, exampleOwner } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const CHANGED_AT = '2026-11-16T09:00:00Z';
const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const inClientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress', assignee: mine });
const dueToday = agyItem({ number: 5, title: '今天要交的需求', assignee: mine, dueDate: daysFromToday(0) });

function rowKeysOf(tableName: string): (string | null | undefined)[] {
  return within(screen.getByRole('table', { name: tableName }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]?.textContent);
}

/**
 * 補填的兩步：第一次改狀態說要先填到期日；PATCH 照送來的內容改好（更新時間變新）；
 * 第二次改狀態又被別的原因（角色）擋下，對話框要留著說明。
 */
function serveDueDateThenRoleBlock(item: Item): void {
  let posts = 0;
  const fieldsRequired: JsonBodyType = [{ code: 'item.transition_fields_required', path: 'due_date', message: '要先填「到期日」。' }];
  server.use(
    http.patch(`/v1/items/${item.key}`, async ({ request }) => {
      const body = (await request.json()) as Partial<Item>;
      return ok(detailOf({ ...item, ...body, updatedAt: CHANGED_AT }));
    }),
    http.post(`/v1/items/${item.key}/transitions`, () => {
      posts += 1;
      return posts === 1
        ? failure(422, 'item.transition_fields_required', '要先填「到期日」才能進入「結案」。', fieldsRequired)
        : failure(403, 'item.transition_role_required', '你的角色不能做這個轉換。');
    }),
  );
}

describe('我的工作：補填之後只唸一句', () => {
  it('補填之後移到結案：狀態格不自己唸，工具列的 status 合成一句「已填好…，重新整理後會離開清單。」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], myWork: [inClientQa, dueToday] });
    let posts = 0;
    server.use(
      http.patch(`/v1/items/${inClientQa.key}`, async ({ request }) => {
        const body = (await request.json()) as Partial<Item>;
        return ok(detailOf({ ...inClientQa, ...body, updatedAt: CHANGED_AT }));
      }),
      http.post(`/v1/items/${inClientQa.key}/transitions`, () => {
        posts += 1;
        const fieldsRequired: JsonBodyType = [{ code: 'item.transition_fields_required', path: 'due_date', message: '要先填「到期日」。' }];
        return posts === 1
          ? failure(422, 'item.transition_fields_required', '要先填「到期日」才能進入「結案」。', fieldsRequired)
          : ok(detailOf({ ...inClientQa, state: 'closed', stateCategory: 'done', dueDate: daysFromToday(0), updatedAt: CHANGED_AT }));
      }),
    );
    const { user } = renderApp('/my-work');
    const row = await screen.findByRole('row', { name: /AGY-4/ });
    await user.click(await within(row).findByRole('button', { name: '狀態：客戶驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '結案');
    const dialog = await screen.findByRole('dialog', { name: '移到「結案」之前要先填' });

    // Act
    await user.type(within(dialog).getByLabelText('到期日'), daysFromToday(0));
    await user.click(within(dialog).getByRole('button', { name: '填好並移到「結案」' }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toHaveFocus();
    // 整頁只有一句在唸（其他 status 都是空的；表格最下面的「已載入 N 個項目」是清單的計數，不是這件事）：不是狀態格一句、工具列一句。
    await waitFor(() => {
      const spoken = screen
        .getAllByRole('status')
        .map((status) => status.textContent)
        .filter((text) => text !== '' && !text.startsWith('已載入'));
      expect(spoken).toEqual(['已填好「到期日」，AGY-4 已移到「結案」，重新整理後會離開清單。']);
    });
    expect(within(row).queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('我的工作：補填之後列不換組', () => {
  it('補填到期日：列停在原本那一組（只換顯示的值），對話框不被卸下、焦點留著，第二步被擋下時說明出現', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], myWork: [inClientQa, dueToday] });
    serveDueDateThenRoleBlock(inClientQa);
    const { user } = renderApp('/my-work');
    const row = await screen.findByRole('row', { name: /AGY-4/ });
    await user.click(await within(row).findByRole('button', { name: '狀態：客戶驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '結案');
    const dialog = await screen.findByRole('dialog', { name: '移到「結案」之前要先填' });

    // Act
    await user.type(within(dialog).getByLabelText('到期日'), daysFromToday(0));
    await user.click(within(dialog).getByRole('button', { name: '填好並移到「結案」' }));

    // Assert
    expect(await within(dialog).findByText('欄位已經存好，但還沒有移到「結案」。')).toBeInTheDocument();
    expect(dialog).toBeInTheDocument();
    expect(dialog).toContainElement(document.activeElement as HTMLElement);
    expect(within(dialog).getByText('專案經理')).toBeInTheDocument();
    expect(rowKeysOf('沒有到期日')).toEqual(['AGY-4']);
    expect(rowKeysOf('今天')).toEqual(['AGY-5']);
    await waitFor(() => {
      expect(within(screen.getByRole('row', { name: /AGY-4/ })).getByText(formatDate(daysFromToday(0)))).toBeInTheDocument();
    });
  });
});
