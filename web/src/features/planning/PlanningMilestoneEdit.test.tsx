import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { milestoneOf } from '../../test/planning';
import { fakeToday, openPlanning } from './planningTestUtils';

afterEach(() => {
  vi.useRealTimers();
});

const v1 = milestoneOf('v1.0 上線', '2026-12-16', {
  id: 'm1',
  description: '第一個對外的版本。',
  progress: { total: 3, done: 1, canceled: 0, estimateTotal: null, estimateDone: null },
});

describe('里程碑的新增、編輯、刪除', () => {
  it('新增：到期日預設今天，名稱必填；送出後出現在清單裡', async () => {
    // Arrange
    fakeToday();
    const { user, milestoneServer } = openPlanning();
    await screen.findByText('還沒有里程碑');
    await user.click(screen.getByRole('button', { name: '新增里程碑' }));
    const dialog = await screen.findByRole('dialog', { name: '新增里程碑' });
    expect(within(dialog).getByRole('combobox', { name: '到期日' })).toHaveValue('2026/11/18');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '新增' }));
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveAccessibleDescription('請輸入里程碑的名稱。');
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '內測');
    await user.type(within(dialog).getByRole('textbox', { name: '說明' }), '給十個人試用。');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(milestoneServer.created.map((request) => request.body)).toEqual([
      { name: '內測', dueDate: '2026-11-18', description: '給十個人試用。' },
    ]);
    expect(await screen.findByRole('link', { name: '內測' })).toBeInTheDocument();
  });

  it('說明留空時送 null', async () => {
    // Arrange
    fakeToday();
    const { user, milestoneServer } = openPlanning();
    await screen.findByText('還沒有里程碑');
    await user.click(screen.getByRole('button', { name: '新增里程碑' }));
    const dialog = await screen.findByRole('dialog', { name: '新增里程碑' });

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '內測');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    await waitFor(() => {
      expect(milestoneServer.created).toHaveLength(1);
    });
    expect(milestoneServer.created[0]?.body).toMatchObject({ description: null });
  });

  it('編輯：帶出目前的值，改了送整份', async () => {
    // Arrange
    fakeToday();
    const { user, milestoneServer } = openPlanning({ milestones: [v1] });
    await screen.findByRole('list', { name: '里程碑' });
    await user.click(screen.getByRole('button', { name: 'v1.0 上線 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '編輯' }));
    const dialog = await screen.findByRole('dialog', { name: '編輯里程碑' });
    const description = within(dialog).getByRole('textbox', { name: '說明' });
    expect(description).toHaveValue('第一個對外的版本。');

    // Act
    await user.clear(description);
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(milestoneServer.replaced).toHaveLength(1);
    });
    expect(milestoneServer.replaced[0]?.body).toEqual({ name: 'v1.0 上線', dueDate: '2026-12-16', description: null });
  });

  it('刪除要確認，說明項目只是變成沒有里程碑', async () => {
    // Arrange
    fakeToday();
    const { user, milestoneServer } = openPlanning({ milestones: [v1] });
    await screen.findByRole('list', { name: '里程碑' });
    await user.click(screen.getByRole('button', { name: 'v1.0 上線 的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除「v1.0 上線」？' });
    expect(dialog).toHaveAccessibleDescription('這個里程碑有 3 個項目。項目不會被刪除，只會變成沒有里程碑。');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(milestoneServer.deleted.map((request) => request.body)).toEqual(['m1']);
    expect(await screen.findByText('還沒有里程碑')).toBeInTheDocument();
  });

  it('新增失敗之後取消，再開一次不帶著上一次的錯誤', async () => {
    // Arrange
    fakeToday();
    const { user, milestoneServer } = openPlanning({ milestones: [v1] });
    await screen.findByRole('list', { name: '里程碑' });
    await user.click(screen.getByRole('button', { name: '新增里程碑' }));
    const first = await screen.findByRole('dialog', { name: '新增里程碑' });
    await user.type(within(first).getByRole('textbox', { name: '名稱' }), 'v2.0');
    milestoneServer.rejectNext({ status: 409, code: 'milestone.name_taken', message: '已經有同名的里程碑。' });
    await user.click(within(first).getByRole('button', { name: '新增' }));
    await within(first).findByText('已經有同名的里程碑。');

    // Act
    await user.click(within(first).getByRole('button', { name: '取消' }));
    await user.click(screen.getByRole('button', { name: '新增里程碑' }));

    // Assert
    const second = await screen.findByRole('dialog', { name: '新增里程碑' });
    expect(within(second).queryByText('已經有同名的里程碑。')).not.toBeInTheDocument();
  });
});
