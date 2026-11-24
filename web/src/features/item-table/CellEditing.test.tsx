import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import {
  agyDetail,
  agyItem,
  exampleMember,
  exampleOwner,
  smkDetail,
  smkItem,
} from '../../test/fixtures';
import { serveItems, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const task = smkItem({
  number: 7,
  title: '首頁設計稿',
  state: 'doing',
  stateCategory: 'in_progress',
  priority: 'medium',
  dueDate: '2026-12-01',
  estimate: 2,
  fields: {
    category: 'bug',
    tags: ['frontend'],
    story_points: 3,
    blocked: true,
    summary: '舊的摘要',
    notes: '舊的備註',
  },
});

async function openTask(item: Item = task) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const listRequests = serveItems('SMK', [item]);
  const patches = servePatch(item);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: new RegExp(item.key) });
  return { ...view, row, patches, listRequests };
}

describe('格內編輯：文字與數字', () => {
  it('估計按 Enter 儲存，只送改了的屬性，用回傳的項目更新而不重抓清單，焦點回到格子', async () => {
    // Arrange
    const { user, row, patches, listRequests } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    const input = within(row).getByRole('textbox', { name: '估計' });
    await user.clear(input);
    await user.type(input, '5{Enter}');

    // Assert
    const trigger = await within(row).findByRole('button', { name: '估計：5' });
    expect(trigger).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ estimate: 5 }]);
    expect(listRequests).toHaveLength(1);
  });

  it('Esc 取消，不送出，顯示原本的值', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '摘要：舊的摘要' }));
    await user.type(within(row).getByRole('textbox', { name: '摘要' }), '改到一半{Escape}');

    // Assert
    expect(within(row).getByRole('button', { name: '摘要：舊的摘要' })).toHaveFocus();
    expect(patches).toHaveLength(0);
  });

  it('沒有改就離開格子不送出', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '摘要：舊的摘要' }));
    await user.tab();

    // Assert
    expect(within(row).getByRole('button', { name: '摘要：舊的摘要' })).toBeInTheDocument();
    expect(patches).toHaveLength(0);
  });

  it('文字欄離開格子時儲存；清空就是清掉那個欄位', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '摘要：舊的摘要' }));
    await user.clear(within(row).getByRole('textbox', { name: '摘要' }));
    await user.tab();

    // Assert
    expect(await within(row).findByRole('button', { name: '摘要：未填' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { summary: null } }]);
  });

  it('長文字的 Enter 是換行，Ctrl+Enter 儲存', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '備註：舊的備註' }));
    const textarea = within(row).getByRole('textbox', { name: '備註' });
    await user.clear(textarea);
    await user.type(textarea, '第一行{Enter}第二行');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(await within(row).findByRole('button', { name: /^備註：第一行/ })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { notes: '第一行\n第二行' } }]);
  });

  it('數字欄接受千分位，打錯字時在格子裡說明而且不送出', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '點數：3' }));
    const input = within(row).getByRole('textbox', { name: '點數' });

    // Act
    await user.clear(input);
    await user.type(input, '三點五{Enter}');

    // Assert
    expect(within(row).getByRole('alert')).toHaveTextContent('請輸入數字。');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('請輸入數字。');
    expect(patches).toHaveLength(0);

    // Act
    await user.clear(input);
    await user.type(input, '1,234.5{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '點數：1,234.5' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { story_points: 1234.5 } }]);
  });

  it('金額、估計與連結各自送出對的值；估計清空送 null', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '預算：未填' }));
    await user.type(within(row).getByRole('textbox', { name: '預算' }), '1500{Enter}');
    await within(row).findByRole('button', { name: /^預算：.*1,500/ });
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    await user.clear(within(row).getByRole('textbox', { name: '估計' }));
    await user.keyboard('{Enter}');
    await within(row).findByRole('button', { name: '估計：未填' });
    await user.click(within(row).getByRole('button', { name: '參考連結：未填' }));
    await user.type(within(row).getByRole('textbox', { name: '參考連結' }), 'https://example.com/a{Enter}');

    // Assert
    expect(await within(row).findByRole('link', { name: '開啟參考連結' })).toHaveAttribute(
      'href',
      'https://example.com/a',
    );
    expect(patches.map((request) => request.body)).toEqual([
      { fields: { budget: 1500 } },
      { estimate: null },
      { fields: { reference_url: 'https://example.com/a' } },
    ]);
  });
});

describe('格內編輯：日期與選項', () => {
  it('核心的日期送 dueDate，自訂的日期放在 fields；清空是 null', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));
    // user-event 打日期時要整串打完才會寫進值，Enter 要分開按。
    const due = within(row).getByLabelText('到期日');
    await user.clear(due);
    await user.type(due, '2026-12-24');
    await user.keyboard('{Enter}');
    await within(row).findByRole('button', { name: '到期日：2026/12/24' });
    await user.click(within(row).getByRole('button', { name: '檢查日：未填' }));
    await user.type(within(row).getByLabelText('檢查日'), '2026-11-30');
    await user.keyboard('{Enter}');
    await within(row).findByRole('button', { name: '檢查日：2026/11/30' });
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/24' }));
    await user.clear(within(row).getByLabelText('到期日'));
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：未填' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([
      { dueDate: '2026-12-24' },
      { fields: { review_date: '2026-11-30' } },
      { dueDate: null },
    ]);
  });

  it('用滑鼠選了選項就儲存：單選、優先度、審查者', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '分類：錯誤' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '分類' }), '新功能');
    await within(row).findByRole('button', { name: '分類：新功能' });
    await user.click(within(row).getByRole('button', { name: '優先度：中' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '優先度' }), '緊急');
    await within(row).findByRole('button', { name: '優先度：緊急' });
    await user.click(within(row).getByRole('button', { name: '審查者：未填' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '審查者' }), '範例成員');
    await within(row).findByRole('button', { name: '審查者：範例成員' });
    await user.click(within(row).getByRole('button', { name: '分類：新功能' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '分類' }), '未選');

    // Assert
    expect(await within(row).findByRole('button', { name: '分類：未填' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([
      { fields: { category: 'feature' } },
      { priority: 'urgent' },
      { fields: { reviewer: exampleMember.personId } },
      { fields: { category: null } },
    ]);
  });

  it('負責人從成員裡選，選「沒有負責人」送 null', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task]);
    const bodies: unknown[] = [];
    server.use(
      http.patch(`/v1/items/${task.key}`, async ({ request }) => {
        const body = (await request.json()) as { assigneeId: string | null };
        bodies.push(body);
        const assignee = body.assigneeId === null ? null : { personId: body.assigneeId, displayName: '範例成員' };
        return ok({ ...task, assignee });
      }),
    );
    const { user } = renderApp('/projects/SMK');
    const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /SMK-7/ });

    // Act
    await user.click(within(row).getByRole('button', { name: '負責人：範例使用者' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '負責人' }), '範例成員');
    await user.click(await within(row).findByRole('button', { name: '負責人：範例成員' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '負責人' }), '沒有負責人');

    // Assert
    expect(await within(row).findByRole('button', { name: '負責人：未填' })).toBeInTheDocument();
    expect(bodies).toEqual([{ assigneeId: exampleMember.personId }, { assigneeId: null }]);
  });

  it('多選勾好後按 Enter 儲存，依選項的順序送；全部取消就清掉', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '標籤：前端' }));
    const group = within(row).getByRole('group', { name: '標籤' });
    await user.click(within(group).getByRole('checkbox', { name: '文件' }));
    await user.click(within(group).getByRole('checkbox', { name: '後端' }));
    await user.keyboard('{Enter}');
    await within(row).findByRole('button', { name: '標籤：前端、後端、文件' });
    await user.click(within(row).getByRole('button', { name: '標籤：前端、後端、文件' }));
    const again = within(row).getByRole('group', { name: '標籤' });
    for (const name of ['前端', '後端', '文件']) {
      await user.click(within(again).getByRole('checkbox', { name }));
    }
    await user.click(within(again).getByRole('button', { name: '完成' }));

    // Assert
    expect(await within(row).findByRole('button', { name: '標籤：未填' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([
      { fields: { tags: ['frontend', 'backend', 'docs'] } },
      { fields: { tags: null } },
    ]);
  });

  it('勾選直接儲存；取消勾選送 null', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    const checkbox = within(row).getByRole('checkbox', { name: '卡住了' });

    // Act
    await user.click(checkbox);
    await waitFor(() => {
      expect(checkbox).not.toBeChecked();
    });
    await user.click(checkbox);

    // Assert
    await waitFor(() => {
      expect(checkbox).toBeChecked();
    });
    expect(patches.map((request) => request.body)).toEqual([
      { fields: { blocked: null } },
      { fields: { blocked: true } },
    ]);
  });

  it('勾選儲存失敗時顯示後端的訊息', async () => {
    // Arrange
    const { user, row } = await openTask();
    server.use(http.patch(`/v1/items/${task.key}`, () => failure(403, 'work.forbidden', '你沒有權限修改這個項目。')));

    // Act
    await user.click(within(row).getByRole('checkbox', { name: '卡住了' }));

    // Assert
    expect(await within(row).findByRole('alert')).toHaveTextContent('你沒有權限修改這個項目。');
    expect(within(row).getByRole('checkbox', { name: '卡住了' })).toBeChecked();
  });

  it('上層項目從允許的類型裡選', async () => {
    // Arrange
    const child = agyItem({ number: 2, type: 'task', title: '切版', state: 'todo' });
    const parent = agyItem({ number: 1, title: '官網需求' });
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const listRequests = serveItems('AGY', [parent, child]);
    const patches = servePatch(child);
    const { user } = renderApp('/projects/AGY');
    const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /AGY-2/ });

    // Act
    await user.click(within(row).getByRole('button', { name: '上層項目：未填' }));
    const select = within(row).getByRole('combobox', { name: '上層項目' });
    await chooseOption(user, select, 'AGY-1 官網需求');

    // Assert
    expect(await within(row).findByRole('button', { name: '上層項目：AGY-1' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ parentKey: 'AGY-1' }]);
    const candidates = listRequests.at(-1)?.searchParams;
    expect(candidates?.getAll('type')).toEqual(['requirement', 'change_request']);
    expect(candidates?.get('limit')).toBe('200');
  });
});

describe('格內編輯：失敗與鍵盤', () => {
  it('儲存失敗時把後端的訊息顯示在格子裡，保留輸入的值，改好再按 Enter 就成功', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    server.use(
      http.patch(
        `/v1/items/${task.key}`,
        () =>
          failure(422, 'item.invalid_value', '欄位的值不正確。', [
            { code: 'item.invalid_value', path: 'reference_url', message: '「參考連結」要填 http:// 或 https:// 開頭的完整網址。' },
          ]),
        { once: true },
      ),
    );

    // Act
    await user.click(within(row).getByRole('button', { name: '參考連結：未填' }));
    const input = within(row).getByRole('textbox', { name: '參考連結' });
    await user.type(input, 'example.com{Enter}');

    // Assert
    expect(await within(row).findByRole('alert')).toHaveTextContent('「參考連結」要填 http:// 或 https:// 開頭的完整網址。');
    expect(input).toHaveValue('example.com');
    expect(input).toHaveAttribute('aria-invalid', 'true');

    // Act
    await user.clear(input);
    await user.type(input, 'https://example.com{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '參考連結：https://example.com' })).toBeInTheDocument();
    expect(within(row).queryByRole('alert')).not.toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { reference_url: 'https://example.com' } }]);
  });

  it('核心欄位的失敗用細項的訊息', async () => {
    // Arrange
    const { user, row } = await openTask();
    server.use(
      http.patch(`/v1/items/${task.key}`, () =>
        failure(422, 'item.estimate_invalid', '估計不正確。', [
          { code: 'item.estimate_negative', path: 'estimate', message: '估計不能是負數。' },
        ]),
      ),
    );

    // Act
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    await user.type(within(row).getByRole('textbox', { name: '估計' }), '9{Enter}');

    // Assert
    expect(await within(row).findByRole('alert')).toHaveTextContent('估計不能是負數。');
  });

  it('估計不是數字時不送出，直接說明', async () => {
    // Arrange
    const { user, row, patches } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    await user.clear(within(row).getByRole('textbox', { name: '估計' }));
    await user.type(within(row).getByRole('textbox', { name: '估計' }), 'abc{Enter}');

    // Assert
    expect(within(row).getByRole('alert')).toHaveTextContent('請輸入數字。');
    expect(patches).toHaveLength(0);
  });

  it('方向鍵在格子之間移動，Enter 進入編輯', async () => {
    // Arrange
    const { user, row } = await openTask();
    within(row).getByRole('link', { name: '首頁設計稿' }).focus();

    // Act
    await user.keyboard('{ArrowRight}');
    const stateCell = within(row).getByRole('button', { name: '狀態：進行中' });
    const focusedAfterArrow = document.activeElement;
    await user.keyboard('{ArrowRight}');
    await user.keyboard('{Enter}');

    // Assert
    expect(focusedAfterArrow).toBe(stateCell);
    expect(within(row).getByRole('combobox', { name: '負責人' })).toHaveFocus();
  });

  it('儲存中顯示狀態', async () => {
    // Arrange
    const { user, row } = await openTask();
    let release: () => void = () => undefined;
    server.use(
      http.patch(`/v1/items/${task.key}`, async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return ok({ ...task, estimate: 8 });
      }),
    );

    // Act
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    const input = within(row).getByRole('textbox', { name: '估計' });
    await user.clear(input);
    await user.type(input, '8{Enter}');

    // Assert
    expect(await within(row).findByRole('status')).toHaveTextContent('儲存中…');
    release();
    expect(await within(row).findByRole('button', { name: '估計：8' })).toBeInTheDocument();
  });
});
