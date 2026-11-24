import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agencyBlueprint, simpleTasksBlueprint } from '../../test/blueprints';
import { captureProposals, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

async function openSettings(detail: ProjectDetail) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail);
  const view = renderApp(`/projects/${detail.project.key}/settings`);
  await screen.findByRole('heading', { level: 1, name: '設定' });
  return { ...view, proposals };
}

function statesList(name: string): HTMLElement {
  return screen.getByRole('list', { name: `「${name}」的狀態` });
}

function rowTexts(list: HTMLElement): string[] {
  return within(list).getAllByRole('listitem').map((row) => row.textContent);
}

describe('設定頁：狀態', () => {
  it('新增狀態：預設放在完成之前，送出的藍圖帶著它，成功後換到這份變更集的預覽頁', async () => {
    // Arrange
    const { user, router, proposals } = await openSettings(smkDetail());

    // Act
    await user.click(screen.getByRole('button', { name: '在「任務流程」新增狀態' }));
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Code Review');
    expect(within(dialog).getByLabelText('key')).toHaveValue('code_review');
    await chooseOption(user, within(dialog).getByLabelText('類別'), '進行中');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    await user.type(screen.getByLabelText('摘要'), '加一個審查的狀態');
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/changesets/7');
    });
    expect(proposals).toHaveLength(1);
    expect(proposals[0]?.contentType).toBe('application/json');
    expect(proposals[0]?.body).toEqual({
      baseVersion: 1,
      summary: '加一個審查的狀態',
      blueprint: {
        ...simpleTasksBlueprint,
        workflows: [
          {
            key: 'task_flow',
            name: '任務流程',
            states: [
              { key: 'todo', name: '待辦', category: 'not_started' },
              { key: 'doing', name: '進行中', category: 'in_progress' },
              { key: 'code_review', name: 'Code Review', category: 'in_progress' },
              { key: 'done', name: '完成', category: 'done' },
            ],
          },
        ],
      },
      migrations: { moveStates: [], dropFields: [] },
    });
  });

  it('改名稱、改類別、上移：每一列標出還沒送出的修改，送出的狀態照新的順序', async () => {
    // Arrange
    const { user, proposals } = await openSettings(smkDetail());

    // Act
    const name = screen.getByRole('textbox', { name: '「待辦」的名稱' });
    await user.clear(name);
    await user.type(name, '待處理');
    await chooseOption(user, screen.getByRole('combobox', { name: '「進行中」的類別' }), '待規劃');
    const up = screen.getByRole('button', { name: '把「完成」往上移' });
    await user.click(up);

    // Assert
    expect(rowTexts(statesList('任務流程'))).toEqual([
      expect.stringMatching(/todo.*已修改/),
      expect.stringMatching(/done.*已移動/),
      expect.stringMatching(/doing.*已修改/),
    ]);
    expect(screen.getByText('還沒送出的修改：3 項')).toBeInTheDocument();
    expect(up).toHaveFocus();
    await user.click(screen.getByRole('button', { name: '預覽變更' }));
    await waitFor(() => {
      expect(proposals).toHaveLength(1);
    });
    expect(proposals[0]?.body.blueprint.workflows[0]?.states).toEqual([
      { key: 'todo', name: '待處理', category: 'not_started' },
      { key: 'done', name: '完成', category: 'done' },
      { key: 'doing', name: '進行中', category: 'backlog' },
    ]);
    expect(proposals[0]?.body).not.toHaveProperty('summary');
  });

  it('名稱前後的空白送出時去掉；只差空白不算修改', async () => {
    // Arrange
    const { user, proposals } = await openSettings(smkDetail());
    const name = screen.getByRole('textbox', { name: '「待辦」的名稱' });

    // Act
    await user.type(name, '  ');

    // Assert
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(rowTexts(statesList('任務流程'))[0]).not.toMatch(/已修改/);
    expect(screen.getByRole('button', { name: '預覽變更' })).toBeDisabled();
    await user.clear(name);
    await user.type(name, '  待處理 ');
    await user.click(screen.getByRole('button', { name: '預覽變更' }));
    await waitFor(() => {
      expect(proposals).toHaveLength(1);
    });
    expect(proposals[0]?.body.blueprint.workflows[0]?.states[0]).toEqual({ key: 'todo', name: '待處理', category: 'not_started' });
  });

  it('刪除狀態：換到「要刪除」那一列，焦點到搬移的選單；沒選目標不送出，選了寫進 moveStates', async () => {
    // Arrange
    const { user, proposals } = await openSettings(smkDetail());

    // Act
    await user.click(screen.getByRole('button', { name: '刪除「進行中」' }));
    const target = screen.getByRole('combobox', { name: '停在「進行中」的項目搬到' });
    expect(target).toHaveFocus();
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 1 個地方要修正');
    expect(target).toHaveAccessibleDescription('請選擇停在「進行中」的項目要搬到哪個狀態。');
    expect(proposals).toHaveLength(0);
    const removed = screen.getByRole('list', { name: '「任務流程」要刪除的狀態' });
    expect(removed).toHaveTextContent('要刪除');
    // 還沒選時按鈕上是灰字的「選擇狀態」（placeholder），不是清單裡的選項。
    expect(target).toHaveTextContent('選擇狀態');
    expect((await openOptions(user, target)).map((option) => option.textContent)).toEqual(['待辦', '完成']);

    await chooseOption(user, target, '待辦');
    await user.click(screen.getByRole('button', { name: '預覽變更' }));
    await waitFor(() => {
      expect(proposals).toHaveLength(1);
    });
    expect(proposals[0]?.body.migrations).toEqual({
      moveStates: [{ workflow: 'task_flow', from: 'doing', to: 'todo' }],
      dropFields: [],
    });
    expect(proposals[0]?.body.blueprint.workflows[0]?.states.map((state) => state.key)).toEqual(['todo', 'done']);
  });

  it('復原刪除的狀態：回到原本的位置，沒有修改了', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));

    // Act
    await user.click(screen.getByRole('button', { name: '復原「待辦」' }));

    // Assert
    expect(within(statesList('任務流程')).getAllByRole('textbox')[0]).toHaveValue('待辦');
    expect(screen.getByRole('button', { name: '刪除「待辦」' })).toHaveFocus();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '預覽變更' })).toBeDisabled();
  });

  it('有列轉換的流程：說明限制，刪除狀態時一起拿掉進出它的轉換', async () => {
    // Arrange
    const { user, proposals } = await openSettings(agyDetail());
    const delivery = screen.getByRole('region', { name: '交付流程' });
    expect(within(delivery).getByText(/這個流程有限制轉換/)).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: '任務流程' })).queryByText(/這個流程有限制轉換/)).not.toBeInTheDocument();

    // Act
    await user.click(within(delivery).getByRole('button', { name: '刪除「內部驗收」' }));
    await chooseOption(user, screen.getByRole('combobox', { name: '停在「內部驗收」的項目搬到' }), '開發');
    await user.click(within(delivery).getByRole('checkbox', { name: '從「開發」到「客戶驗收」（交付流程）' }));
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    await waitFor(() => {
      expect(proposals).toHaveLength(1);
    });
    const [deliveryFlow, taskFlow] = proposals[0]?.body.blueprint.workflows ?? [];
    expect(deliveryFlow?.transitions?.map((transition) => `${transition.from}->${transition.to}`)).toEqual([
      'confirming->quoting',
      'quoting->designing',
      'designing->developing',
      'client_qa->developing',
      'client_qa->closed',
      'developing->client_qa',
    ]);
    expect(taskFlow).toEqual(agencyBlueprint.workflows[1]);
    expect(proposals[0]?.body.migrations?.moveStates).toEqual([{ workflow: 'delivery', from: 'internal_qa', to: 'developing' }]);
  });

  it('新增狀態到有列轉換的流程時，對話框說明要選進出的轉換，預設從前一個狀態進來、轉去後一個狀態', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());

    // Act
    await user.click(screen.getByRole('button', { name: '在「交付流程」新增狀態' }));

    // Assert
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });
    expect(dialog).toHaveAccessibleDescription(
      '「交付流程」有限制轉換：選新狀態從哪些狀態轉進來、可以轉去哪些狀態，之後也可以在「轉換」的表格裡改。',
    );
    const group = (name: string) => within(within(dialog).getByRole('group', { name }));
    const checkedCount = (name: string) => group(name).getAllByRole('checkbox', { checked: true }).length;
    expect(group('從哪些狀態可以轉進來').getByRole('checkbox', { name: '客戶驗收' })).toBeChecked();
    expect(checkedCount('從哪些狀態可以轉進來')).toBe(1);
    expect(group('可以轉去哪些狀態').getByRole('checkbox', { name: '結案' })).toBeChecked();
    expect(checkedCount('可以轉去哪些狀態')).toBe(1);
  });

  it('新增狀態的檢查：名稱必填、key 的格式與不重複', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '在「任務流程」新增狀態' }));
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });

    // Act
    await user.clear(within(dialog).getByLabelText('key'));
    await user.type(within(dialog).getByLabelText('key'), 'doing');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    expect(within(dialog).getByLabelText('名稱')).toHaveAccessibleDescription('請輸入名稱。');
    expect(within(dialog).getByLabelText('key')).toHaveAccessibleDescription(expect.stringContaining('這個流程已經有 key 是 doing 的狀態。'));
    await user.clear(within(dialog).getByLabelText('key'));
    await user.type(within(dialog).getByLabelText('key'), 'Review');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    expect(within(dialog).getByLabelText('key')).toHaveAccessibleDescription(expect.stringContaining('key 要用小寫英文字母開頭'));
  });

  it('每個流程至少要有一個完成類的狀態', async () => {
    // Arrange
    const { user, proposals } = await openSettings(smkDetail());

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '「完成」的類別' }), '進行中');
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    const region = screen.getByRole('region', { name: '任務流程' });
    expect(within(region).getByText('流程「任務流程」至少要有一個類別是「完成」的狀態。')).toBeInTheDocument();
    expect(proposals).toHaveLength(0);
  });
});
