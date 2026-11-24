import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint, WorkflowTransition } from '../../api/blueprint';
import type { ProjectDetail } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { agencyBlueprint, simpleTasksBlueprint } from '../../test/blueprints';
import { captureProposals, invalidChangeSet, serveChangeSets } from '../../test/changesets';
import { agyDetail, agyProject, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { openOptions } from '../../test/select';
import { server } from '../../test/server';

/** 設定頁拆成區塊之後，切到另一個區塊（internal/HANDOFF-ui-ux-refactor.md 3A.4）。 */
async function showSection(user: ReturnType<typeof renderApp>['user'], name: string) {
  await user.click(within(screen.getByRole('navigation', { name: '設定的區塊' })).getByRole('link', { name }));
}

async function openSettings(detail: ProjectDetail, respond?: Parameters<typeof captureProposals>[1]) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail, respond);
  const view = renderApp(`/projects/${detail.project.key}/settings`);
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

function region(name: string): HTMLElement {
  return screen.getByRole('region', { name });
}

/** 接案的專案有兩個流程：勾選框的名稱後面帶流程的名稱。 */
function cell(from: string, to: string): HTMLElement {
  return screen.getByRole('checkbox', { name: `從「${from}」到「${to}」（交付流程）` });
}

function smkCell(from: string, to: string): HTMLElement {
  return screen.getByRole('checkbox', { name: `從「${from}」到「${to}」` });
}

function limitsButton(from: string, to: string): HTMLElement {
  return screen.getByRole('button', { name: `設定從「${from}」到「${to}」的限制（交付流程）` });
}

/** 交付流程的轉換改掉其中一條（「客戶驗收」→「結案」）的限制。 */
function withClosing(patch: Partial<WorkflowTransition>): ProjectBlueprint {
  return {
    ...agencyBlueprint,
    workflows: agencyBlueprint.workflows.map((workflow) => ({
      ...workflow,
      ...(workflow.transitions && {
        transitions: workflow.transitions.map((transition) => (transition.to === 'closed' ? { ...transition, ...patch } : transition)),
      }),
    })),
  };
}

function transition(from: string, to: string, allowedRoles: readonly string[] = [], requireFields: readonly string[] = []): WorkflowTransition {
  return { from, to, allowedRoles, requireFields };
}

const [delivery, taskFlow] = agencyBlueprint.workflows;
const originalTransitions = delivery?.transitions ?? [];

describe('設定頁：轉換', () => {
  it('在交付流程新增狀態並設定進出的轉換：送出的流程多了狀態與三條轉換，其他部分和原本的一樣', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    await user.click(screen.getByRole('button', { name: '在「交付流程」新增狀態' }));
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Review');
    await user.click(within(within(dialog).getByRole('group', { name: '從哪些狀態可以轉進來' })).getByRole('checkbox', { name: '內部驗收' }));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    expect(cell('客戶驗收', 'review')).toBeChecked();
    expect(cell('客戶驗收', 'review')).toHaveAccessibleDescription('新增');
    expect(cell('review', '結案')).toBeChecked();
    expect(cell('review', '開發')).not.toBeChecked();
    const body = await preview();
    const states = delivery?.states ?? [];
    expect(body.blueprint).toEqual({
      ...agencyBlueprint,
      workflows: [
        {
          ...delivery,
          states: [...states.slice(0, 6), { key: 'review', name: 'Review', category: 'in_progress' }, ...states.slice(6)],
          transitions: [...originalTransitions, transition('internal_qa', 'review'), transition('client_qa', 'review'), transition('review', 'closed')],
        },
        taskFlow,
      ],
    });
    expect(body.blueprint.workflows[0]?.transitions?.slice(0, 8)).toEqual(originalTransitions);
    expect(body.migrations).toEqual({ moveStates: [], dropFields: [] });
  });

  it('取消一條轉換再勾回來：標成要刪除、計數，勾回來之後不算修改', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());

    // Act
    await user.click(cell('客戶驗收', '結案'));

    // Assert
    expect(cell('客戶驗收', '結案')).not.toBeChecked();
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription('要刪除');
    expect(screen.queryByRole('button', { name: '設定從「客戶驗收」到「結案」的限制（交付流程）' })).not.toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    await user.click(cell('客戶驗收', '結案'));
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription('限「專案經理」；要先填「客戶驗收日」');
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '預覽變更' })).toBeDisabled();
  });

  it('有限制切回不限制：transitions 整個不送，表格不見', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    const flow = region('交付流程');

    // Act
    await user.click(within(flow).getByRole('checkbox', { name: '限制轉換（交付流程）' }));

    // Assert
    expect(within(flow).queryByRole('region', { name: '「交付流程」的轉換' })).not.toBeInTheDocument();
    expect(within(flow).getByText('不限制：任何兩個狀態之間都可以互轉。')).toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.workflows[0]).toEqual({ key: 'delivery', name: '交付流程', states: delivery?.states });
    expect(body.blueprint.workflows[0] && 'transitions' in body.blueprint.workflows[0]).toBe(false);
    expect(body.blueprint.workflows[1]).toEqual(taskFlow);
  });

  it('不限制切成限制：從每兩個狀態都可以互轉開始，取消不要的才算修改', async () => {
    // Arrange
    const { user, preview } = await openSettings(smkDetail());
    await user.click(within(region('任務流程')).getByRole('checkbox', { name: '限制轉換（任務流程）' }));
    const matrix = screen.getByRole('region', { name: '「任務流程」的轉換' });
    expect(within(matrix).getAllByRole('checkbox').filter((box) => box instanceof HTMLInputElement && box.checked)).toHaveLength(6);
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();

    // Act
    await user.click(smkCell('完成', '待辦'));

    // Assert
    expect(screen.getByText('還沒送出的修改：2 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.workflows).toEqual([
      {
        ...simpleTasksBlueprint.workflows[0],
        transitions: [transition('todo', 'doing'), transition('todo', 'done'), transition('doing', 'todo'), transition('doing', 'done'), transition('done', 'doing')],
      },
    ]);
  });

  it('改允許的角色與要先填的欄位：只有那一條轉換改了，取消對話框不改', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    await user.click(limitsButton('內部驗收', '客戶驗收'));
    let dialog = screen.getByRole('dialog', { name: '從「內部驗收」到「客戶驗收」' });
    await user.click(within(dialog).getByRole('checkbox', { name: '管理員' }));
    await user.click(within(dialog).getByRole('button', { name: '取消' }));
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(limitsButton('內部驗收', '客戶驗收')).toHaveFocus();

    // Act
    await user.click(limitsButton('內部驗收', '客戶驗收'));
    dialog = screen.getByRole('dialog', { name: '從「內部驗收」到「客戶驗收」' });
    expect(within(dialog).getByRole('checkbox', { name: '專案經理' })).toBeChecked();
    await user.click(within(dialog).getByRole('checkbox', { name: '客戶（不能改變狀態）' }));
    await user.click(within(within(dialog).getByRole('group', { name: '要先填的欄位' })).getByRole('checkbox', { name: '說明' }));
    await user.click(within(dialog).getByRole('button', { name: '套用' }));

    // Assert
    expect(cell('內部驗收', '客戶驗收')).toHaveAccessibleDescription('已修改 限「專案經理」、「客戶」；要先填「說明」');
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const body = await preview();
    const sent = body.blueprint.workflows[0]?.transitions ?? [];
    expect(sent[5]).toEqual(transition('internal_qa', 'client_qa', ['project_manager', 'client'], ['description']));
    expect(sent.filter((_, index) => index !== 5)).toEqual(originalTransitions.filter((_, index) => index !== 5));
  });

  it('送出前先擋：走不到的狀態標在那一列，要先填的欄位缺在某個類型時標在那一格並寫在表格下方', async () => {
    // Arrange
    const { user, proposals } = await openSettings(agyDetail());
    await user.click(cell('報價', '設計'));
    await showSection(user, '欄位');
    const types = screen.getByRole('group', { name: '「客戶驗收日」套用到的類型' });
    await user.click(within(types).getByRole('checkbox', { name: '需求變更單' }));
    await showSection(user, '流程');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 7 個地方要修正');
    expect(proposals).toHaveLength(0);
    expect(screen.getByRole('textbox', { name: '「報價」的名稱' })).toHaveAccessibleDescription('「報價」不是完成或已取消類，卻沒有可以轉出去的轉換。');
    expect(screen.getByRole('textbox', { name: '「設計」的名稱' })).toHaveAccessibleDescription('從起始狀態「需求確認」走不到「設計」：請在「轉換」勾一條轉進來的路。');
    expect(screen.getByRole('textbox', { name: '「結案」的名稱' })).toHaveAccessibleDescription('從起始狀態「需求確認」走不到「結案」：請在「轉換」勾一條轉進來的路。');
    const message = '「需求變更單」沒有套用「客戶驗收日」：用「交付流程」的類型都要有這個欄位，這條轉換才走得過去。';
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription(expect.stringContaining(message));
    expect(cell('客戶驗收', '結案')).toHaveAttribute('aria-invalid', 'true');
    expect(within(region('交付流程')).getByText(`「客戶驗收」→「結案」：${message}`)).toBeInTheDocument();
  });

  it('自己轉到自己的格子沒有勾選框', async () => {
    // Arrange & Act
    await openSettings(agyDetail());

    // Assert
    const matrix = screen.getByRole('region', { name: '「交付流程」的轉換' });
    expect(within(matrix).getAllByRole('checkbox')).toHaveLength(7 * 6);
    expect(within(matrix).getAllByText('不能轉到自己')).toHaveLength(7);
    expect(matrix).toHaveAttribute('tabindex', '0');
  });

  it('限制對話框標出沒有「改變狀態」權限的角色；轉換只允許這些角色時送出前先擋', async () => {
    // Arrange
    const { user, proposals } = await openSettings(agyDetail());
    await user.click(limitsButton('客戶驗收', '結案'));
    const dialog = screen.getByRole('dialog', { name: '從「客戶驗收」到「結案」' });
    expect(within(dialog).getByRole('checkbox', { name: '編輯者' })).toBeInTheDocument();
    expect(within(dialog).getByRole('checkbox', { name: '檢視者（不能改變狀態）' })).toBeInTheDocument();
    await user.click(within(dialog).getByRole('checkbox', { name: '專案經理' }));
    await user.click(within(dialog).getByRole('checkbox', { name: '客戶（不能改變狀態）' }));
    await user.click(within(dialog).getByRole('button', { name: '套用' }));

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 1 個地方要修正');
    expect(proposals).toHaveLength(0);
    const message = '只允許「客戶」，但這個角色沒有「改變狀態」的權限，沒有人走得了這條轉換。';
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription(expect.stringContaining(message));
    expect(within(region('交付流程')).getByText(`「客戶驗收」→「結案」：${message}`)).toBeInTheDocument();
  });

  it('轉換原本就要求的其他核心欄位（例如到期日）看得到、也拿得掉', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail({ blueprint: withClosing({ requireFields: ['client_signoff_date', 'due_date'] }) }));
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription('限「專案經理」；要先填「到期日」、「客戶驗收日」');
    await user.click(limitsButton('客戶驗收', '結案'));
    const fields = within(screen.getByRole('dialog', { name: '從「客戶驗收」到「結案」' })).getByRole('group', { name: '要先填的欄位' });
    expect(within(fields).getByRole('checkbox', { name: '到期日' })).toBeChecked();

    // Act
    await user.click(within(fields).getByRole('checkbox', { name: '到期日' }));
    await user.click(screen.getByRole('button', { name: '套用' }));

    // Assert
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription('已修改 限「專案經理」；要先填「客戶驗收日」');
    const body = await preview();
    expect(body.blueprint.workflows[0]?.transitions?.[7]).toEqual(transition('client_qa', 'closed', ['project_manager'], ['client_signoff_date']));
  });

  it('打開「設定」不改就按「套用」：仍然沒有修改', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail({ blueprint: withClosing({ requireFields: ['client_signoff_date', 'description'] }) }));
    await user.click(limitsButton('客戶驗收', '結案'));

    // Act
    await user.click(within(screen.getByRole('dialog', { name: '從「客戶驗收」到「結案」' })).getByRole('button', { name: '套用' }));

    // Assert
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '預覽變更' })).toBeDisabled();
    expect(cell('客戶驗收', '結案')).toHaveAccessibleDescription('限「專案經理」；要先填「說明」、「客戶驗收日」');
  });

  it('表頭用草稿的名稱（空的時候用 key）；勾選框與「設定」的名稱用目前藍圖的原名，打字時不跟著變', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    const name = screen.getByRole('textbox', { name: '「開發」的名稱' });
    const table = screen.getByRole('table', { name: '「交付流程」的轉換：列是起點，欄是終點' });

    // Act
    await user.clear(name);

    // Assert
    expect(within(table).getByRole('columnheader', { name: 'developing' })).toBeInTheDocument();
    expect(within(table).getByRole('rowheader', { name: 'developing' })).toBeInTheDocument();
    await user.type(name, '實作');
    expect(within(table).getByRole('columnheader', { name: '實作' })).toBeInTheDocument();
    expect(cell('開發', '內部驗收')).toBeChecked();
    await user.click(limitsButton('開發', '內部驗收'));
    expect(screen.getByRole('dialog', { name: '從「實作」到「內部驗收」' })).toBeInTheDocument();
  });

  it('伺服器標在一條轉換的問題：取消勾選之後照樣列在表格下方，勾選框的說明一定連得到；格子另外寫「有問題」', async () => {
    // Arrange
    const details = [{ code: 'transition.example', path: '/blueprint/workflows/0/transitions/7', message: '伺服器說這條轉換有問題。' }];
    const { user } = await openSettings(agyDetail(), () => invalidChangeSet(details));
    const name = screen.getByRole('textbox', { name: '「報價」的名稱' });
    await user.type(name, '單');
    await user.click(screen.getByRole('button', { name: '預覽變更' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器檢查後擋下了這份變更（1 個問題）');
    const table = screen.getByRole('table', { name: '「交付流程」的轉換：列是起點，欄是終點' });
    expect(within(table).getAllByText('有問題')).toHaveLength(1);

    // Act
    await user.click(cell('客戶驗收', '結案'));

    // Assert
    const box = cell('客戶驗收', '結案');
    expect(box).not.toBeChecked();
    expect(box).toHaveAccessibleDescription(`要刪除 「客戶驗收」→「結案」：${details[0]?.message ?? ''}`);
    const ids = (box.getAttribute('aria-describedby') ?? '').split(' ');
    expect(ids.every((id) => document.getElementById(id) !== null)).toBe(true);
    expect(within(region('交付流程')).getByText(`「客戶驗收」→「結案」：${details[0]?.message ?? ''}`)).toBeInTheDocument();
    expect(within(table).getAllByText('有問題')).toHaveLength(1);
  });

  it('限制對話框開著時，草稿被背景重抓換成新的一版、那條轉換不在了：對話框關上，焦點到「限制轉換」', async () => {
    // Arrange
    const first = agyDetail();
    const { user, queryClient } = await openSettings(first);
    let current = first;
    server.use(http.get('/v1/projects/AGY', () => ok(current)));
    await user.click(limitsButton('客戶驗收', '結案'));
    expect(screen.getByRole('dialog', { name: '從「客戶驗收」到「結案」' })).toBeInTheDocument();
    const without: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: agencyBlueprint.workflows.map((workflow) => ({
        ...workflow,
        ...(workflow.transitions && { transitions: workflow.transitions.filter((entry) => entry.to !== 'closed') }),
      })),
    };

    // Act
    current = agyDetail({ project: { ...agyProject, blueprintVersion: 2 }, blueprint: without });
    await queryClient.refetchQueries({ queryKey: ['project', 'AGY'], exact: true });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(region('交付流程')).getByRole('checkbox', { name: '限制轉換（交付流程）' })).toHaveFocus();
  });

  it('新增狀態的對話框：狀態的名稱空著時用 key', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    await user.clear(screen.getByRole('textbox', { name: '「客戶驗收」的名稱' }));

    // Act
    await user.click(screen.getByRole('button', { name: '在「交付流程」新增狀態' }));

    // Assert
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });
    expect(within(within(dialog).getByRole('group', { name: '從哪些狀態可以轉進來' })).getByRole('checkbox', { name: 'client_qa' })).toBeChecked();
    await openOptions(user, within(dialog).getByRole('combobox', { name: '位置' }));
    expect(await within(dialog).findByRole('option', { name: '放在「client_qa」之後' })).toBeInTheDocument();
  });
});
