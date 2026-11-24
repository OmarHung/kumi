import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { created, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';

/*
 * 項目頁與項目面板的欄位也是表格的格子（EditableCell）：用滑鼠點開日期，日曆直接打開；從日曆選了就存並結束編輯，
 * 焦點回到那一格（docs/DESIGN.md 8.3「就地編輯」）。表格的同一件事在 item-table/DateEditing.test.tsx。
 */

const task = smkItem({ number: 7, title: '首頁設計稿', dueDate: '2026-11-20' });

function serveTask() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(detailOf(task, '說明'));
  serveActivity(task.key, [created()]);
  return servePatch(task);
}

async function pickFromCalendar(user: ReturnType<typeof renderApp>['user'], dayName: RegExp): Promise<void> {
  const calendar = await screen.findByRole('dialog', { name: '選擇日期' });
  await user.click(within(calendar).getByRole('gridcell', { name: dayName }));
}

describe('項目頁與面板的日期欄位', () => {
  it('項目頁：點到期日打開日曆，選了就存，焦點回到那一格', async () => {
    // Arrange
    const patches = serveTask();
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Act
    // 欄位區等週期與里程碑載入完才畫。
    await user.click(await within(fields).findByRole('button', { name: '到期日：2026/11/20' }));
    await pickFromCalendar(user, /^2026 年 11 月 24 日/);

    // Assert
    expect(await within(fields).findByRole('button', { name: '到期日：2026/11/24' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-11-24' }]);
  });

  it('項目面板：點到期日打開日曆，選了就存，焦點回到面板裡那一格，表格的那一格也跟著變', async () => {
    // Arrange
    const patches = serveTask();
    serveItems('SMK', [task]);
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const fields = await within(panel).findByRole('region', { name: '欄位' });

    // Act
    await user.click(within(fields).getByRole('button', { name: '到期日：2026/11/20' }));
    await pickFromCalendar(user, /^2026 年 11 月 24 日/);

    // Assert
    expect(await within(fields).findByRole('button', { name: '到期日：2026/11/24' })).toHaveFocus();
    expect(within(table).getByRole('button', { name: '到期日：2026/11/24' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ dueDate: '2026-11-24' }]);
    expect(screen.getByRole('complementary', { name: '項目 SMK-7' })).toBe(panel);
  });
});
