import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { created, manyComments, serveActivity, showAllActivity, transitioned, updated } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, failTransition, serveItem, serveItems, servePatch, serveTransition } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿', dueDate: '2026-11-20' }), '說明');
const creation = created();

function serveTaskPage() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  return serveActivity(task.key, [creation]);
}

describe('在項目頁修改', () => {
  it('在頁首改標題，標題與分頁標題跟著變，活動重抓後出現這次的修改', async () => {
    // Arrange
    const activity = serveTaskPage();
    const patches = servePatch(task);
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    const heading = await screen.findByRole('heading', { level: 1 });
    const section = screen.getByRole('region', { name: '活動與留言' });
    await within(section).findByText(/建立了這個項目/);
    activity.set([creation, updated([{ field: 'title', from: '首頁設計稿', to: '首頁設計稿第二版' }])]);

    // Act
    await user.click(within(heading).getByRole('button', { name: '標題：首頁設計稿' }));
    const input = within(heading).getByRole('textbox', { name: '標題' });
    await user.clear(input);
    await user.type(input, '首頁設計稿第二版{Enter}');

    // Assert
    expect(await within(heading).findByRole('button', { name: '標題：首頁設計稿第二版' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ title: '首頁設計稿第二版' }]);
    const list = within(section).getByRole('list', { name: '活動與留言' });
    await waitFor(() => {
      expect(list.lastElementChild).toHaveTextContent('範例使用者 把「標題」從 首頁設計稿 改成 首頁設計稿第二版');
    });
    expect(activity.requests).toHaveLength(2);
    await waitFor(() => {
      expect(document.title).toBe(`SMK-7 首頁設計稿第二版 · ${PRODUCT_NAME}`);
    });
  });

  it('改狀態成功後顯示新的狀態，時間軸多一筆改狀態', async () => {
    // Arrange
    const activity = serveTaskPage();
    const requests = serveTransition(task, { ...task, state: 'doing', stateCategory: 'in_progress' });
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' });
    const section = screen.getByRole('region', { name: '活動與留言' });
    await within(section).findByText(/建立了這個項目/);
    activity.set([creation, transitioned('todo', 'doing')]);

    // Act
    await user.click(within(fields).getByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(fields).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    expect(await within(fields).findByRole('button', { name: '狀態：進行中' })).toHaveFocus();
    expect(requests.map((request) => request.body)).toEqual([{ to: 'doing' }]);
    const list = within(section).getByRole('list', { name: '活動與留言' });
    await waitFor(() => {
      expect(list.lastElementChild).toHaveTextContent('範例使用者 把狀態從「待辦」改成「進行中」');
    });
    expect(within(section).getAllByRole('status').at(-1)).toHaveTextContent('範例使用者 把狀態從「待辦」改成「進行中」');
  });

  it('改狀態被角色擋下時，照表格的方式說明，並可以打開成員調整', async () => {
    // Arrange
    const inQa = detailOf(agyItem({ number: 3, title: '官網需求', state: 'internal_qa', stateCategory: 'in_progress' }));
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItem(inQa);
    serveActivity(inQa.key, [created()]);
    serveItems('AGY', []);
    failTransition(inQa, 403, 'item.transition_role_required', '只有「專案經理」可以做。', [
      { code: 'item.transition_role_required', path: 'role', message: '「專案經理」（project_manager）' },
    ]);
    const { user } = renderApp('/projects/AGY/items/AGY-3');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Act
    await user.click(await within(fields).findByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(fields).getByRole('combobox', { name: '狀態' }), '客戶驗收');

    // Assert
    const alert = await within(fields).findByRole('alert');
    expect(alert).toHaveTextContent('只有「專案經理」可以做。');
    expect(alert).toHaveTextContent('需要的角色專案經理');
    expect(alert).toHaveTextContent('你的角色管理員');
    await user.click(within(alert).getByRole('button', { name: '到「成員」調整角色' }));
    expect(await screen.findByRole('dialog', { name: '成員' })).toBeInTheDocument();
  });

  // F 輪（U1）：寬螢幕點編號改成在旁邊開項目面板，不換頁；原本「進到項目頁、從麵包屑回到表格」改成「在面板裡改、關上面板」。
  it('從表格的編號打開項目面板，改好之後關上，表格裡的項目也是新的，而且不重抓清單', async () => {
    // Arrange
    const listItem = smkItem({ number: 7, title: '首頁設計稿', dueDate: '2026-11-20' });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const listRequests = serveItems('SMK', [listItem]);
    serveItem(task);
    serveActivity(task.key, [created()]);
    servePatch(task);
    const { user, router } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });

    // Act
    await user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    await user.click(within(fields).getByRole('button', { name: '到期日：2026/11/20' }));
    const input = within(fields).getByLabelText('到期日');
    await user.clear(input);
    await user.type(input, '2026-12-24');
    await user.keyboard('{Enter}');
    await within(fields).findByRole('button', { name: '到期日：2026/12/24' });
    await user.click(within(panel).getByRole('button', { name: '關閉' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
    const again = await screen.findByRole('table', { name: '項目' });
    expect(within(again).getByRole('button', { name: '到期日：2026/12/24' })).toBeInTheDocument();
    expect(listRequests).toHaveLength(1);
  });

  describe('時間軸跨過一頁之後', () => {
    const titleChange = updated([{ field: 'title', from: '首頁設計稿', to: '第二版' }]);

    async function editTitleTo(user: ReturnType<typeof renderApp>['user'], heading: HTMLElement, title: string) {
      await user.click(within(heading).getByRole('button', { name: '標題：首頁設計稿' }));
      const input = within(heading).getByRole('textbox', { name: '標題' });
      await user.clear(input);
      await user.type(input, `${title}{Enter}`);
    }

    it('原本已經載到底，修改後新的一筆落在下一頁，重抓時自動接著讀，不必按「載入更多」', async () => {
      // Arrange
      signedInAs(exampleOwner, { details: [smkDetail()] });
      serveItem(task);
      const history = [creation, ...manyComments(99)];
      const activity = serveActivity(task.key, history);
      servePatch(task);
      const { user } = renderApp('/projects/SMK/items/SMK-7');
      const heading = await screen.findByRole('heading', { level: 1 });
      const section = screen.getByRole('region', { name: '活動與留言' });
      await within(section).findByText('留言 49');
      await user.click(within(section).getByRole('button', { name: '載入更多' }));
      await within(section).findByText('留言 99');
      expect(within(section).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
      activity.set([...history, titleChange]);

      // Act
      await editTitleTo(user, heading, '第二版');

      // Assert
      const list = within(section).getByRole('list', { name: '活動與留言' });
      await waitFor(() => {
        expect(list.lastElementChild).toHaveTextContent('範例使用者 把「標題」從 首頁設計稿 改成 第二版');
      });
      expect(list.children).toHaveLength(101);
      expect(within(section).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
      expect(within(section).getAllByRole('status').at(-1)).toHaveTextContent('範例使用者 把「標題」從 首頁設計稿 改成 第二版');
      expect(activity.requests).toHaveLength(5);
    });

    it('原本還沒載到底時，重抓只重抓已經載入的頁，後面的照樣等「載入更多」', async () => {
      // Arrange
      signedInAs(exampleOwner, { details: [smkDetail()] });
      serveItem(task);
      const history = [creation, ...manyComments(50)];
      const activity = serveActivity(task.key, history);
      servePatch(task);
      const { user } = renderApp('/projects/SMK/items/SMK-7');
      const heading = await screen.findByRole('heading', { level: 1 });
      const section = screen.getByRole('region', { name: '活動與留言' });
      await within(section).findByText('留言 49');
      activity.set([...history, titleChange]);

      // Act
      await editTitleTo(user, heading, '第二版');

      // Assert
      await waitFor(() => {
        expect(activity.requests).toHaveLength(2);
      });
      const list = within(section).getByRole('list', { name: '活動與留言' });
      expect(list.children).toHaveLength(50);
      expect(within(section).getByRole('button', { name: '載入更多' })).toBeInTheDocument();
    });
  });
});
