import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { failTransition, serveItems, serveTransition } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

const inQa = agyItem({ number: 3, title: '官網需求', state: 'internal_qa', stateCategory: 'in_progress' });
const inClientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress' });

async function openRow(projectKey: 'AGY' | 'SMK', item: Item) {
  signedInAs(exampleOwner, { details: [projectKey === 'AGY' ? agyDetail() : smkDetail()] });
  const listRequests = serveItems(projectKey, [item]);
  const view = renderApp(`/projects/${projectKey}`);
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: new RegExp(item.key) });
  return { ...view, row, listRequests };
}

async function optionNames(user: ReturnType<typeof renderApp>['user'], select: HTMLElement): Promise<(string | null)[]> {
  const options = await openOptions(user, select);
  return options.map((option) => option.textContent);
}

describe('改狀態', () => {
  it('選單只列從目前狀態走得到的狀態', async () => {
    // Arrange
    const { user, row } = await openRow('AGY', inQa);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：內部驗收' }));

    // Assert
    expect(await optionNames(user, within(row).getByRole('combobox', { name: '狀態' }))).toEqual([
      '內部驗收（目前）',
      '開發',
      '客戶驗收',
    ]);
  });

  it('流程沒有列轉換時，其他每個狀態都可以選', async () => {
    // Arrange
    const { user, row } = await openRow('SMK', smkItem({ number: 1 }));

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：待辦' }));

    // Assert
    expect(await optionNames(user, within(row).getByRole('combobox', { name: '狀態' }))).toEqual([
      '待辦（目前）',
      '進行中',
      '完成',
    ]);
  });

  it('選了就送出轉換，成功後顯示新的狀態，不重抓清單', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    const { user, row, listRequests } = await openRow('SMK', item);
    const requests = serveTransition(item, { ...item, state: 'done', stateCategory: 'done' });

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '完成');

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：完成' })).toHaveFocus();
    expect(requests.map((request) => request.body)).toEqual([{ to: 'done' }]);
    expect(listRequests).toHaveLength(1);
  });

  // 自訂控制項那一輪：方向鍵只移動清單裡的反白、不換值（docs/DESIGN.md 7.3），「方向鍵換了值要等 Enter」的區分不需要了。
  it('用方向鍵移動反白時不送出，按 Enter 選了才送', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    const { user, row } = await openRow('SMK', item);
    const requests = serveTransition(item, { ...item, state: 'doing', stateCategory: 'in_progress' });
    await user.click(within(row).getByRole('button', { name: '狀態：待辦' }));
    await within(row).findByRole('listbox', { name: '狀態' });

    // Act
    await user.keyboard('{ArrowDown}');
    await flushPending();
    const sentBeforeEnter = requests.length;
    await user.keyboard('{Enter}');

    // Assert
    expect(sentBeforeEnter).toBe(0);
    expect(await within(row).findByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(requests.map((request) => request.body)).toEqual([{ to: 'doing' }]);
  });

  it('流程沒有這條路時顯示後端的說明', async () => {
    // Arrange
    const { user, row } = await openRow('AGY', inQa);
    failTransition(inQa, 422, 'item.transition_not_allowed', '「內部驗收」不能直接轉到「客戶驗收」。', [
      { code: 'item.transition_not_allowed', path: 'state', message: '「內部驗收」不能直接轉到「客戶驗收」。' },
    ]);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '客戶驗收');

    // Assert
    expect(await within(row).findByRole('alert')).toHaveTextContent('「內部驗收」不能直接轉到「客戶驗收」。');
    expect(within(row).getByRole('combobox', { name: '狀態' })).toHaveTextContent('客戶驗收');
  });

  it('角色不對時說明需要哪些角色（用藍圖的名稱）、我現在的角色，並可以去調整', async () => {
    // Arrange
    const { user, row } = await openRow('AGY', inQa);
    failTransition(
      inQa,
      403,
      'item.transition_role_required',
      '從「內部驗收」轉到「客戶驗收」只有「專案經理」可以做，你在這個專案的角色是「管理員」。',
      [{ code: 'item.transition_role_required', path: 'role', message: '「專案經理」（project_manager）' }],
    );

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '客戶驗收');

    // Assert
    const alert = await within(row).findByRole('alert');
    expect(alert).toHaveTextContent('只有「專案經理」可以做');
    expect(within(alert).getByText('需要的角色').nextElementSibling).toHaveTextContent('專案經理');
    expect(within(alert).getByText('你的角色').nextElementSibling).toHaveTextContent('管理員');
    expect(within(alert).getByRole('button', { name: '到「成員」調整角色' })).toBeInTheDocument();
  });

  it('不能管理成員的人看不到「調整角色」', async () => {
    // Arrange
    const detail = agyDetail({ myPermissions: ['item.read', 'item.update', 'item.transition'] });
    signedInAs(exampleOwner, { details: [detail] });
    serveItems('AGY', [inQa]);
    failTransition(inQa, 403, 'item.transition_role_required', '只有「專案經理」可以做。', [
      { code: 'item.transition_role_required', path: 'role', message: '「專案經理」（project_manager）' },
    ]);
    const { user } = renderApp('/projects/AGY');
    const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /AGY-3/ });

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '客戶驗收');

    // Assert
    const alert = await within(row).findByRole('alert');
    expect(within(alert).queryByRole('button')).not.toBeInTheDocument();
  });

  // G 輪（U3）：缺欄位時不再只在格子裡列名稱，改成開對話框讓使用者當場補填（細節在 FillFields.test.tsx）。
  it('缺欄位時開對話框，列出要先填的欄位', async () => {
    // Arrange
    const { user, row } = await openRow('AGY', inClientQa);
    failTransition(inClientQa, 422, 'item.transition_fields_required', '要先填「客戶驗收日」才能進入「結案」。', [
      { code: 'item.transition_fields_required', path: 'client_signoff_date', message: '要先填「客戶驗收日」。' },
    ]);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：客戶驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '結案');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '移到「結案」之前要先填' });
    expect(within(dialog).getByLabelText('客戶驗收日')).toBeInTheDocument();
  });

  it('Esc 放棄這次改狀態，錯誤也一起收掉', async () => {
    // Arrange
    const { user, row } = await openRow('AGY', inQa);
    failTransition(inQa, 422, 'item.transition_not_allowed', '不能這樣轉。', []);
    await user.click(within(row).getByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '開發');
    await within(row).findByRole('alert');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(within(row).getByRole('button', { name: '狀態：內部驗收' })).toHaveFocus();
    expect(within(row).queryByRole('alert')).not.toBeInTheDocument();
  });
});
