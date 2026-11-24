import { act, screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { serveItems, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

/*
 * 編輯中的格子分兩層（docs/DESIGN.md 8.3「就地編輯」、7.1）：原本的顯示留在原位撐住格子的大小（給報讀軟體隱藏），
 * 編輯器疊在上面、不參與表格的版面，進入編輯、儲存中、出錯時欄寬與列高都不變。
 * 寬度在 jsdom 量不到（實測在真的瀏覽器量），這裡驗兩層的結構與內容。
 */

const task = smkItem({ number: 7, title: '首頁設計稿' });

async function openTask() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [task]);
  const patches = servePatch(task);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: /SMK-7/ });
  return { ...view, row, patches };
}

function cellOf(element: HTMLElement): HTMLElement | null {
  return element.closest('td, th');
}

describe('編輯中的格子：原本的顯示撐住格子，編輯器浮在上面', () => {
  it('原本的顯示留在同一格、給報讀軟體隱藏；輸入框在浮起來的那一層（data-cell-editor）', async () => {
    // Arrange
    const { user, row } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '估計：未填' }));

    // Assert
    const input = within(row).getByRole('textbox', { name: '估計' });
    const shown = within(row).getByText('沒有估計');
    expect(shown.closest('[aria-hidden="true"]')).not.toBeNull();
    expect(shown.closest('[data-cell-editor]')).toBeNull();
    expect(input.closest('[data-cell-editor]')).not.toBeNull();
    expect(cellOf(input)).toBe(cellOf(shown));
  });

  it('下拉的按鈕裡是格子原本的樣子：同一個狀態標籤，沒有「（目前）」與箭頭；清單裡照舊', async () => {
    // Arrange
    const { user, row } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：待辦' }));

    // Assert
    const select = await within(row).findByRole('combobox', { name: '狀態' });
    expect(select).toHaveTextContent(/^待辦$/);
    expect(select.querySelector('[data-icon="chevronDown"]')).toBeNull();
    expect(select.querySelector('[data-icon="stateNotStarted"]')).not.toBeNull();
    const listbox = await within(row).findByRole('listbox', { name: '狀態' });
    expect(within(listbox).getByRole('option', { name: '待辦（目前）' })).toBeInTheDocument();
  });

  it('負責人的下拉：按鈕裡是格子原本的頭像與名字', async () => {
    // Arrange
    const { user, row } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '負責人：範例使用者' }));

    // Assert
    const select = await within(row).findByRole('combobox', { name: '負責人' });
    expect(select).toHaveTextContent('範例使用者');
    expect(select.querySelector('[data-icon="chevronDown"]')).toBeNull();
  });

  it('錯誤的說明在浮起來的那一層裡（不撐高那一列），原本的顯示還在', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '估計：未填' }));

    // Act
    await user.type(within(row).getByRole('textbox', { name: '估計' }), 'abc{Enter}');

    // Assert
    const alert = await within(row).findByRole('alert');
    expect(alert.closest('[data-cell-editor]')).not.toBeNull();
    const cell = cellOf(alert);
    expect(cell).not.toBeNull();
    expect(within(cell as HTMLElement).getByText('沒有估計').closest('[aria-hidden="true"]')).not.toBeNull();
    await flushPending();
    expect(patches).toHaveLength(0);
  });

  it('「儲存中…」的狀態區一進入編輯就掛著（先是空的），送出時才換字；外框不標 aria-busy', async () => {
    // Arrange
    const { user, row } = await openTask();
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.patch(`/v1/items/${task.key}`, async () => {
        await held;
        return ok({ ...task, estimate: 4 });
      }),
    );
    await user.click(within(row).getByRole('button', { name: '估計：未填' }));
    const input = within(row).getByRole('textbox', { name: '估計' });
    const editor = input.closest('[data-cell-editor]');
    const status = within(editor as HTMLElement).getByRole('status');
    expect(status).toHaveTextContent(/^$/);

    // Act
    await user.clear(input);
    await user.type(input, '4{Enter}');

    // Assert：同一個元素換字（新掛上的 live region 多數報讀軟體不唸）
    expect(within(editor as HTMLElement).getByRole('status')).toBe(status);
    expect(status).toHaveTextContent('儲存中…');
    expect(editor).not.toHaveAttribute('aria-busy');
    act(() => {
      release();
    });
    expect(await within(row).findByRole('button', { name: '估計：4' })).toHaveFocus();
  });

  it('Esc 取消：只剩原本的格子按鈕，焦點回到它', async () => {
    // Arrange
    const { user, row } = await openTask();
    await user.click(within(row).getByRole('button', { name: '估計：未填' }));

    // Act
    await user.keyboard('{Escape}');

    // Assert
    const trigger = within(row).getByRole('button', { name: '估計：未填' });
    expect(trigger).toHaveFocus();
    expect(within(row).queryByRole('textbox', { name: '估計' })).not.toBeInTheDocument();
  });
});
