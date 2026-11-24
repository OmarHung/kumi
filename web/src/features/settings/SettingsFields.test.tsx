import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agencyBlueprint, simpleTasksBlueprint } from '../../test/blueprints';
import { captureProposals, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

async function openSettings(detail: ProjectDetail) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail);
  const view = renderApp(`/projects/${detail.project.key}/settings/fields`);
  await screen.findByRole('heading', { level: 1, name: '設定' });
  const preview = async () => {
    await view.user.click(screen.getByRole('button', { name: '預覽變更' }));
    await waitFor(() => {
      expect(proposals).toHaveLength(1);
    });
    const [proposal] = proposals;
    if (proposal === undefined) throw new Error('沒有送出變更集。');
    return proposal.body;
  };
  return { ...view, proposals, preview };
}

function fieldRow(name: string): HTMLElement {
  const input = within(screen.getByRole('list', { name: '自訂欄位' })).getByRole('textbox', { name: `「${name}」的名稱` });
  const row = input.closest('li');
  if (row === null) throw new Error(`找不到欄位「${name}」那一列。`);
  return row;
}

describe('設定頁：欄位', () => {
  it('新增文字欄位：預設套用到每一種類型，送出的藍圖不帶 options 與 currency', async () => {
    // Arrange
    const { user, preview } = await openSettings(smkDetail());

    // Act
    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    const dialog = screen.getByRole('dialog', { name: '新增欄位' });
    await user.type(within(dialog).getByLabelText('名稱'), '客戶名稱');
    expect(within(dialog).getByLabelText('key')).toHaveValue('field_1');
    await user.clear(within(dialog).getByLabelText('key'));
    await user.type(within(dialog).getByLabelText('key'), 'client_name');
    expect(within(within(dialog).getByRole('group', { name: '套用到的類型' })).getByRole('checkbox', { name: '任務' })).toBeChecked();
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    const body = await preview();

    // Assert
    expect(body.blueprint.fields).toEqual([...simpleTasksBlueprint.fields, { key: 'client_name', name: '客戶名稱', type: 'text' }]);
    expect(body.blueprint.itemTypes[0]?.fields?.at(-1)).toBe('client_name');
    expect(body.migrations).toEqual({ moveStates: [], dropFields: [] });
  });

  it('新增單選欄位要有選項，key 有建議值；金額欄位要選幣別', async () => {
    // Arrange
    const { user, preview } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    let dialog = screen.getByRole('dialog', { name: '新增欄位' });
    await user.type(within(dialog).getByLabelText('名稱'), '嚴重度');
    await chooseOption(user, within(dialog).getByLabelText('型別'), '單選');
    await user.click(within(dialog).getByRole('button', { name: '拿掉第 1 個選項' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    expect(within(dialog).getByText('選項型的欄位至少要有一個選項。')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: '再加一個選項' }));
    await user.type(within(dialog).getByLabelText('第 1 個選項的名稱'), 'Minor');
    await user.click(within(dialog).getByRole('button', { name: '再加一個選項' }));
    await user.type(within(dialog).getByLabelText('第 2 個選項的名稱'), '嚴重');
    expect(within(dialog).getByLabelText('第 1 個選項的 key')).toHaveValue('minor');
    expect(within(dialog).getByLabelText('第 2 個選項的 key')).toHaveValue('option_1');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    dialog = screen.getByRole('dialog', { name: '新增欄位' });
    await user.type(within(dialog).getByLabelText('名稱'), '成本');
    await chooseOption(user, within(dialog).getByLabelText('型別'), '金額');
    await chooseOption(user, within(dialog).getByLabelText('幣別'), 'USD（美元）');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    const body = await preview();
    expect(body.blueprint.fields.slice(-2)).toEqual([
      { key: 'field_1', name: '嚴重度', type: 'single_select', options: [{ key: 'minor', name: 'Minor' }, { key: 'option_1', name: '嚴重' }] },
      { key: 'field_2', name: '成本', type: 'currency', currency: 'USD' },
    ]);
  });

  it('新增欄位的 key 不能用核心欄位的名字，也不能和既有的重複', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    const dialog = screen.getByRole('dialog', { name: '新增欄位' });
    await user.type(within(dialog).getByLabelText('名稱'), '標題');
    const key = within(dialog).getByLabelText('key');

    // Act
    await user.clear(key);
    await user.type(key, 'title');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    expect(key).toHaveAccessibleDescription(expect.stringContaining('「title」是內建欄位的名字，自訂欄位不能用。'));
    await user.clear(key);
    await user.type(key, 'notes');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    expect(key).toHaveAccessibleDescription(expect.stringContaining('已經有 key 是 notes 的欄位。'));
  });

  it('改名、改套用的類型：送出的類型 fields 只動到改了的那些', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());

    // Act
    const name = screen.getByRole('textbox', { name: '「報價金額」的名稱' });
    await user.clear(name);
    await user.type(name, '報價');
    const types = within(fieldRow('報價金額')).getByRole('group', { name: '「報價金額」套用到的類型' });
    await user.click(within(types).getByRole('checkbox', { name: '需求' }));
    await user.click(within(types).getByRole('checkbox', { name: '任務' }));
    const body = await preview();

    // Assert
    expect(body.blueprint.fields[0]).toEqual({ key: 'quote_amount', name: '報價', type: 'currency', currency: 'TWD' });
    expect(body.blueprint.itemTypes.map((type) => [type.key, type.fields])).toEqual([
      ['requirement', ['client_signoff_date']],
      ['change_request', ['change_reason', 'impact', 'quote_amount', 'client_signoff_date']],
      ['task', ['quote_amount']],
    ]);
  });

  it('選項：既有的可以改名、不能刪（停用並說明）；新加的可以拿掉', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    const options = screen.getByRole('list', { name: '「影響程度」的選項' });

    // Act
    const locked = within(options).getByRole('button', { name: '刪除選項「低」' });
    await user.click(locked);
    expect(locked).toHaveAttribute('aria-disabled', 'true');
    expect(locked).toHaveAccessibleDescription('既有的選項不能刪除：已經用到它的項目會對不到值。可以改名。');
    const low = within(options).getByRole('textbox', { name: '選項「低」的名稱' });
    await user.clear(low);
    await user.type(low, '輕微');
    await user.type(screen.getByLabelText('「影響程度」新選項的名稱'), '嚴重{Enter}');
    await user.type(screen.getByLabelText('「影響程度」新選項的名稱'), '暫定{Enter}');
    await user.click(within(options).getByRole('button', { name: '移除選項「option_2」' }));
    const body = await preview();

    // Assert
    expect(body.blueprint.fields.find((field) => field.key === 'impact')?.options).toEqual([
      { key: 'low', name: '輕微', color: 'gray' },
      { key: 'medium', name: '中', color: 'yellow' },
      { key: 'high', name: '高', color: 'red' },
      { key: 'option_1', name: '嚴重' },
    ]);
  });

  it('刪除欄位：說明值會封存與一起拿掉的引用，寫進 dropFields，也從類型與轉換的必填欄位拿掉', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());

    // Act
    await user.click(screen.getByRole('button', { name: '刪除「客戶驗收日」' }));

    // Assert
    const removed = screen.getByRole('list', { name: '要刪除的欄位' });
    expect(removed).toHaveTextContent('客戶驗收日');
    expect(removed).toHaveTextContent('要刪除');
    expect(removed).toHaveTextContent('套用後，項目裡這個欄位的值會封存起來，畫面上不再顯示。也會從轉換的必填欄位拿掉。');
    expect(within(removed).getByRole('button', { name: '復原「客戶驗收日」' })).toHaveFocus();
    const body = await preview();
    expect(body.migrations).toEqual({ moveStates: [], dropFields: ['client_signoff_date'] });
    expect(body.blueprint.fields.map((field) => field.key)).toEqual(['quote_amount', 'change_reason', 'impact']);
    expect(body.blueprint.itemTypes.map((type) => type.fields)).toEqual([['quote_amount'], ['change_reason', 'impact', 'quote_amount'], []]);
    expect(body.blueprint.workflows[0]?.transitions?.at(-1)).toEqual({
      from: 'client_qa',
      to: 'closed',
      allowedRoles: ['project_manager'],
      requireFields: [],
    });
    expect(body.blueprint.workflows[1]).toEqual(agencyBlueprint.workflows[1]);
  });

  it('復原刪除的欄位：回到原本的位置，沒有修改了', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '刪除「分類」' }));

    // Act
    await user.click(screen.getByRole('button', { name: '復原「分類」' }));

    // Assert
    expect(within(screen.getByRole('list', { name: '自訂欄位' })).getAllByRole('textbox')[0]).toHaveValue('分類');
    expect(screen.getByRole('button', { name: '刪除「分類」' })).toHaveFocus();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('剛新增的欄位刪掉就沒了，焦點回到「新增欄位」', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    await user.type(within(screen.getByRole('dialog', { name: '新增欄位' })).getByLabelText('名稱'), '暫時');
    await user.click(screen.getByRole('button', { name: '加入' }));
    expect(fieldRow('field_1')).toHaveTextContent('新增');

    // Act
    await user.click(screen.getByRole('button', { name: '刪除「field_1」' }));

    // Assert
    expect(screen.queryByRole('list', { name: '要刪除的欄位' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增欄位' })).toHaveFocus();
  });

  it('拿掉剛加的選項：焦點到相鄰那一列；一個都不剩時到新選項的名稱', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    const options = screen.getByRole('list', { name: '「影響程度」的選項' });
    await user.type(screen.getByLabelText('「影響程度」新選項的名稱'), '嚴重{Enter}');
    await user.type(screen.getByLabelText('「影響程度」新選項的名稱'), '暫定{Enter}');

    // Act
    await user.click(within(options).getByRole('button', { name: '移除選項「option_1」' }));

    // Assert
    expect(within(options).getByRole('button', { name: '移除選項「option_2」' })).toHaveFocus();
    await user.click(within(options).getByRole('button', { name: '移除選項「option_2」' }));
    expect(within(options).getByRole('button', { name: '刪除選項「高」' })).toHaveFocus();

    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    const dialog = screen.getByRole('dialog', { name: '新增欄位' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Severity');
    await chooseOption(user, within(dialog).getByLabelText('型別'), '單選');
    await user.type(within(dialog).getByLabelText('第 1 個選項的名稱'), 'Minor');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    await user.click(screen.getByRole('button', { name: '移除選項「minor」' }));
    expect(screen.getByLabelText('「severity」新選項的名稱')).toHaveFocus();
  });

  it('新增欄位的對話框：拿掉選項後焦點到相鄰那一列，都拿掉了到「再加一個選項」，焦點不會跑到背景', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '新增欄位' }));
    const dialog = screen.getByRole('dialog', { name: '新增欄位' });
    await chooseOption(user, within(dialog).getByLabelText('型別'), '單選');
    await user.click(within(dialog).getByRole('button', { name: '再加一個選項' }));
    await user.click(within(dialog).getByRole('button', { name: '再加一個選項' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '拿掉第 2 個選項' }));

    // Assert
    expect(within(dialog).getByRole('button', { name: '拿掉第 2 個選項' })).toHaveFocus();
    await user.click(within(dialog).getByRole('button', { name: '拿掉第 2 個選項' }));
    expect(within(dialog).getByRole('button', { name: '拿掉第 1 個選項' })).toHaveFocus();
    await user.click(within(dialog).getByRole('button', { name: '拿掉第 1 個選項' }));
    expect(within(dialog).getByRole('button', { name: '再加一個選項' })).toHaveFocus();
    await user.tab();
    expect(dialog).toContainElement(document.activeElement instanceof HTMLElement ? document.activeElement : null);
  });

  it('取消套用到某個類型、而那個類型的流程有轉換要求這個欄位時，在那一列先提醒', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    const row = fieldRow('客戶驗收日');
    const types = within(row).getByRole('group', { name: '「客戶驗收日」套用到的類型' });
    const reminder = '「需求變更單」用的「交付流程」有轉換要求先填這個欄位：取消套用之後，送出會被伺服器擋下。';

    // Act
    await user.click(within(types).getByRole('checkbox', { name: '需求變更單' }));

    // Assert
    expect(within(row).getByText(reminder)).toBeInTheDocument();
    expect(types).toHaveAccessibleDescription(reminder);
    await user.click(within(types).getByRole('checkbox', { name: '需求變更單' }));
    expect(within(row).queryByText(reminder)).not.toBeInTheDocument();
    expect(within(fieldRow('報價金額')).queryByText(/有轉換要求先填這個欄位/)).not.toBeInTheDocument();
  });
});
