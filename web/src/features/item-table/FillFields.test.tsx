import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, type JsonBodyType } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { agencyBlueprint } from '../../test/blueprints';
import { agyDetail, agyItem, daysFromToday, exampleMember, exampleOwner } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { flushPending } from '../../test/flush';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const inClientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress' });
const closed: Item = { ...inClientQa, state: 'closed', stateCategory: 'done', fields: { client_signoff_date: '2026-11-20' } };
const DIALOG = '移到「結案」之前要先填';
const SUBMIT = '填好並移到「結案」';

type Respond = () => Response;

function signoffRequired(paths: readonly string[] = ['client_signoff_date']): Respond {
  const details: JsonBodyType = paths.map((path) => ({
    code: 'item.transition_fields_required',
    path,
    message: `要先填「${path}」。`,
  }));
  return () => failure(422, 'item.transition_fields_required', '要先填「客戶驗收日」才能進入「結案」。', details);
}

const moved: Respond = () => ok(detailOf(closed));

interface Logged {
  readonly method: 'PATCH' | 'POST';
  readonly body: unknown;
}

/**
 * 這個項目的 PATCH 與改狀態照順序記下來（補填一定是先 PATCH 再改狀態）。
 * 改狀態照 transitions 的順序回應，用完之後一直回最後一個；PATCH 沒給 patch 時把內文的 fields 合併進項目回傳。
 */
function serveFill(item: Item, transitions: readonly Respond[], patch?: Respond): Logged[] {
  const log: Logged[] = [];
  let current = item;
  server.use(
    http.patch(`/v1/items/${item.key}`, async ({ request }) => {
      const body = (await request.json()) as { readonly fields?: Item['fields']; readonly assigneeId?: string };
      log.push({ method: 'PATCH', body });
      if (patch !== undefined) return patch();
      current = { ...current, fields: { ...current.fields, ...body.fields } };
      return ok(detailOf(current));
    }),
    http.post(`/v1/items/${item.key}/transitions`, async ({ request }) => {
      log.push({ method: 'POST', body: await request.json() });
      const index = log.filter((entry) => entry.method === 'POST').length - 1;
      const respond = transitions[Math.min(index, transitions.length - 1)] ?? moved;
      return respond();
    }),
  );
  return log;
}

async function openRow(detail = agyDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('AGY', [inClientQa]);
  const view = renderApp('/projects/AGY');
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: /AGY-4/ });
  return { ...view, row };
}

async function moveToClosed(user: ReturnType<typeof renderApp>['user'], scope: HTMLElement) {
  // 欄位區等週期與里程碑載入完才畫：先等欄位出現。
  await user.click(await within(scope).findByRole('button', { name: '狀態：客戶驗收' }));
  await chooseOption(user, within(scope).getByRole('combobox', { name: '狀態' }), '結案');
  return screen.findByRole('dialog', { name: DIALOG });
}

describe('補填再前進：表格', () => {
  it('缺欄位時開對話框，只列回應裡缺的欄位，焦點在第一個欄位；格子裡不再只是說明', async () => {
    // Arrange
    const { user, row } = await openRow();
    serveFill(inClientQa, [signoffRequired()]);

    // Act
    const dialog = await moveToClosed(user, row);

    // Assert
    const field = within(dialog).getByLabelText('客戶驗收日');
    await waitFor(() => {
      expect(field).toHaveFocus();
    });
    expect(within(dialog).getByText('客戶驗收日', { selector: 'label' })).toBeVisible();
    expect(within(dialog).queryByLabelText('報價金額')).not.toBeInTheDocument();
    expect(dialog).toHaveAccessibleDescription('填好之後接著把 AGY-4「驗收中的需求」移到「結案」。');
    expect(within(row).getByRole('button', { name: '狀態：客戶驗收' })).toBeInTheDocument();
    expect(within(row).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('按「填好並移到」：先 PATCH 欄位，成功才送改狀態；關上後焦點回到狀態格，結果唸得出來', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(), moved]);
    const dialog = await moveToClosed(user, row);

    // Act
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toHaveFocus();
    expect(log).toEqual([
      { method: 'POST', body: { to: 'closed' } },
      { method: 'PATCH', body: { fields: { client_signoff_date: '2026-11-20' } } },
      { method: 'POST', body: { to: 'closed' } },
    ]);
    expect(within(row).getByRole('status')).toHaveTextContent('已填好「客戶驗收日」，AGY-4 已移到「結案」。');
  });

  it('在欄位裡按 Enter 就送出；輸入法選字的 Enter 不算', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(), moved]);
    const dialog = await moveToClosed(user, row);
    const field = within(dialog).getByLabelText('客戶驗收日');
    await user.type(field, '2026-11-20');

    // Act
    fireEvent.keyDown(field, { key: 'Enter', isComposing: true });
    fireEvent.keyDown(field, { key: 'Enter', keyCode: 229 });
    const sentWhileComposing = log.length;
    await user.keyboard('{Enter}');

    // Assert
    expect(sentWhileComposing).toBe(1);
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log.map((entry) => entry.method)).toEqual(['POST', 'PATCH', 'POST']);
  });

  it('欄位存好了但改狀態被別的原因擋下：留在對話框說明，再按一次只重送改狀態', async () => {
    // Arrange
    const { user, row } = await openRow();
    const roleRequired: Respond = () =>
      failure(403, 'item.transition_role_required', '從「客戶驗收」轉到「結案」只有「專案經理」可以做。', [
        { code: 'item.transition_role_required', path: 'role', message: '「專案經理」（project_manager）' },
      ]);
    const log = serveFill(inClientQa, [signoffRequired(), roleRequired, moved]);
    const dialog = await moveToClosed(user, row);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert：說明原因，填的值還在
    const alert = await within(dialog).findByRole('alert');
    await waitFor(() => {
      expect(alert).toHaveTextContent('欄位已經存好，但還沒有移到「結案」。');
    });
    expect(alert).toHaveTextContent('只有「專案經理」可以做');
    expect(alert).toHaveTextContent('不用重填');
    // 日期欄位離開之後排成 2026/11/20（components/DatePicker，規範 7.3「日期」）。
    expect(within(dialog).getByLabelText('客戶驗收日')).toHaveValue('2026/11/20');
    expect(within(alert).getByRole('button', { name: '到「成員」調整角色' })).toBeInTheDocument();

    // Act：再按一次
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log.map((entry) => entry.method)).toEqual(['POST', 'PATCH', 'POST', 'POST']);
  });

  it('說明裡的「到『成員』調整角色」：先關上補填的對話框再開成員，不疊兩層', async () => {
    // Arrange
    const { user, row } = await openRow();
    const roleRequired: Respond = () =>
      failure(403, 'item.transition_role_required', '只有「專案經理」可以做。', [
        { code: 'item.transition_role_required', path: 'role', message: '「專案經理」（project_manager）' },
      ]);
    serveFill(inClientQa, [signoffRequired(), roleRequired]);
    const dialog = await moveToClosed(user, row);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));
    const members = await within(dialog).findByRole('button', { name: '到「成員」調整角色' });

    // Act
    await user.click(members);

    // Assert
    expect(await screen.findByRole('dialog', { name: '成員' })).toBeInTheDocument();
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
  });

  it('欄位存不進去：留在對話框，錯誤連到欄位，輸入的值還在，不送改狀態', async () => {
    // Arrange
    const { user, row } = await openRow();
    const rejected: Respond = () =>
      failure(422, 'validation.failed', '欄位的值不對。', [
        { code: 'field.invalid_date', path: 'client_signoff_date', message: '客戶驗收日不能早於開始日。' },
      ]);
    const log = serveFill(inClientQa, [signoffRequired()], rejected);
    const dialog = await moveToClosed(user, row);
    const field = within(dialog).getByLabelText('客戶驗收日');
    await user.type(field, '2026-11-20');

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    await waitFor(() => {
      expect(field).toHaveAccessibleDescription('客戶驗收日不能早於開始日。');
    });
    expect(field).toHaveAttribute('aria-invalid', 'true');
    // 日期欄位離開之後排成 2026/11/20（components/DatePicker，規範 7.3「日期」）。
    expect(field).toHaveValue('2026/11/20');
    expect(within(dialog).getByRole('alert')).toHaveTextContent('欄位沒有存好，還沒有移到「結案」。');
    expect(log.map((entry) => entry.method)).toEqual(['POST', 'PATCH']);
  });

  it('還沒填就按：說要先填，焦點到那個欄位，不送出', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired()]);
    const dialog = await moveToClosed(user, row);

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    const field = within(dialog).getByLabelText('客戶驗收日');
    await waitFor(() => {
      expect(field).toHaveFocus();
    });
    expect(field).toHaveAccessibleDescription('請填「客戶驗收日」。');
    expect(log).toHaveLength(1);
  });

  it('送出中：主要按鈕寫「送出中…」並標 aria-disabled，焦點留在它身上', async () => {
    // Arrange
    const { user, row } = await openRow();
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    serveFill(inClientQa, [signoffRequired(), moved]);
    const dialog = await moveToClosed(user, row);
    server.use(
      http.patch(`/v1/items/${inClientQa.key}`, async () => {
        await held;
        return ok(detailOf({ ...inClientQa, fields: { client_signoff_date: '2026-11-20' } }));
      }),
    );
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    const sending = await within(dialog).findByRole('button', { name: '送出中…' });
    expect(sending).toHaveAttribute('aria-disabled', 'true');
    expect(sending).toHaveFocus();
    release();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('Esc 關上對話框：什麼都不送，焦點回到狀態格（日期的日曆開著時第一次 Esc 只關日曆）', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired()]);
    const dialog = await moveToClosed(user, row);
    // user.type 先點輸入框：日曆打開了（規範 7.3「日期」）。
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: DIALOG })).toBe(dialog);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(row).getByRole('button', { name: '狀態：客戶驗收' })).toHaveFocus();
    expect(log).toHaveLength(1);
  });

  it('好幾個欄位一次填：用表格的編輯器（下拉選了不會馬上送），合成一個 PATCH', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(['assignee', 'client_signoff_date']), moved]);
    const dialog = await moveToClosed(user, row);

    // Act
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '負責人' }), exampleMember.personId);
    const sentAfterPick = log.length;
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(sentAfterPick).toBe(1);
    expect(log[1]).toEqual({
      method: 'PATCH',
      body: { assigneeId: exampleMember.personId, fields: { client_signoff_date: '2026-11-20' } },
    });
    expect(within(row).getByRole('status')).toHaveTextContent('已填好「負責人」、「客戶驗收日」，AGY-4 已移到「結案」。');
  });

  it('沒有修改項目的權限：照舊說明缺哪些欄位、請有權限的人填，不開對話框', async () => {
    // Arrange
    const { user, row } = await openRow(agyDetail({ myPermissions: ['item.read', 'item.transition'] }));
    serveFill(inClientQa, [signoffRequired()]);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：客戶驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '結案');

    // Assert
    const alert = await within(row).findByRole('alert');
    expect(within(alert).getByText('要先填的欄位').nextElementSibling).toHaveTextContent('客戶驗收日');
    expect(alert).toHaveTextContent('請有權限修改項目的人先填好');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('每種欄位都用表格的編輯器（核心欄位、說明、勾選、多選），一次存好', async () => {
    // Arrange
    const blueprint = {
      ...agencyBlueprint,
      fields: [
        ...agencyBlueprint.fields,
        { key: 'signed', name: '已簽約', type: 'checkbox' as const },
        {
          key: 'channels',
          name: '通路',
          type: 'multi_select' as const,
          options: [
            { key: 'web', name: '網站' },
            { key: 'app', name: 'App' },
          ],
        },
      ],
    };
    const { user, row } = await openRow(agyDetail({ blueprint }));
    const paths = ['priority', 'start_date', 'due_date', 'estimate', 'description', 'signed', 'channels'];
    const log = serveFill(inClientQa, [signoffRequired(paths), moved]);
    const dialog = await moveToClosed(user, row);

    // Act
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '優先度' }), 'high');
    await user.type(within(dialog).getByLabelText('開始日'), '2026-11-01');
    await user.type(within(dialog).getByLabelText('到期日'), '2026-12-01');
    await user.type(within(dialog).getByRole('textbox', { name: '估計' }), '3');
    await user.type(within(dialog).getByRole('textbox', { name: '說明' }), '客戶要求的修改');
    await user.click(within(dialog).getByRole('checkbox', { name: '已簽約' }));
    await user.click(within(within(dialog).getByRole('group', { name: '通路' })).getByRole('checkbox', { name: '網站' }));
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log[1]).toEqual({
      method: 'PATCH',
      body: {
        priority: 'high',
        startDate: '2026-11-01',
        dueDate: '2026-12-01',
        estimate: 3,
        description: '客戶要求的修改',
        fields: { signed: true, channels: ['web'] },
      },
    });
  });

  it('又被要求多填一個欄位：清單換成新缺的欄位，存好的不重填；數字打錯照編輯器的說明', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(['client_signoff_date']), signoffRequired(['quote_amount']), moved]);
    const dialog = await moveToClosed(user, row);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));
    const amount = await within(dialog).findByRole('textbox', { name: '報價金額' });

    // Act：數字打錯
    await user.type(amount, '一萬');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    await waitFor(() => {
      expect(amount).toHaveAccessibleDescription('請輸入數字。');
    });
    expect(within(dialog).getByRole('alert')).toHaveTextContent('欄位已經存好，但還沒有移到「結案」。');

    // Act：改好再送
    await user.clear(amount);
    await user.type(amount, '12,000');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log.map((entry) => entry.body)).toEqual([
      { to: 'closed' },
      { fields: { client_signoff_date: '2026-11-20' } },
      { to: 'closed' },
      { fields: { quote_amount: 12000 } },
      { to: 'closed' },
    ]);
  });

  it('缺的欄位有一個對不到編輯器（例如這個類型不能有上層項目）：照舊只說明缺什麼', async () => {
    // Arrange
    const { user, row } = await openRow();
    serveFill(inClientQa, [signoffRequired(['client_signoff_date', 'parent'])]);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：客戶驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '結案');

    // Assert
    const alert = await within(row).findByRole('alert');
    expect(within(alert).getByText('要先填的欄位').nextElementSibling).toHaveTextContent('客戶驗收日、上層項目');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('流程沒有這條路：照舊在格子裡說明，不開對話框', async () => {
    // Arrange
    const { user, row } = await openRow();
    serveFill(inClientQa, [() => failure(422, 'item.transition_not_allowed', '「客戶驗收」不能直接轉到「結案」。')]);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：客戶驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '結案');

    // Assert
    expect(await within(row).findByRole('alert')).toHaveTextContent('「客戶驗收」不能直接轉到「結案」。');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('補填再前進：項目頁與面板', () => {
  function serveItemPage() {
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [inClientQa]);
    serveItem(detailOf(inClientQa));
    serveActivity(inClientQa.key, [created()]);
    return serveFill(inClientQa, [signoffRequired(), moved]);
  }

  it('整頁的狀態欄位走同一個對話框，關上後焦點回到狀態欄位', async () => {
    // Arrange
    const log = serveItemPage();
    const { user } = renderApp('/projects/AGY/items/AGY-4');
    const fields = await screen.findByRole('region', { name: '欄位' });
    const dialog = await moveToClosed(user, fields);

    // Act
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(fields).findByRole('button', { name: '狀態：結案' })).toHaveFocus();
    expect(log.map((entry) => entry.method)).toEqual(['POST', 'PATCH', 'POST']);
  });

  it('面板裡改狀態也是：Esc 先關對話框，面板還開著，焦點回到面板的狀態欄位', async () => {
    // Arrange
    serveItemPage();
    const { user } = renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('link', { name: '驗收中的需求' }));
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-4' });
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    await moveToClosed(user, fields);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('complementary', { name: '項目 AGY-4' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '狀態：客戶驗收' })).toHaveFocus();
  });
});

describe('補填再前進：對話框裡的編輯器', () => {
  const blueprint = {
    ...agencyBlueprint,
    fields: [
      ...agencyBlueprint.fields,
      { key: 'signed', name: '已簽約', type: 'checkbox' as const },
      {
        key: 'channels',
        name: '通路',
        type: 'multi_select' as const,
        options: [
          { key: 'web', name: '網站' },
          { key: 'app', name: 'App' },
        ],
      },
    ],
  };

  it('多選用看得到的 legend 當標籤；第一個沒填的是多選時，焦點到它的第一個勾選框，說明連到這一組', async () => {
    // Arrange
    const { user, row } = await openRow(agyDetail({ blueprint }));
    const log = serveFill(inClientQa, [signoffRequired(['channels', 'client_signoff_date'])]);
    const dialog = await moveToClosed(user, row);
    const group = within(dialog).getByRole('group', { name: '通路' });

    // Act
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    const web = within(group).getByRole('checkbox', { name: '網站' });
    await waitFor(() => {
      expect(web).toHaveFocus();
    });
    expect(group).toHaveAccessibleDescription('請填「通路」。');
    expect(within(dialog).getByText('通路', { selector: 'legend' })).toBeVisible();
    expect(within(dialog).queryByText('通路', { selector: 'label' })).not.toBeInTheDocument();
    expect(log).toHaveLength(1);
  });

  it('勾選欄位和 CheckboxField 一樣：勾選框加文字，點文字也勾得到', async () => {
    // Arrange
    const { user, row } = await openRow(agyDetail({ blueprint }));
    const log = serveFill(inClientQa, [signoffRequired(['signed']), moved]);
    const dialog = await moveToClosed(user, row);

    // Act
    await user.click(within(dialog).getByText('已簽約'));

    // Assert
    const checkbox = within(dialog).getByRole('checkbox', { name: '已簽約' });
    expect(checkbox).toBeChecked();
    expect(checkbox.closest('label')).toHaveTextContent('已簽約');
    expect(within(dialog).getAllByText('已簽約')).toHaveLength(1);
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log[1]).toEqual({ method: 'PATCH', body: { fields: { signed: true } } });
  });

  it('送出中下拉與多選不收新的值，送出的是按下時的內容', async () => {
    // Arrange
    const { user, row } = await openRow(agyDetail({ blueprint }));
    const log = serveFill(inClientQa, [signoffRequired(['assignee', 'channels']), moved]);
    const dialog = await moveToClosed(user, row);
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.patch(`/v1/items/${inClientQa.key}`, async ({ request }) => {
        log.push({ method: 'PATCH', body: await request.json() });
        await held;
        return ok(detailOf(inClientQa));
      }),
    );
    const assignee = within(dialog).getByRole('combobox', { name: '負責人' });
    const group = within(dialog).getByRole('group', { name: '通路' });
    await chooseOption(user, assignee, exampleMember.personId);
    await user.click(within(group).getByRole('checkbox', { name: '網站' }));
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));
    await within(dialog).findByRole('button', { name: '送出中…' });

    // Act
    await chooseOption(user, assignee, exampleOwner.personId);
    await user.click(within(group).getByRole('checkbox', { name: 'App' }));

    // Assert
    expect(assignee).toHaveTextContent(exampleMember.displayName);
    expect(within(group).getByRole('checkbox', { name: 'App' })).not.toBeChecked();
    release();
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log[1]).toEqual({ method: 'PATCH', body: { assigneeId: exampleMember.personId, fields: { channels: ['web'] } } });
  });

  it('又因缺欄位被擋下：不說「只會重送改狀態」，焦點到新清單的第一個欄位', async () => {
    // Arrange
    const { user, row } = await openRow();
    serveFill(inClientQa, [signoffRequired(['client_signoff_date']), signoffRequired(['quote_amount']), moved]);
    const dialog = await moveToClosed(user, row);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    const amount = await within(dialog).findByRole('textbox', { name: '報價金額' });
    await waitFor(() => {
      expect(amount).toHaveFocus();
    });
    const alert = within(dialog).getByRole('alert');
    expect(alert).toHaveTextContent('欄位已經存好，但還沒有移到「結案」。');
    expect(alert).not.toHaveTextContent('只會重送改狀態');
  });

  it('後端說剛存好的欄位還是空的：那一格清空重填，再按一次會重送 PATCH', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(), signoffRequired(), moved]);
    const dialog = await moveToClosed(user, row);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Act
    const field = within(dialog).getByLabelText('客戶驗收日');
    await waitFor(() => {
      expect(within(dialog).getByLabelText('客戶驗收日')).toHaveValue('');
    });
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-21');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(field).not.toBeInTheDocument();
    expect(log.map((entry) => entry.method)).toEqual(['POST', 'PATCH', 'POST', 'PATCH', 'POST']);
    expect(log[3]?.body).toEqual({ fields: { client_signoff_date: '2026-11-21' } });
  });

  it('長文字：提示寫 Ctrl+Enter（Mac 是 ⌘+Enter）送出，按了就送', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(['description']), moved]);
    const dialog = await moveToClosed(user, row);
    const text = within(dialog).getByRole('textbox', { name: '說明' });
    expect(text).toHaveAccessibleDescription('Ctrl+Enter（Mac 是 ⌘+Enter）送出');
    expect(within(dialog).queryByText(/Esc 取消/)).not.toBeInTheDocument();

    // Act
    await user.type(text, '客戶要求的修改');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log[1]).toEqual({ method: 'PATCH', body: { description: '客戶要求的修改' } });
  });

  it('日期：點輸入框就打開日曆；從日曆選了只是填好，按「填好並移到」才送', async () => {
    // Arrange
    const { user, row } = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(), moved]);
    const dialog = await moveToClosed(user, row);
    const field = within(dialog).getByRole('combobox', { name: '客戶驗收日' });
    expect(field).toHaveAttribute('aria-expanded', 'false');

    // Act
    await user.click(field);
    // 「今天」和點某一天一樣是明確的選擇；日曆從今天的那個月開始，用它就不必固定今天。
    await user.click(within(await screen.findByRole('dialog', { name: '選擇日期' })).getByRole('button', { name: '今天' }));
    await flushPending();

    // Assert
    expect(field).toHaveValue(daysFromToday(0).replaceAll('-', '/'));
    expect(screen.getByRole('dialog', { name: DIALOG })).toBe(dialog);
    expect(log).toEqual([{ method: 'POST', body: { to: 'closed' } }]);

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：結案' })).toBeInTheDocument();
    expect(log[1]).toEqual({ method: 'PATCH', body: { fields: { client_signoff_date: daysFromToday(0) } } });
  });
});

describe('補填對話框裡的下拉（placement 是 dialog）', () => {
  async function openAssigneeInDialog() {
    const view = await openRow();
    const log = serveFill(inClientQa, [signoffRequired(['assignee'])]);
    const dialog = await moveToClosed(view.user, view.row);
    const assignee = within(dialog).getByRole('combobox', { name: '負責人' });
    return { ...view, log, dialog, assignee };
  }

  it('對話框裡是一般外框的下拉（和 SelectField 一樣），不是格子裡的 bare', async () => {
    // Arrange & Act
    const { assignee } = await openAssigneeInDialog();

    // Assert
    expect(assignee).toHaveAttribute('data-variant', 'default');
  });

  it('清單開著時 Esc 只關清單：對話框還在，焦點留在下拉，什麼都不送', async () => {
    // Arrange
    const { user, dialog, assignee, log } = await openAssigneeInDialog();
    await openOptions(user, assignee);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await flushPending();
    expect(assignee).toHaveAttribute('aria-expanded', 'false');
    expect(dialog).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: DIALOG })).toBe(dialog);
    expect(assignee).toHaveFocus();
    expect(log).toHaveLength(1);
  });

  it('清單關著時 Esc 交給對話框：關上對話框，焦點回到狀態格', async () => {
    // Arrange
    const { user, row, assignee, log } = await openAssigneeInDialog();
    act(() => {
      assignee.focus();
    });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(row).getByRole('button', { name: '狀態：客戶驗收' })).toHaveFocus();
    expect(log).toHaveLength(1);
  });

  it('在關著的下拉上按 Enter：打開清單，不送出對話框', async () => {
    // Arrange
    const { user, dialog, assignee, log } = await openAssigneeInDialog();
    act(() => {
      assignee.focus();
    });

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(dialog).findByRole('listbox', { name: '負責人' })).toBeInTheDocument();
    await flushPending();
    expect(log).toHaveLength(1);
    expect(within(dialog).getByRole('button', { name: SUBMIT })).not.toHaveAttribute('aria-disabled');
  });
});
