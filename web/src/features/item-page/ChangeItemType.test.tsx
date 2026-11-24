import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Item, ItemDetail } from '../../api/types';
import { created, serveActivity, showAllActivity, typeChanged } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, ALL_PROJECT_PERMISSIONS, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, failChangeType, serveChangeType, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const requirement = detailOf(
  agyItem({
    number: 3,
    title: '官網需求',
    state: 'internal_qa',
    stateCategory: 'in_progress',
    fields: { quote_amount: 1200, client_signoff_date: '2026-11-20' },
  }),
);
const parent = detailOf(agyItem({ number: 1, title: '整體需求' }));
const childTask = agyItem({ number: 5, type: 'task', title: '首頁切版', state: 'doing', stateCategory: 'in_progress', parentKey: 'AGY-3' });

interface OpenOptions {
  readonly item?: ItemDetail;
  /** 下層清單的每一頁；多於一頁時對話框只知道第一頁。 */
  readonly children?: readonly (readonly Item[])[];
  readonly permissions?: typeof ALL_PROJECT_PERMISSIONS;
}

async function openItem({ item = requirement, children = [[]], permissions = ALL_PROJECT_PERMISSIONS }: OpenOptions = {}) {
  signedInAs(exampleOwner, { details: [agyDetail({ myPermissions: permissions })] });
  serveItem(item);
  serveItem(parent);
  serveItems('AGY', ...children);
  const timeline = serveActivity(item.key, [created()]);
  const view = renderApp(`/projects/AGY/items/${item.key}`);
  const fields = await screen.findByRole('region', { name: '欄位' });
  return { ...view, fields, timeline };
}

type User = ReturnType<typeof renderApp>['user'];

/** 「改類型」在頁列的「更多動作」選單裡（原本在欄位面板「類型」的旁邊）：先打開選單。 */
async function openActions(user: User) {
  await user.click(screen.getByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ }));
}

async function chooseType(user: User, typeName: string) {
  await openActions(user);
  await user.click(screen.getByRole('menuitem', { name: '改類型' }));
  const dialog = screen.getByRole('dialog', { name: /的類型$/ });
  await chooseOption(user, within(dialog).getByRole('combobox', { name: '新的類型' }), typeName);
  return dialog;
}

describe('項目頁：改類型', () => {
  it('選了新類型先說明狀態怎麼搬、哪些欄位的值會封存；送出後項目頁換成新的類型、狀態與欄位，時間軸多一筆', async () => {
    // Arrange
    const { user, fields, timeline } = await openItem();
    const dialog = await chooseType(user, '任務');
    const changed: ItemDetail = { ...requirement, type: 'task', state: 'doing', stateCategory: 'in_progress', fields: {} };
    const requests = serveChangeType(requirement, () => changed);
    const entry = typeChanged(
      'requirement',
      'task',
      [
        { field: 'quote_amount', from: 1200, to: null },
        { field: 'client_signoff_date', from: '2026-11-20', to: null },
      ],
      { fromState: 'internal_qa', toState: 'doing' },
    );

    // Act
    const preview = within(dialog).getByRole('region', { name: '改了之後' });
    const options = await openOptions(user, within(dialog).getByRole('combobox', { name: '新的類型' }));
    timeline.add(entry);
    await user.click(within(dialog).getByRole('button', { name: '改類型' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['需求變更單', '任務']);
    expect(preview).toHaveTextContent('狀態會從「內部驗收」搬到「進行中」：「任務流程」沒有「內部驗收」。');
    expect(preview).toHaveTextContent('這些欄位的值會封存起來，「任務」沒有這些欄位：');
    expect(within(within(preview).getByRole('list', { name: '會封存的值' })).getAllByRole('listitem').map((line) => line.textContent)).toEqual([
      '「報價金額」：$1,200',
      '「客戶驗收日」：2026/11/20',
    ]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(requests.map((request) => request.body)).toEqual([{ type: 'task' }]);
    expect(within(fields).getByText('任務')).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(within(fields).queryByText('報價金額')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'AGY-3 的更多動作' })).toHaveFocus();
    const section = screen.getByRole('region', { name: '活動與留言' });
    expect(
      await within(section).findByText(
        '把類型從「需求」改成「任務」，狀態從「內部驗收」搬到「進行中」；「報價金額」（原本是 $1,200）、「客戶驗收日」（原本是 2026/11/20）的值封存起來',
        { exact: false },
      ),
    ).toBeInTheDocument();
  });

  it('同一個流程：狀態留著，沒有欄位的值要封存', async () => {
    // Arrange
    const { user } = await openItem();

    // Act
    const dialog = await chooseType(user, '需求變更單');

    // Assert
    const preview = within(dialog).getByRole('region', { name: '改了之後' });
    expect(preview).toHaveTextContent('狀態維持「內部驗收」。');
    expect(preview).toHaveTextContent('沒有欄位的值要封存。');
    expect(within(dialog).getByRole('button', { name: '改類型' })).toBeEnabled();
  });

  it('還沒選類型時不能送出', async () => {
    // Arrange
    const { user } = await openItem();

    // Act
    await openActions(user);
    await user.click(screen.getByRole('menuitem', { name: '改類型' }));

    // Assert
    const dialog = screen.getByRole('dialog', { name: '改 AGY-3 的類型' });
    expect(dialog).toHaveTextContent('目前是「需求」。');
    expect(within(dialog).getByRole('button', { name: '改類型' })).toBeDisabled();
  });

  it('新類型不能有上層、而這個項目有上層：先說明會被擋下，不能送出', async () => {
    // Arrange
    const task = detailOf(agyItem({ number: 5, type: 'task', title: '首頁切版', state: 'todo', parentKey: 'AGY-1' }));
    const { user } = await openItem({ item: task });

    // Act
    const dialog = await chooseType(user, '需求');

    // Assert
    const reason = '「需求」不能掛在任何類型底下，這個項目目前在 AGY-1 底下：要先拿掉上層項目才能改。';
    expect(within(dialog).getByRole('region', { name: '改了之後' })).toHaveTextContent(reason);
    // 停用但停得到（和這一輪其他被擋下的按鈕一樣），原因連到按鈕上；按了不送出。
    const submit = within(dialog).getByRole('button', { name: '改類型' });
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(submit).toHaveAccessibleDescription(reason);
    await user.click(submit);
    expect(screen.getByRole('dialog', { name: '改 AGY-5 的類型' })).toBeInTheDocument();
  });

  it('已經知道的下層不能掛在新類型底下：列出它們，不能送出；換成允許的類型就可以', async () => {
    // Arrange
    const { user } = await openItem({ children: [[childTask]] });
    await screen.findByRole('list', { name: '下層項目' });

    // Act
    const dialog = await chooseType(user, '任務');
    const preview = within(dialog).getByRole('region', { name: '改了之後' });

    // Assert
    expect(preview).toHaveTextContent('這些下層項目不能掛在「任務」底下，要先移開或刪除它們才能改：');
    expect(within(preview).getByRole('link', { name: 'AGY-5 首頁切版' })).toHaveAttribute('href', '/projects/AGY/items/AGY-5');
    const submit = within(dialog).getByRole('button', { name: '改類型' });
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(submit).toHaveAccessibleDescription('這些下層項目不能掛在「任務」底下，要先移開或刪除它們才能改：');
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '新的類型' }), '需求變更單');
    expect(within(dialog).getByRole('button', { name: '改類型' })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: '改類型' })).not.toHaveAttribute('aria-disabled');
  });

  it('伺服器擋下（422）時在對話框裡說明；下層的問題列出是哪些', async () => {
    // Arrange
    const { user } = await openItem();
    const dialog = await chooseType(user, '需求變更單');
    failChangeType(requirement, 422, 'item.children_not_allowed', '有下層項目不能掛在新的類型底下。', [
      { code: 'item.children_not_allowed', path: 'children/AGY-8', message: '「任務」不能掛在「需求變更單」底下。' },
    ]);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '改類型' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('有下層項目不能掛在新的類型底下。');
    expect(within(dialog).getByRole('link', { name: 'AGY-8' })).toHaveAttribute('href', '/projects/AGY/items/AGY-8');
    expect(screen.getByRole('dialog', { name: '改 AGY-3 的類型' })).toBeInTheDocument();
  });

  it('伺服器擋下（422）時不管代碼是什麼，details 裡有下層的路徑就列出那些下層', async () => {
    // Arrange
    const { user } = await openItem();
    const dialog = await chooseType(user, '需求變更單');
    failChangeType(requirement, 422, 'item.invalid', '有些下層項目擋下了這次修改。', [
      { code: 'item.invalid', path: 'children/AGY-9', message: '「任務」不能掛在「需求變更單」底下。' },
      { code: 'item.invalid', path: 'type', message: '類型不對。' },
    ]);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '改類型' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('有些下層項目擋下了這次修改。');
    const listed = within(within(dialog).getByRole('list', { name: '下層項目' })).getAllByRole('link');
    expect(listed.map((link) => link.textContent)).toEqual(['AGY-9']);
  });

  it('從不限制轉換的流程改成有限制轉換的：說明回到起始狀態與原因', async () => {
    // Arrange
    const task = detailOf(agyItem({ number: 6, type: 'task', title: '首頁切版', state: 'doing', stateCategory: 'in_progress' }));
    const { user } = await openItem({ item: task });

    // Act
    const dialog = await chooseType(user, '需求');

    // Assert
    expect(within(dialog).getByRole('region', { name: '改了之後' })).toHaveTextContent(
      '狀態會從「進行中」回到起始狀態：「交付流程」有限制轉換，改類型之後從起始狀態「需求確認」開始，再照一般的方式改狀態。',
    );
  });

  it('沒有「改變狀態」的權限：說明會回到起始狀態', async () => {
    // Arrange
    const permissions = ALL_PROJECT_PERMISSIONS.filter((permission) => permission !== 'item.transition');
    const { user } = await openItem({ permissions });

    // Act
    const dialog = await chooseType(user, '任務');

    // Assert
    expect(within(dialog).getByRole('region', { name: '改了之後' })).toHaveTextContent(
      '狀態會從「內部驗收」回到起始狀態：你沒有「改變狀態」的權限，改類型之後從起始狀態「待辦」開始。',
    );
  });

  it('下層只載入了第一頁：預覽說只檢查了前 N 個，以伺服器為準', async () => {
    // Arrange
    const later = agyItem({ number: 9, type: 'task', title: '還沒載入的下層', state: 'todo', parentKey: 'AGY-3' });
    const { user } = await openItem({ children: [[childTask], [later]] });
    await screen.findByRole('list', { name: '下層項目' });

    // Act
    const dialog = await chooseType(user, '需求變更單');

    // Assert
    expect(within(dialog).getByRole('region', { name: '改了之後' })).toHaveTextContent('只檢查了前 1 個下層，送出後以伺服器為準。');
  });

  it('上層不允許（422 item.parent_not_allowed）顯示伺服器的訊息', async () => {
    // Arrange
    const { user } = await openItem();
    const dialog = await chooseType(user, '需求變更單');
    failChangeType(requirement, 422, 'item.parent_not_allowed', '「需求變更單」不能掛在這個上層底下。', [
      { code: 'item.parent_not_allowed', path: 'parent', message: '「需求變更單」不能掛在這個上層底下。' },
    ]);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '改類型' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('「需求變更單」不能掛在這個上層底下。');
    expect(within(dialog).queryByRole('list', { name: '下層項目' })).not.toBeInTheDocument();
  });

  it('沒有修改項目的權限時沒有「改類型」', async () => {
    // Arrange
    const permissions = ALL_PROJECT_PERMISSIONS.filter((permission) => permission !== 'item.update');
    const { user } = await openItem({ permissions });

    // Act
    await openActions(user);

    // Assert
    expect(screen.queryByRole('menuitem', { name: '改類型' })).not.toBeInTheDocument();
  });

  it('專案只有一種類型時沒有「改類型」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(detailOf(smkItem({ number: 7 })));
    serveActivity('SMK-7', []);
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('region', { name: '欄位' });

    // Act
    await openActions(user);

    // Assert
    expect(screen.queryByRole('menuitem', { name: '改類型' })).not.toBeInTheDocument();
  });
});

/** 打開「改類型」的對話框，還沒選類型。 */
async function openChangeType(user: User) {
  await openActions(user);
  await user.click(screen.getByRole('menuitem', { name: '改類型' }));
  const dialog = await screen.findByRole('dialog', { name: /的類型$/ });
  return { dialog, field: within(dialog).getByRole('combobox', { name: '新的類型' }) };
}

describe('改類型的選單：選了才變', () => {
  it('還沒選時按鈕上是灰字的「選擇類型」（不是選項）；用方向鍵移動反白不改，按 Enter 選了才換說明', async () => {
    // Arrange
    const { user } = await openItem();
    const { dialog, field } = await openChangeType(user);
    const preview = within(dialog).getByRole('region', { name: '改了之後' });
    expect(field).toHaveTextContent('選擇類型');

    // Act
    const options = await openOptions(user, field);
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['需求變更單', '任務']);
    expect(field).toHaveAttribute('aria-activedescendant', options[1]?.id);
    expect(field).toHaveTextContent('選擇類型');
    expect(preview).toBeEmptyDOMElement();
    expect(within(dialog).getByRole('button', { name: '改類型' })).toBeDisabled();
    await user.keyboard('{Enter}');
    expect(field).toHaveAttribute('aria-expanded', 'false');
    expect(field).toHaveTextContent('任務');
    expect(field).toHaveFocus();
    expect(preview).toHaveTextContent('狀態會從「內部驗收」搬到「進行中」：「任務流程」沒有「內部驗收」。');
    expect(within(dialog).getByRole('button', { name: '改類型' })).toBeEnabled();
  });

  it('在關著的選單上按 Enter 是打開清單，不是送出表單', async () => {
    // Arrange
    const { user } = await openItem();
    const dialog = await chooseType(user, '需求變更單');
    const requests = serveChangeType(requirement, () => ({ ...requirement, type: 'change_request' }));
    const field = within(dialog).getByRole('combobox', { name: '新的類型' });
    expect(field).toHaveFocus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(field).toHaveAttribute('aria-expanded', 'true');
    });
    expect(requests).toHaveLength(0);
    expect(screen.getByRole('dialog', { name: '改 AGY-3 的類型' })).toBeInTheDocument();
  });

  it('清單開著時 Esc 只關清單、值不變，對話框留著；再按一次才關對話框', async () => {
    // Arrange
    const { user } = await openItem();
    const dialog = await chooseType(user, '需求變更單');
    const field = within(dialog).getByRole('combobox', { name: '新的類型' });
    await openOptions(user, field);
    await user.keyboard('{ArrowDown}');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(field).toHaveAttribute('aria-expanded', 'false');
    expect(field).toHaveTextContent('需求變更單');
    expect(field).toHaveFocus();
    expect(screen.getByRole('dialog', { name: '改 AGY-3 的類型' })).toBe(dialog);
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'AGY-3 的更多動作' })).toHaveFocus();
  });
});

describe('項目面板裡的改類型', () => {
  const task = agyItem({ number: 6, type: 'task', title: '首頁切版', state: 'doing', stateCategory: 'in_progress' });

  it('清單開著時 Esc 只關清單：對話框與背後的面板都留著；選了只更新說明', async () => {
    // Arrange：任務沒有下層也沒有上層，面板裡不會多讀別的清單
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [task]);
    serveItem(detailOf(task));
    serveActivity(task.key, [created()]);
    const { user } = renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('link', { name: '首頁切版' }));
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-6' });
    await user.click(await within(panel).findByRole('button', { name: 'AGY-6 的更多動作' }));
    await user.click(screen.getByRole('menuitem', { name: '改類型' }));
    const dialog = await screen.findByRole('dialog', { name: '改 AGY-6 的類型' });
    const field = within(dialog).getByRole('combobox', { name: '新的類型' });
    await chooseOption(user, field, '需求');
    await openOptions(user, field);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(field).toHaveAttribute('aria-expanded', 'false');
    expect(field).toHaveTextContent('需求');
    expect(within(dialog).getByRole('region', { name: '改了之後' })).toHaveTextContent('狀態會從「進行中」回到起始狀態');
    expect(screen.getByRole('dialog', { name: '改 AGY-6 的類型' })).toBe(dialog);
    expect(screen.getByRole('complementary', { name: '項目 AGY-6' })).toBe(panel);
  });
});
