import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { serveItems, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';

/*
 * 格子裡的日期（docs/DESIGN.md 8.3「就地編輯」、7.3「日期」）：components/DatePicker 的 bare。
 * 用滑鼠點開的格子直接打開日曆，從日曆選了就存並結束編輯；用鍵盤進入不打開；打字照舊 Enter 或離開才存；
 * 打了解析不了的字在格子裡說明。日曆從值的那個月開始，所以這裡的測試不必固定今天。
 */

const withDue = smkItem({ number: 7, title: '首頁設計稿', dueDate: '2026-12-01' });

const FORMAT_MESSAGE = '日期的格式不對，請輸入像 2026/12/15 的日期。';

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openTask(item: Item = withDue) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [item]);
  const patches = servePatch(item);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: new RegExp(item.key) });
  return { ...view, row, patches };
}

function calendar() {
  return screen.findByRole('dialog', { name: '選擇日期' });
}

describe('格內編輯：日期', () => {
  it('用滑鼠點日期格：日曆直接打開，焦點留在輸入框', async () => {
    // Arrange
    const { user, row } = await openTask();

    // Act
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));

    // Assert
    const due = within(row).getByRole('combobox', { name: '到期日' });
    expect(due).toHaveFocus();
    expect(due).toHaveValue('2026/12/01');
    expect(due).toHaveAttribute('aria-expanded', 'true');
    expect(await calendar()).toBeInTheDocument();
  });

  it('從日曆選了就存並結束編輯，焦點回到格子', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));

    // Act
    await user.click(within(await calendar()).getByRole('gridcell', { name: /^2026 年 12 月 24 日/ }));

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：2026/12/24' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-12-24' }]);
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
  });

  it('在日曆裡用方向鍵移動不存；Enter 選了才存', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));
    await calendar();

    // Act
    await user.keyboard('{ArrowDown}{ArrowRight}');
    await flushPending();

    // Assert
    expect(patches).toHaveLength(0);
    expect(screen.getByRole('gridcell', { name: /^2026 年 12 月 2 日/ })).toHaveFocus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：2026/12/02' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-12-02' }]);
  });

  it('「清除」就是清掉那個值', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));

    // Act
    await user.click(within(await calendar()).getByRole('button', { name: '清除' }));

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：未填' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: null }]);
  });

  it('日曆開著時 Esc 一次就關上並取消編輯，焦點回到格子，不送出', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));
    await calendar();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(within(row).getByRole('button', { name: '到期日：2026/12/01' })).toHaveFocus();
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    await flushPending();
    expect(patches).toHaveLength(0);
  });

  it('用鍵盤進入（Enter、F2）不打開日曆；打字不馬上存，Enter 才存', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    within(row).getByRole('button', { name: '到期日：2026/12/01' }).focus();

    // Act
    await user.keyboard('{Enter}');
    expect(within(row).getByRole('combobox', { name: '到期日' })).toHaveAttribute('aria-expanded', 'false');
    await user.keyboard('{Escape}');
    await user.keyboard('{F2}');
    const due = within(row).getByRole('combobox', { name: '到期日' });
    await user.clear(due);
    // 不用 user.type：它會先點一下輸入框，點了就打開日曆。這裡只用鍵盤。
    await user.keyboard('2026/12/24');
    await flushPending();

    // Assert
    expect(due).toHaveAttribute('aria-expanded', 'false');
    expect(patches).toHaveLength(0);
    expect(due).toHaveFocus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：2026/12/24' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-12-24' }]);
  });

  it('打了解析不了的字：格子裡說明格式不對、輸入框標成有錯，不送出', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    within(row).getByRole('button', { name: '到期日：2026/12/01' }).focus();
    await user.keyboard('{Enter}');
    const due = within(row).getByRole('combobox', { name: '到期日' });

    // Act
    await user.clear(due);
    await user.keyboard('下週一{Enter}');

    // Assert
    expect(await within(row).findByRole('alert')).toHaveTextContent(FORMAT_MESSAGE);
    expect(due).toHaveAttribute('aria-invalid', 'true');
    expect(due).toHaveAccessibleDescription(FORMAT_MESSAGE);
    expect(due).toHaveValue('下週一');
    await flushPending();
    expect(patches).toHaveLength(0);
  });

  it('日曆開著時輸入框被捲出畫面：只關日曆，打的字留著、照舊編輯中，Enter 才存', async () => {
    // Arrange
    const observer = stubIntersectionObserver();
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));
    await calendar();
    const due = within(row).getByRole('combobox', { name: '到期日' });
    await user.clear(due);
    await user.keyboard('2026/12/24');

    // Act
    observer.report(false);
    await flushPending();

    // Assert
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    expect(due).toBeInTheDocument();
    expect(due).toHaveValue('2026/12/24');
    expect(due).toHaveFocus();
    expect(patches).toHaveLength(0);

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：2026/12/24' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-12-24' }]);
  });

  it('用鍵盤進入之後按 ↓ 進日曆，在日曆裡按 Esc：關上日曆並取消編輯，焦點回到格子', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    within(row).getByRole('button', { name: '到期日：2026/12/01' }).focus();
    await user.keyboard('{Enter}');

    // Act
    await user.keyboard('{ArrowDown}');
    expect(within(await calendar()).getByRole('gridcell', { name: /^2026 年 12 月 1 日/ })).toHaveFocus();
    await user.keyboard('{ArrowRight}{Escape}');

    // Assert
    expect(within(row).getByRole('button', { name: '到期日：2026/12/01' })).toHaveFocus();
    expect(within(row).queryByRole('combobox', { name: '到期日' })).not.toBeInTheDocument();
    await flushPending();
    expect(patches).toHaveLength(0);
  });

  it('日曆開著時打了字再按 Tab 離開：只送一次', async () => {
    // Arrange
    const { user, row, patches } = await openTask();
    await user.click(within(row).getByRole('button', { name: '到期日：2026/12/01' }));
    await calendar();
    const due = within(row).getByRole('combobox', { name: '到期日' });
    await user.clear(due);
    await user.keyboard('2026/12/9');

    // Act
    await user.tab();

    // Assert
    expect(await within(row).findByRole('button', { name: '到期日：2026/12/09' })).toBeInTheDocument();
    await flushPending();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-12-09' }]);
  });

  it('自訂的日期欄位也一樣：從日曆選了就存進 fields', async () => {
    // Arrange
    const item = smkItem({ number: 7, title: '首頁設計稿', fields: { review_date: '2026-11-30' } });
    const { user, row, patches } = await openTask(item);
    await user.click(within(row).getByRole('button', { name: '檢查日：2026/11/30' }));

    // Act
    await user.click(within(await calendar()).getByRole('gridcell', { name: /^2026 年 11 月 27 日/ }));

    // Assert
    expect(await within(row).findByRole('button', { name: '檢查日：2026/11/27' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { review_date: '2026-11-27' } }]);
  });
});
