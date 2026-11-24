import { screen, waitFor, within } from '@testing-library/react';
import type userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cycleOf } from '../../test/planning';
import { fakeToday, openPlanning } from './planningTestUtils';

afterEach(() => {
  vi.useRealTimers();
});

const sprint1 = cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: 'c1' });
const sprint2 = cycleOf('衝刺 2', '2026-11-16', '2026-11-29', {
  id: 'c2',
  progress: { total: 7, done: 2, canceled: 1, estimateTotal: null, estimateDone: null },
});

async function replaceDate(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, label: string, text: string) {
  const input = within(dialog).getByRole('combobox', { name: label });
  await user.clear(input);
  await user.type(input, `${text}{Tab}`);
}

describe('新增週期', () => {
  it('預設名稱是「衝刺 N」、開始日接在最後一個結束的隔天、結束日是 13 天後', async () => {
    // Arrange
    fakeToday();
    const { user } = openPlanning({ cycles: [sprint1, sprint2] });
    await screen.findByRole('list', { name: '週期' });

    // Act
    await user.click(screen.getByRole('button', { name: '新增週期' }));
    const dialog = await screen.findByRole('dialog', { name: '新增週期' });

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('衝刺 3');
    expect(within(dialog).getByRole('combobox', { name: '開始日' })).toHaveValue('2026/11/30');
    expect(within(dialog).getByRole('combobox', { name: '結束日' })).toHaveValue('2026/12/13');
  });

  it('沒有週期時開始日是今天；送出後關掉對話框，新的週期出現在清單裡', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning();
    await screen.findByText('還沒有週期');
    await user.click(screen.getByRole('button', { name: '新增週期' }));
    const dialog = await screen.findByRole('dialog', { name: '新增週期' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(cycleServer.created.map((request) => request.body)).toEqual([{ name: '衝刺 1', startDate: '2026-11-18', endDate: '2026-12-01' }]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('link', { name: '衝刺 1' })).toBeInTheDocument();
  });

  it('名稱空白時留在對話框，錯誤在欄位上、不送出', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning();
    await screen.findByText('還沒有週期');
    await user.click(screen.getByRole('button', { name: '新增週期' }));
    const dialog = await screen.findByRole('dialog', { name: '新增週期' });

    // Act
    await user.clear(within(dialog).getByRole('textbox', { name: '名稱' }));
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveAccessibleDescription('請輸入週期的名稱。');
    expect(cycleServer.created).toHaveLength(0);
  });

  it('和別的週期重疊：伺服器的說明留在表單上方，對話框不關', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint2] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '新增週期' }));
    const dialog = await screen.findByRole('dialog', { name: '新增週期' });
    await replaceDate(user, dialog, '開始日', '2026/11/20');
    cycleServer.rejectNext({ status: 409, code: 'cycle.overlaps', message: '和「衝刺 2」的日期重疊。' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(await within(dialog).findByText('和「衝刺 2」的日期重疊。')).toBeInTheDocument();
  });

  it('名稱重複：錯誤留在名稱欄位', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint1] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '新增週期' }));
    const dialog = await screen.findByRole('dialog', { name: '新增週期' });
    cycleServer.rejectNext({ status: 409, code: 'cycle.name_taken', message: '已經有同名的週期。' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    await waitFor(() => {
      expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveAccessibleDescription('已經有同名的週期。');
    });
  });
});

describe('對話框關上就清掉上一次的失敗', () => {
  it('新增失敗之後取消，再開一次不帶著上一次的錯誤', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint2] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '新增週期' }));
    const first = await screen.findByRole('dialog', { name: '新增週期' });
    cycleServer.rejectNext({ status: 409, code: 'cycle.overlaps', message: '和「衝刺 2」的日期重疊。' });
    await user.click(within(first).getByRole('button', { name: '新增' }));
    await within(first).findByText('和「衝刺 2」的日期重疊。');

    // Act
    await user.click(within(first).getByRole('button', { name: '取消' }));
    await user.click(screen.getByRole('button', { name: '新增週期' }));

    // Assert
    const second = await screen.findByRole('dialog', { name: '新增週期' });
    expect(within(second).queryByText('和「衝刺 2」的日期重疊。')).not.toBeInTheDocument();
  });

  it('編輯失敗之後取消，再開一次不帶著上一次的錯誤', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint1, sprint2] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '衝刺 2 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '編輯' }));
    const first = await screen.findByRole('dialog', { name: '編輯週期' });
    cycleServer.rejectNext({ status: 409, code: 'cycle.overlaps', message: '和「衝刺 1」的日期重疊。' });
    await user.click(within(first).getByRole('button', { name: '儲存' }));
    await within(first).findByText('和「衝刺 1」的日期重疊。');

    // Act
    await user.click(within(first).getByRole('button', { name: '取消' }));
    await user.click(screen.getByRole('button', { name: '衝刺 2 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '編輯' }));

    // Assert
    const second = await screen.findByRole('dialog', { name: '編輯週期' });
    expect(within(second).queryByText('和「衝刺 1」的日期重疊。')).not.toBeInTheDocument();
  });
});

describe('編輯與刪除週期', () => {
  it('更多選單的「編輯」帶出目前的值，改了名稱送整份', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint2] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '衝刺 2 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '編輯' }));
    const dialog = await screen.findByRole('dialog', { name: '編輯週期' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(name).toHaveValue('衝刺 2');

    // Act
    await user.clear(name);
    await user.type(name, '衝刺 二');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(cycleServer.replaced.map((request) => request.body)).toEqual([{ name: '衝刺 二', startDate: '2026-11-16', endDate: '2026-11-29' }]);
    expect(await screen.findByRole('link', { name: '衝刺 二' })).toBeInTheDocument();
  });

  it('刪除要確認，說明項目不會被刪除；確認後週期從清單消失', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint1, sprint2] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '衝刺 2 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除「衝刺 2」？' });
    expect(dialog).toHaveAccessibleDescription('這個週期有 7 個項目。項目不會被刪除，只會變成沒有週期。');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(cycleServer.deleted.map((request) => request.body)).toEqual(['c2']);
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: '衝刺 2' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: '衝刺 1' })).toBeInTheDocument();
  });

  it('取消刪除什麼都不會發生', async () => {
    // Arrange
    fakeToday();
    const { user, cycleServer } = openPlanning({ cycles: [sprint2] });
    await screen.findByRole('list', { name: '週期' });
    await user.click(screen.getByRole('button', { name: '衝刺 2 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '取消' }));

    // Assert
    expect(cycleServer.deleted).toHaveLength(0);
    expect(screen.getByRole('link', { name: '衝刺 2' })).toBeInTheDocument();
  });
});
