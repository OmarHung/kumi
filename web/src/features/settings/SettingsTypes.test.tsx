import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { signedInAs } from '../../test/api';
import type { ProjectBlueprint } from '../../api/blueprint';
import { agencyBlueprint } from '../../test/blueprints';
import { captureProposals, invalidChangeSet, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

async function openSettings(detail: ProjectDetail, respond?: Parameters<typeof captureProposals>[1]) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail, respond);
  const view = renderApp(`/projects/${detail.project.key}/settings/types`);
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

const [requirement, changeRequest, task] = agencyBlueprint.itemTypes;

/** 設定頁拆成區塊之後，切到另一個區塊（internal/HANDOFF-ui-ux-refactor.md 3A.4）。 */
async function showSection(user: ReturnType<typeof renderApp>['user'], name: string) {
  await user.click(within(screen.getByRole('navigation', { name: '設定的區塊' })).getByRole('link', { name }));
}

function moveSelect(type: string, state: string): HTMLElement {
  return screen.getByRole('combobox', { name: `類型「${type}」停在「${state}」的項目搬到` });
}

describe('設定頁：類型', () => {
  it('換流程：列出要搬的狀態並預先選好同類別的狀態；兩個類型共用同一條搬移，改一個另一個跟著變', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「需求」的流程' }), '任務流程');
    expect(moveSelect('需求', '需求確認')).toHaveTextContent('待辦');
    expect(moveSelect('需求', '開發')).toHaveTextContent('進行中');
    expect(moveSelect('需求', '結案')).toHaveTextContent('完成');
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「需求變更單」的流程' }), '任務流程');

    // Act
    await chooseOption(user, moveSelect('需求變更單', '結案'), '進行中');

    // Assert
    expect(moveSelect('需求', '結案')).toHaveTextContent('進行中');
    expect(screen.getAllByText('（和「需求變更單」共用）')).toHaveLength(7);
    expect(screen.getByText('還沒送出的修改：2 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.itemTypes).toEqual([{ ...requirement, workflow: 'task_flow' }, { ...changeRequest, workflow: 'task_flow' }, task]);
    expect(body.blueprint.itemTypes[2]).toEqual(task);
    expect(body.blueprint.workflows).toEqual(agencyBlueprint.workflows);
    expect(body.migrations).toEqual({
      moveStates: [
        { workflow: 'task_flow', from: 'confirming', to: 'todo' },
        { workflow: 'task_flow', from: 'quoting', to: 'todo' },
        { workflow: 'task_flow', from: 'designing', to: 'doing' },
        { workflow: 'task_flow', from: 'developing', to: 'doing' },
        { workflow: 'task_flow', from: 'internal_qa', to: 'doing' },
        { workflow: 'task_flow', from: 'client_qa', to: 'doing' },
        { workflow: 'task_flow', from: 'closed', to: 'doing' },
      ],
      dropFields: [],
    });
  });

  it('換回原本的流程：不用搬，沒有修改了', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    const workflow = screen.getByRole('combobox', { name: '類型「任務」的流程' });
    await chooseOption(user, workflow, '交付流程');
    expect(moveSelect('任務', '完成')).toHaveTextContent('結案');

    // Act
    await chooseOption(user, workflow, '任務流程');

    // Assert
    expect(screen.queryByRole('combobox', { name: '類型「任務」停在「完成」的項目搬到' })).not.toBeInTheDocument();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('新增類型：預設套用選定流程的轉換要求先填的欄位，新類型出現在欄位的「套用到」；送出的只多了它', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    await user.click(screen.getByRole('button', { name: '新增類型' }));
    const dialog = screen.getByRole('dialog', { name: '新增類型' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Bug');
    expect(within(dialog).getByLabelText('key')).toHaveValue('bug');
    const fields = within(dialog).getByRole('group', { name: '要套用的自訂欄位' });
    expect(within(fields).getByRole('checkbox', { name: '客戶驗收日' })).toBeChecked();
    expect(within(fields).getByRole('checkbox', { name: '報價金額' })).not.toBeChecked();
    await user.click(within(within(dialog).getByRole('group', { name: '可以掛在哪些類型底下（都不勾就不能有上層）' })).getByRole('checkbox', { name: '需求' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    await showSection(user, '欄位');
    expect(within(screen.getByRole('group', { name: '「客戶驗收日」套用到的類型' })).getByRole('checkbox', { name: 'Bug' })).toBeChecked();
    expect(within(screen.getByRole('group', { name: '「報價金額」套用到的類型' })).getByRole('checkbox', { name: 'Bug' })).not.toBeChecked();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint).toEqual({
      ...agencyBlueprint,
      itemTypes: [...agencyBlueprint.itemTypes, { key: 'bug', name: 'Bug', workflow: 'delivery', fields: ['client_signoff_date'], parents: ['requirement'] }],
    });
  });

  it('新增類型的 key：建議值避開用過的，自己填了重複的會說明', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    await user.click(screen.getByRole('button', { name: '新增類型' }));
    const dialog = screen.getByRole('dialog', { name: '新增類型' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Task');
    const key = within(dialog).getByLabelText('key');
    expect(key).toHaveValue('task_2');
    await user.clear(key);
    await user.type(key, 'task');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    expect(key).toHaveAccessibleDescription(expect.stringContaining('已經有 key 是 task 的類型。'));
  });

  it('改名與上層：只送改了的屬性', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    const name = screen.getByRole('textbox', { name: '類型「任務」的名稱' });
    await user.clear(name);
    await user.type(name, '工作');

    // Act
    await user.click(within(screen.getByRole('group', { name: '類型「任務」可以掛在哪些類型底下' })).getByRole('checkbox', { name: '需求變更單' }));

    // Assert
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.itemTypes).toEqual([requirement, changeRequest, { ...task, name: '工作', parents: ['requirement'] }]);
  });

  it('上層繞成一圈時先擋下，標在把圈閉合的那個類型', async () => {
    // Arrange
    const { user, proposals } = await openSettings(agyDetail());
    await user.click(within(screen.getByRole('group', { name: '類型「需求」可以掛在哪些類型底下' })).getByRole('checkbox', { name: '任務' }));

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 1 個地方要修正');
    expect(screen.getByRole('textbox', { name: '類型「任務」的名稱' })).toHaveAccessibleDescription('「任務」把「需求」當成上層，上層關係因此繞成一圈。');
    expect(proposals).toHaveLength(0);
  });

  it('伺服器的問題標在類型那一列；類型換流程的搬移問題標在那條搬移的選單上', async () => {
    // Arrange
    const details = [
      { code: 'item_type.unknown_parent', path: '/blueprint/itemTypes/2/parents/0', message: '類型「task」的上層類型不存在。' },
      { code: 'changeset.move_unknown_target', path: '/migrations/moveStates/0/to', message: '搬移的目的狀態不在新藍圖的流程裡。' },
    ];
    const { user } = await openSettings(agyDetail(), () => invalidChangeSet(details));
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「任務」的流程' }), '交付流程');
    await showSection(user, '欄位');
    await user.click(within(screen.getByRole('group', { name: '「客戶驗收日」套用到的類型' })).getByRole('checkbox', { name: '任務' }));
    await showSection(user, '類型');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器檢查後擋下了這份變更（2 個問題）');
    const name = screen.getByRole('textbox', { name: '類型「任務」的名稱' });
    expect(name).toHaveAccessibleDescription(details[0]?.message);
    expect(moveSelect('任務', '待辦')).toHaveAttribute('aria-invalid', 'true');
    expect(moveSelect('任務', '待辦')).toHaveAccessibleDescription(details[1]?.message);
    expect(screen.queryByRole('list', { name: '其他問題' })).not.toBeInTheDocument();
  });

  it('兩個類型共用一條搬移、舊狀態的類別不同：不預選；送出前要選，兩列的選單都標出問題', async () => {
    // Arrange
    const flow = (key: string, name: string, open: { readonly name: string; readonly category: 'not_started' | 'in_progress' }) => ({
      key,
      name,
      states: [{ key: 'open', ...open }, { key: 'end', name: '結束', category: 'done' as const }],
    });
    const blueprint: ProjectBlueprint = {
      ...agencyBlueprint,
      fields: [],
      workflows: [
        flow('alpha', '甲流程', { name: '開放', category: 'not_started' }),
        flow('beta', '乙流程', { name: '處理中', category: 'in_progress' }),
        { key: 'gamma', name: '新流程', states: [{ key: 'new', name: '新的', category: 'not_started' }, { key: 'working', name: '做', category: 'in_progress' }, { key: 'finish', name: '完成', category: 'done' }] },
      ],
      itemTypes: [
        { key: 'first', name: '第一種', workflow: 'alpha', fields: [], parents: [] },
        { key: 'second', name: '第二種', workflow: 'beta', fields: [], parents: [] },
      ],
      roles: [],
    };
    const { user, proposals } = await openSettings(agyDetail({ blueprint }));
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「第一種」的流程' }), '新流程');
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「第二種」的流程' }), '新流程');
    expect(moveSelect('第一種', '開放')).toHaveTextContent('選擇狀態');
    expect(moveSelect('第二種', '開放')).toHaveTextContent('選擇狀態');
    expect(moveSelect('第二種', '結束')).toHaveTextContent('完成');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 1 個地方要修正');
    expect(proposals).toHaveLength(0);
    for (const type of ['第一種', '第二種']) {
      expect(moveSelect(type, '開放')).toHaveAttribute('aria-invalid', 'true');
      expect(moveSelect(type, '開放')).toHaveAccessibleDescription('請選擇「第一種」停在「開放」的項目要搬到哪個狀態。');
    }
    expect(moveSelect('第一種', '結束')).not.toHaveAttribute('aria-invalid');
  });

  it('舊狀態在新流程裡被刪除了：類型那一列說明它照「刪除狀態」的搬移走，不是漏了', async () => {
    // Arrange
    const [delivery, taskFlow] = agencyBlueprint.workflows;
    if (delivery === undefined || taskFlow === undefined) throw new Error('沒有流程。');
    const developing = { key: 'developing', name: '開發中', category: 'in_progress' } as const;
    const blueprint: ProjectBlueprint = { ...agencyBlueprint, workflows: [delivery, { ...taskFlow, states: [...taskFlow.states.slice(0, 2), developing, ...taskFlow.states.slice(2)] }] };
    const { user } = await openSettings(agyDetail({ blueprint }));
    await showSection(user, '流程');
    await user.click(screen.getByRole('button', { name: '刪除「開發中」' }));
    await showSection(user, '類型');

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「需求」的流程' }), '任務流程');

    // Assert
    expect(screen.getByText('停在「開發」的項目照「任務流程」刪除「開發中」的搬移（還沒選要搬到哪裡）')).toBeInTheDocument();
    await showSection(user, '流程');
    await chooseOption(user, screen.getByRole('combobox', { name: '停在「開發中」的項目搬到' }), '進行中');
    await showSection(user, '類型');
    expect(screen.getByText('停在「開發」的項目照「任務流程」刪除「開發中」的搬移，搬到「進行中」')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: '類型「需求」停在「開發」的項目搬到' })).not.toBeInTheDocument();
  });

  it('換流程之後出現的搬移選單：一直掛著的區域告訴螢幕報讀器有幾個要確認', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    const row = within(screen.getByRole('list', { name: '類型' })).getAllByRole('listitem')[0];
    // 這個區域沒有角色與名稱（每個類型一個，不搶頁面的 status），只能用屬性找。
    const live = row?.querySelector('[aria-live="polite"]');
    expect(live).toBeEmptyDOMElement();

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「需求」的流程' }), '任務流程');

    // Assert
    const text = '換到「任務流程」之後，有 7 個狀態要選項目搬到哪裡（已經預選好，請確認）';
    expect(within(row ?? document.body).getByText(text)).toBe(live);
  });
});
