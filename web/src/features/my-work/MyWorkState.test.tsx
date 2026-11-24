import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { Item } from '../../api/types';
import { serveActivity } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { agyDetail, agyItem, daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, failTransition, serveItem, serveItems, serveTransition } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const CHANGED_AT = '2026-11-16T09:00:00Z';
const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };

const smkTask = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
const smkUndated = smkItem({ number: 2, title: '整理素材' });
const agyRequirement = agyItem({ number: 3, title: '官網需求', state: 'internal_qa', stateCategory: 'in_progress', assignee: mine });

function changed(item: Item, state: string, stateCategory: Item['stateCategory'], updatedAt = CHANGED_AT): Item {
  return { ...item, state, stateCategory, updatedAt };
}

async function findRow(itemKey: string): Promise<HTMLElement> {
  return screen.findByRole('row', { name: new RegExp(itemKey) });
}

async function optionNames(user: ReturnType<typeof renderApp>['user'], select: HTMLElement): Promise<(string | null)[]> {
  const options = await openOptions(user, select);
  return options.map((option) => option.textContent);
}

/** 選項或按鈕前面的小圖裡的形狀（Select 把小圖包在第一個 aria-hidden 的 span 裡，Icon 帶 data-icon）。 */
function shapeOf(element: HTMLElement): string | null | undefined {
  const first = element.firstElementChild;
  const leading = first instanceof HTMLElement && first.tagName === 'SPAN' && first.getAttribute('aria-hidden') === 'true' ? first : null;
  return leading?.querySelector('[data-icon]')?.getAttribute('data-icon');
}

function rowKeysOf(tableName: string): (string | null | undefined)[] {
  return within(screen.getByRole('table', { name: tableName }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]?.textContent);
}

/** 多一個「已取消」類的狀態（測試的藍圖都沒有）。 */
const withCanceled: ProjectBlueprint = {
  ...simpleTasksBlueprint,
  workflows: simpleTasksBlueprint.workflows.map((workflow) => ({
    ...workflow,
    states: [...workflow.states, { key: 'dropped', name: '不做了', category: 'canceled' }],
  })),
};

describe('我的工作：狀態可以直接改', () => {
  it('每一列用自己專案的流程：選單只列那個專案走得到的狀態', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()], myWork: [smkTask, agyRequirement] });
    const { user } = renderApp('/my-work');
    const agyRow = await findRow('AGY-3');
    const smkRow = await findRow('SMK-1');

    // Act
    await user.click(await within(agyRow).findByRole('button', { name: '狀態：內部驗收' }));
    await user.click(await within(smkRow).findByRole('button', { name: '狀態：待辦' }));

    // Assert
    expect(await optionNames(user, within(smkRow).getByRole('combobox', { name: '狀態' }))).toEqual(['待辦（目前）', '進行中', '完成']);
    await user.click(await within(agyRow).findByRole('button', { name: '狀態：內部驗收' }));
    expect(await optionNames(user, within(agyRow).getByRole('combobox', { name: '狀態' }))).toEqual(['內部驗收（目前）', '開發', '客戶驗收']);
  });

  it('2 步改好、不換頁：點狀態、選新的狀態；那一列顯示新的狀態，焦點回到那一格', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkTask] });
    const requests = serveTransition(smkTask, changed(smkTask, 'doing', 'in_progress'));
    const { user, router } = renderApp('/my-work');
    const row = await findRow('SMK-1');

    // Act
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：進行中' })).toHaveFocus();
    expect(requests.map((request) => request.body)).toEqual([{ to: 'doing' }]);
    expect(currentPath(router)).toBe('/my-work');
    expect(row).not.toHaveAttribute('data-settled');
  });

  it('沒有改狀態權限（item.transition）的專案，那一列的狀態是唯讀文字', async () => {
    // Arrange
    signedInAs(exampleOwner, {
      details: [smkDetail({ myPermissions: ['item.read', 'item.update'] }), agyDetail()],
      myWork: [smkTask, agyRequirement],
    });

    // Act
    renderApp('/my-work');

    // Assert
    const agyRow = await findRow('AGY-3');
    expect(await within(agyRow).findByRole('button', { name: '狀態：內部驗收' })).toBeInTheDocument();
    const smkRow = await findRow('SMK-1');
    expect(await within(smkRow).findByText('待辦')).toBeInTheDocument();
    expect(within(smkRow).queryByRole('button', { name: /^狀態：/ })).not.toBeInTheDocument();
  });

  it('被角色擋下時說明照舊；「到「成員」調整角色」打開那一列的專案的成員', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()], myWork: [smkTask, agyRequirement] });
    failTransition(agyRequirement, 403, 'item.transition_role_required', '你的角色不能做這個轉換。', []);
    const { user } = renderApp('/my-work');
    const row = await findRow('AGY-3');
    await user.click(await within(row).findByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '客戶驗收');
    const alert = await within(row).findByRole('alert');

    // Act
    await user.click(within(alert).getByRole('button', { name: '到「成員」調整角色' }));

    // Assert
    expect(alert).toHaveTextContent('專案經理');
    expect(await screen.findByRole('dialog', { name: '成員' })).toBeInTheDocument();
  });

  it('狀態的選項前面是狀態類別的形狀（和狀態標籤同一組），按鈕上是格子原本的狀態標籤（同一個形狀）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ blueprint: withCanceled })], myWork: [smkTask] });
    const { user } = renderApp('/my-work');
    const row = await findRow('SMK-1');

    // Act
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    const select = await within(row).findByRole('combobox', { name: '狀態' });
    const options = await openOptions(user, select);

    // Assert
    expect(options.map((option) => [option.textContent, shapeOf(option)])).toEqual([
      ['待辦（目前）', 'stateNotStarted'],
      ['進行中', 'stateInProgress'],
      ['完成', 'stateDone'],
      ['不做了', 'stateCanceled'],
    ]);
    // 格子裡的按鈕是格子平常的樣子（docs/DESIGN.md 8.3「就地編輯」）：形狀在狀態標籤裡，不是選項的小圖。
    expect(select.querySelector('[data-icon]')?.getAttribute('data-icon')).toBe('stateNotStarted');
    expect(select).toHaveTextContent(/^待辦$/);
  });
});

describe('我的工作：改成完成或取消的項目留在原位', () => {
  it('改成完成：留在原位、劃掉，旁邊的 status 說重新整理後會離開清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkTask, smkUndated] });
    serveTransition(smkTask, changed(smkTask, 'done', 'done'));
    const { user } = renderApp('/my-work');
    const row = await findRow('SMK-1');

    // Act
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '完成');

    // Assert
    const status = await screen.findByText('SMK-1 已經完成，重新整理後會離開清單。');
    expect(status).toHaveAttribute('role', 'status');
    expect(await within(row).findByRole('button', { name: '狀態：完成' })).toHaveFocus();
    expect(row).toHaveAttribute('data-settled', 'true');
    expect(rowKeysOf('已過期')).toEqual(['SMK-1']);
    expect(screen.getByRole('heading', { level: 2, name: '已過期 1 項' })).toBeInTheDocument();
  });

  it('背景重抓（伺服器已經不給它）時照樣留著；離開再回來才不見', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkTask, smkUndated] });
    serveTransition(smkTask, changed(smkTask, 'done', 'done'));
    const { user, router, queryClient } = renderApp('/my-work');
    const row = await findRow('SMK-1');
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '完成');
    await screen.findByText('SMK-1 已經完成，重新整理後會離開清單。');
    server.use(http.get('/v1/items', () => ok({ items: [smkUndated], nextCursor: null })));

    // Act
    await queryClient.refetchQueries({ queryKey: ['my-work'] });

    // Assert
    expect(await findRow('SMK-1')).toHaveAttribute('data-settled', 'true');
    expect(screen.getByText('SMK-1 已經完成，重新整理後會離開清單。')).toBeInTheDocument();
    await router.navigate('/projects');
    await screen.findByRole('heading', { level: 1, name: '專案' });
    await router.navigate('/my-work');
    expect(await findRow('SMK-2')).toBeInTheDocument();
    expect(screen.queryByRole('row', { name: /SMK-1/ })).not.toBeInTheDocument();
    expect(screen.queryByText('SMK-1 已經完成，重新整理後會離開清單。')).not.toBeInTheDocument();
  });

  it('改成取消類的狀態：說「已經取消」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ blueprint: withCanceled })], myWork: [smkTask] });
    serveTransition(smkTask, changed(smkTask, 'dropped', 'canceled'));
    const { user } = renderApp('/my-work');
    const row = await findRow('SMK-1');

    // Act
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '不做了');

    // Assert
    expect(await screen.findByText('SMK-1 已經取消，重新整理後會離開清單。')).toHaveAttribute('role', 'status');
    expect(row).toHaveAttribute('data-settled', 'true');
  });

  it('改回還沒結束的狀態：不再劃掉，說明拿掉', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkTask] });
    const done = changed(smkTask, 'done', 'done');
    serveTransition(smkTask, done);
    const { user } = renderApp('/my-work');
    const row = await findRow('SMK-1');
    await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '完成');
    await screen.findByText('SMK-1 已經完成，重新整理後會離開清單。');
    serveTransition(smkTask, changed(smkTask, 'doing', 'in_progress', '2026-11-16T09:05:00Z'));

    // Act
    await user.click(await within(row).findByRole('button', { name: '狀態：完成' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(row).not.toHaveAttribute('data-settled');
    expect(screen.queryByText('SMK-1 已經完成，重新整理後會離開清單。')).not.toBeInTheDocument();
  });

  it('在項目面板裡改成完成，清單上那一列也跟著劃掉', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkTask] });
    serveItem(detailOf(smkTask, '設計稿的說明'));
    serveActivity(smkTask.key, []);
    serveItems('SMK', []);
    serveTransition(smkTask, changed(smkTask, 'done', 'done'));
    const { user } = renderApp('/my-work?item=SMK-1');
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    const fields = await within(panel).findByRole('region', { name: '欄位' });

    // Act
    await user.click(await within(fields).findByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(fields).getByRole('combobox', { name: '狀態' }), '完成');

    // Assert
    expect(await screen.findByText('SMK-1 已經完成，重新整理後會離開清單。')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('row', { name: /SMK-1/ })).toHaveAttribute('data-settled', 'true');
    });
  });
});
