import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agencyBlueprint, agencyFullBlueprint } from '../../test/blueprints';
import { captureProposals, invalidChangeSet, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleMember, exampleOwner, ownerAsAdmin } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

async function openSettings(detail: ProjectDetail, respond?: Parameters<typeof captureProposals>[1]) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail, respond);
  const view = renderApp(`/projects/${detail.project.key}/settings/roles`);
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

/** 設定頁拆成區塊之後，切到另一個區塊（internal/HANDOFF-ui-ux-refactor.md 3A.4）。 */
async function showSection(user: ReturnType<typeof renderApp>['user'], name: string) {
  await user.click(within(screen.getByRole('navigation', { name: '設定的區塊' })).getByRole('link', { name }));
}

/** 和本機 AGY 一樣：有一位成員是客戶。 */
const withClient = () =>
  agyDetail({
    blueprint: agencyFullBlueprint,
    members: [ownerAsAdmin, { personId: exampleMember.personId, displayName: exampleMember.displayName, role: 'client' }],
  });

const [projectManager, client] = agencyBlueprint.roles ?? [];

describe('設定頁：角色', () => {
  it('勾選權限的地方有最高權限的說明', async () => {
    // Arrange & Act
    await openSettings(agyDetail());

    // Assert
    expect(screen.getByText('有「管理專案」（project.manage）的角色能設定成員與角色；能「套用變更集」（blueprint.apply）的角色能改每個角色的權限。兩者都等同這個專案的最高權限，只給信得過的人。')).toBeInTheDocument();
  });

  it('內建角色列出名稱與權限，不能改', async () => {
    // Arrange & Act
    await openSettings(agyDetail());

    // Assert
    const builtIn = screen.getByRole('list', { name: '內建角色' });
    const rows = within(builtIn).getAllByRole('listitem');
    expect(rows).toHaveLength(5);
    ['管理員', '編輯者', '執行者', '留言者', '檢視者'].forEach((name, index) => {
      expect(within(rows[index] ?? builtIn).getByText(name)).toBeInTheDocument();
      expect(within(rows[index] ?? builtIn).getByText('內建')).toBeInTheDocument();
    });
    expect(within(rows[4] ?? builtIn).getByText('viewer')).toBeInTheDocument();
    expect(within(rows[4] ?? builtIn).getByText('權限：查看項目、查看設定')).toBeInTheDocument();
    expect(within(rows[2] ?? builtIn).getByText('權限：只看自己的項目、新增項目、修改項目、改變狀態、留言、查看設定')).toBeInTheDocument();
    expect(within(builtIn).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(builtIn).queryByRole('button')).not.toBeInTheDocument();
  });

  it('新增角色並用在轉換上：權限預設和檢視者一樣；送出的角色與那條轉換都是新的，其他照原本的', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    await user.click(screen.getByRole('button', { name: '新增角色' }));
    const dialog = screen.getByRole('dialog', { name: '新增角色' });
    await user.type(within(dialog).getByLabelText('名稱'), 'QA Lead');
    expect(within(dialog).getByLabelText('key')).toHaveValue('qa_lead');
    const permissions = within(dialog).getByRole('group', { name: '權限' });
    expect(within(permissions).getByRole('checkbox', { name: '查看項目' })).toBeChecked();
    expect(within(permissions).getByRole('checkbox', { name: '查看設定' })).toBeChecked();
    await user.click(within(permissions).getByRole('checkbox', { name: '改變狀態' }));
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    await showSection(user, '流程');

    // Act
    await user.click(screen.getByRole('button', { name: '設定從「開發」到「內部驗收」的限制（交付流程）' }));
    const limits = screen.getByRole('dialog', { name: '從「開發」到「內部驗收」' });
    await user.click(within(limits).getByRole('checkbox', { name: 'QA Lead' }));
    await user.click(within(limits).getByRole('button', { name: '套用' }));

    // Assert
    expect(screen.getByText('還沒送出的修改：2 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.roles).toEqual([projectManager, client, { key: 'qa_lead', name: 'QA Lead', permissions: ['item.read', 'item.transition', 'blueprint.read'] }]);
    const transitions = body.blueprint.workflows[0]?.transitions ?? [];
    expect(transitions[3]).toEqual({ from: 'developing', to: 'internal_qa', allowedRoles: ['qa_lead'], requireFields: [] });
    expect(transitions.filter((_, index) => index !== 3)).toEqual(agencyBlueprint.workflows[0]?.transitions?.filter((_, index) => index !== 3));
    expect(body.blueprint.workflows[1]).toEqual(agencyBlueprint.workflows[1]);
  });

  it('勾「只看自己的項目」會取消「查看項目」，反過來也一樣；送出的只有一個，說明寫在區塊上方', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    const permissions = screen.getByRole('group', { name: '角色「客戶」的權限' });
    const readAll = within(permissions).getByRole<HTMLInputElement>('checkbox', { name: '查看項目' });
    const readOwn = within(permissions).getByRole<HTMLInputElement>('checkbox', { name: '只看自己的項目' });

    // Act
    await user.click(readOwn);
    const afterOwn = [readAll.checked, readOwn.checked];
    await user.click(readAll);
    const afterAll = [readAll.checked, readOwn.checked];
    await user.click(readOwn);
    const note = screen.queryByText(/「只看自己的項目」只看得到自己負責或建立的項目。兩個擇一/);
    const body = await preview();

    // Assert
    expect(note).not.toBeNull();
    expect(afterOwn).toEqual([false, true]);
    expect(afterAll).toEqual([true, false]);
    expect(body.blueprint.roles?.find((role) => role.key === 'client')?.permissions).toEqual(['item.read_own', 'comment.create']);
  });

  it('改名與權限：只送改了的那個角色', async () => {
    // Arrange
    const { user, preview } = await openSettings(agyDetail());
    const name = screen.getByRole('textbox', { name: '角色「客戶」的名稱' });
    await user.clear(name);
    await user.type(name, '業主');
    const permissions = screen.getByRole('group', { name: '角色「客戶」的權限' });

    // Act
    await user.click(within(permissions).getByRole('checkbox', { name: '新增項目' }));
    await user.click(within(permissions).getByRole('checkbox', { name: '留言' }));

    // Assert
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.roles).toEqual([projectManager, { key: 'client', name: '業主', permissions: ['item.read', 'item.create'] }]);
  });

  it('刪除角色：說明成員、會一起拿掉的引用與變寬的地方；送出時拿掉轉換與檢視裡的引用，復原後都回來', async () => {
    // Arrange
    const { user, preview } = await openSettings(withClient());

    // Act
    await user.click(screen.getByRole('button', { name: '刪除角色「專案經理」' }));
    await user.click(screen.getByRole('button', { name: '刪除角色「客戶」' }));

    // Assert
    const removed = screen.getByRole('list', { name: '要刪除的角色' });
    const [pmRow, clientRow] = within(removed).getAllByRole('listitem');
    expect(pmRow).toHaveTextContent('目前沒有成員是這個角色。');
    expect(pmRow).toHaveTextContent('也會從轉換「內部驗收」→「客戶驗收」（交付流程）、轉換「客戶驗收」→「結案」（交付流程）、檢視「給客戶看」拿掉這個角色。');
    expect(pmRow).toHaveTextContent(
      '注意：拿掉之後，轉換「內部驗收」→「客戶驗收」（交付流程）、轉換「客戶驗收」→「結案」（交付流程）不再限制角色（所有角色都能走）；檢視「給客戶看」變成所有成員都看得到。',
    );
    expect(clientRow).toHaveTextContent('還有 1 位成員是這個角色：套用前要先在「成員」把他們換成別的角色，否則預覽頁會擋下。');
    const restoreClient = within(clientRow ?? removed).getByRole('button', { name: '復原角色「客戶」' });
    expect(restoreClient).toHaveFocus();
    expect(restoreClient).toHaveAccessibleDescription(expect.stringContaining('還有 1 位成員是這個角色'));
    expect(within(pmRow ?? removed).getByRole('button', { name: '復原角色「專案經理」' })).toHaveAccessibleDescription(
      expect.stringContaining('也會從轉換「內部驗收」→「客戶驗收」（交付流程）'),
    );
    expect(screen.getByText('還沒有自訂角色。')).toBeInTheDocument();
    await showSection(user, '流程');
    expect(screen.getByRole('checkbox', { name: '從「客戶驗收」到「結案」（交付流程）' })).toHaveAccessibleDescription('要先填「客戶驗收日」');
    const body = await preview();
    expect(body.blueprint.roles).toEqual([]);
    const transitions = body.blueprint.workflows[0]?.transitions ?? [];
    expect(transitions[5]).toEqual({ from: 'internal_qa', to: 'client_qa', allowedRoles: [], requireFields: [] });
    expect(transitions[7]).toEqual({ from: 'client_qa', to: 'closed', allowedRoles: [], requireFields: ['client_signoff_date'] });
    const { visibleTo, ...openView } = agencyFullBlueprint.views?.[1] ?? { key: '', name: '', type: 'table' };
    expect(visibleTo).toEqual(['client', 'project_manager']);
    expect(body.blueprint.views).toEqual([agencyFullBlueprint.views?.[0], openView]);
  });

  it('要刪除的角色說明引用的轉換時，狀態的名稱空著就用 key', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    await showSection(user, '流程');
    await user.clear(screen.getByRole('textbox', { name: '「客戶驗收」的名稱' }));
    await showSection(user, '角色');

    // Act
    await user.click(screen.getByRole('button', { name: '刪除角色「專案經理」' }));

    // Assert
    expect(screen.getByRole('list', { name: '要刪除的角色' })).toHaveTextContent('也會從轉換「內部驗收」→「client_qa」（交付流程）、轉換「client_qa」→「結案」（交付流程）');
  });

  it('剛新增的角色刪掉就沒了，焦點回到「新增角色」；復原刪除的角色回到原本的位置', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    await user.click(screen.getByRole('button', { name: '新增角色' }));
    await user.type(within(screen.getByRole('dialog', { name: '新增角色' })).getByLabelText('名稱'), '暫時');
    await user.click(screen.getByRole('button', { name: '加入' }));

    // Act
    await user.click(screen.getByRole('button', { name: '刪除角色「role_1」' }));

    // Assert
    expect(screen.getByRole('button', { name: '新增角色' })).toHaveFocus();
    expect(screen.queryByRole('list', { name: '要刪除的角色' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '刪除角色「專案經理」' }));
    await user.click(screen.getByRole('button', { name: '復原角色「專案經理」' }));
    expect(screen.getByRole('button', { name: '刪除角色「專案經理」' })).toHaveFocus();
    expect(within(screen.getByRole('list', { name: '自訂角色' })).getAllByRole('textbox').map((input) => (input as HTMLInputElement).value)).toEqual(['專案經理', '客戶']);
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('新增角色的 key 不能是內建角色，也不能和要刪除的角色重複', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail());
    await user.click(screen.getByRole('button', { name: '刪除角色「客戶」' }));
    await user.click(screen.getByRole('button', { name: '新增角色' }));
    const dialog = screen.getByRole('dialog', { name: '新增角色' });
    await user.type(within(dialog).getByLabelText('名稱'), '看的人');
    const key = within(dialog).getByLabelText('key');

    // Act
    await user.clear(key);
    await user.type(key, 'viewer');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Assert
    expect(key).toHaveAccessibleDescription(expect.stringContaining('「viewer」是內建角色的 key，自訂角色不能用。'));
    await user.clear(key);
    await user.type(key, 'client');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    expect(key).toHaveAccessibleDescription(expect.stringContaining('已經有 key 是 client 的角色。'));
  });

  it('伺服器的問題標在角色那一列', async () => {
    // Arrange
    const details = [{ code: 'role.builtin_conflict', path: '/blueprint/roles/1/key', message: '自訂角色和內建角色同名。' }];
    const { user } = await openSettings(agyDetail(), () => invalidChangeSet(details));
    const name = screen.getByRole('textbox', { name: '角色「客戶」的名稱' });
    await user.type(name, '們');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器檢查後擋下了這份變更（1 個問題）');
    expect(name).toHaveAccessibleDescription('自訂角色和內建角色同名。');
  });
});

describe('設定頁：自動化的提醒', () => {
  it('要刪除的狀態被自動化用到、而且沒有別的流程有它時，那一列提醒送出會被擋下', async () => {
    // Arrange
    const { user } = await openSettings(withClient());
    await showSection(user, '流程');

    // Act
    await user.click(screen.getByRole('button', { name: '刪除「內部驗收」' }));

    // Assert
    const removed = screen.getByRole('list', { name: '「交付流程」要刪除的狀態' });
    const reminder = '自動化「合併請求合併後轉內部驗收」用到這個狀態：刪除之後，送出會被伺服器擋下。自動化目前只能用變更集文件修改。';
    expect(removed).toHaveTextContent(reminder);
    const move = screen.getByRole('combobox', { name: '停在「內部驗收」的項目搬到' });
    expect(move).toHaveFocus();
    expect(move).toHaveAccessibleDescription(reminder);
    await user.click(screen.getByRole('button', { name: '刪除「報價」' }));
    expect(within(screen.getByRole('list', { name: '「交付流程」要刪除的狀態' })).getAllByRole('listitem')[0]).not.toHaveTextContent('自動化');
  });

  it('要刪除的欄位被自動化的條件或動作用到時提醒', async () => {
    // Arrange
    const blueprint = {
      ...agencyFullBlueprint,
      automations: [
        { key: 'mark_impact', name: '標記影響', trigger: { source: 'item', event: 'item.created' }, actions: [{ type: 'set_field', field: 'impact', value: 'high' }] },
      ],
    } as const;
    const { user } = await openSettings(agyDetail({ blueprint }));
    await showSection(user, '欄位');

    // Act
    await user.click(screen.getByRole('button', { name: '刪除「影響程度」' }));

    // Assert
    expect(screen.getByRole('list', { name: '要刪除的欄位' })).toHaveTextContent('自動化「標記影響」用到這個欄位：刪除之後，送出會被伺服器擋下。');
    const restore = screen.getByRole('button', { name: '復原「影響程度」' });
    expect(restore).toHaveFocus();
    expect(restore).toHaveAccessibleDescription(expect.stringContaining('自動化「標記影響」用到這個欄位'));
  });
});

describe('設定頁：唯讀', () => {
  it('只有 blueprint.read 時轉換、類型、角色都是純文字，沒有任何輸入框與按鈕', async () => {
    // Arrange & Act
    const { user } = await openSettings(agyDetail({ myPermissions: ['item.read', 'blueprint.read'] }));
    await showSection(user, '流程');

    // Assert
    const transitions = screen.getByRole('list', { name: '「交付流程」的轉換' });
    expect(within(transitions).getAllByRole('listitem')).toHaveLength(8);
    expect(within(transitions).getAllByRole('listitem')[7]).toHaveTextContent('「客戶驗收」→「結案」（限「專案經理」；要先填「客戶驗收日」）');
    expect(within(screen.getByRole('region', { name: '交付流程' })).getByText('限制：只有下面列出的轉換可以走。')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: '任務流程' })).getByText('不限制：任何兩個狀態之間都可以互轉。')).toBeInTheDocument();
    await showSection(user, '類型');
    const taskRow = within(screen.getByRole('list', { name: '類型' })).getAllByRole('listitem')[2] ?? document.body;
    expect(within(taskRow).getByText('任務')).toBeInTheDocument();
    expect(within(taskRow).getByText('流程：任務流程')).toBeInTheDocument();
    expect(within(taskRow).getByText('上層：需求、需求變更單')).toBeInTheDocument();
    await showSection(user, '角色');
    const clientRow = within(screen.getByRole('list', { name: '自訂角色' })).getAllByRole('listitem')[1] ?? document.body;
    expect(within(clientRow).getByText('客戶')).toBeInTheDocument();
    expect(within(clientRow).getByText('client')).toBeInTheDocument();
    expect(within(clientRow).getByText('權限：查看項目、留言')).toBeInTheDocument();
    const main = screen.getByRole('main');
    expect(within(main).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(main).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(main).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(main).queryByRole('button')).not.toBeInTheDocument();
    for (const section of ['流程', '類型', '欄位', '角色']) {
      await showSection(user, section);
      const content = within(screen.getByRole('main'));
      expect(content.queryByRole('textbox')).not.toBeInTheDocument();
      expect(content.queryByRole('combobox')).not.toBeInTheDocument();
      expect(content.queryByRole('checkbox')).not.toBeInTheDocument();
      expect(content.queryByRole('button')).not.toBeInTheDocument();
    }
  });
});
