import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, makeItem, smkDetail, smkItem, webProject } from '../../test/fixtures';
import { serveTransition } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const overdue = makeItem({ number: 3, title: '首頁改版需求', dueDate: daysFromToday(-2) });
const dueToday = makeItem({ number: 1, title: '寄出報價', dueDate: daysFromToday(0) });
const undated = makeItem({ number: 5, title: '整理素材' });

function myWorkLink(): HTMLElement {
  return within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' });
}

/** 我的工作的請求數：側欄的數量不能自己多打請求。 */
function countMyWorkRequests(items: readonly Item[]): { readonly count: () => number } {
  let requests = 0;
  server.use(
    http.get('/v1/my-work', () => {
      requests += 1;
      return ok({ items });
    }),
  );
  return { count: () => requests };
}

describe('側欄「我的工作」旁邊的數量', () => {
  it('顯示還沒結束的數量，已過期的另外說出來（說明給報讀軟體）；連結的名稱不變', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdue, dueToday, undated] });

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('3 項、1 項已過期');
    });
    expect(myWorkLink()).toHaveAccessibleName('我的工作');
  });

  it('沒有過期的只說數量', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [dueToday, undated] });

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '今天' });
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('2 項');
    });
  });

  it('沒有工作時不顯示數量', async () => {
    // Arrange
    signedInAs(exampleOwner, { myWork: [] });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByText('目前沒有你負責的工作')).toBeInTheDocument();
    expect(myWorkLink()).toHaveAccessibleDescription('');
  });

  it('還沒載入我的工作時不顯示，也不為了數量多打請求；載入過之後換到別頁還在', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdue, undated] });
    const requests = countMyWorkRequests([overdue, undated]);
    const { router } = renderApp('/projects');
    await screen.findByRole('heading', { level: 1, name: '專案' });
    expect(myWorkLink()).toHaveAccessibleDescription('');
    expect(requests.count()).toBe(0);

    // Act
    await router.navigate('/my-work');
    await screen.findByRole('table', { name: '已過期' });
    await router.navigate('/projects');
    await screen.findByRole('heading', { level: 1, name: '專案' });

    // Assert
    expect(myWorkLink()).toHaveAccessibleDescription('2 項、1 項已過期');
    expect(requests.count()).toBe(1);
  });

  it('在我的工作把一項改成完成，數量跟著少一個', async () => {
    // Arrange
    const task = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
    const other = smkItem({ number: 2, title: '整理素材' });
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [task, other] });
    serveTransition(task, { ...task, state: 'done', stateCategory: 'done', updatedAt: '2026-11-16T09:00:00Z' });
    const { user } = renderApp('/my-work');
    const row = await screen.findByRole('row', { name: /SMK-1/ });
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('2 項、1 項已過期');
    });

    // Act
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '完成');

    // Assert
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('1 項');
    });
  });

  it('和分頁的計數同一個長相：數量是中性的，過期的是危險色加錯誤的圖示（畫面上的都不唸）', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdue, dueToday, undated] });

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    const total = await waitFor(() => within(myWorkLink()).getByText('3'));
    const overdueCount = within(myWorkLink()).getByText('1');
    expect(total).toHaveAttribute('data-tone', 'neutral');
    expect(overdueCount).toHaveAttribute('data-tone', 'danger');
    expect(overdueCount.firstElementChild).toHaveAttribute('data-icon', 'error');
    expect(total.closest('[aria-hidden="true"]')).not.toBeNull();
    expect(myWorkLink()).toHaveAccessibleName('我的工作');
  });
});
