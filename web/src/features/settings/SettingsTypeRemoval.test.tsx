import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { ProjectDetail } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agencyBlueprint, agencyFullBlueprint } from '../../test/blueprints';
import { captureProposals, invalidChangeSet, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

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

const [, changeRequest, task] = agencyBlueprint.itemTypes;
const full = () => agyDetail({ blueprint: agencyFullBlueprint });

/** 多一個自動化：條件看類型（需求或需求變更單）。 */
const withTypeAutomation: ProjectBlueprint = {
  ...agencyFullBlueprint,
  automations: [
    ...(agencyFullBlueprint.automations ?? []),
    {
      key: 'notify_requirements',
      name: '需求更新時通知',
      trigger: { source: 'item', event: 'item.updated' },
      conditions: [{ field: 'item_type', in: ['requirement', 'change_request'] }],
      actions: [{ type: 'notify', message: '需求有更新' }],
    },
  ],
};

/** 設定頁拆成區塊之後，切到另一個區塊（internal/HANDOFF-ui-ux-refactor.md 3A.4）。 */
async function showSection(user: ReturnType<typeof renderApp>['user'], name: string) {
  await user.click(within(screen.getByRole('navigation', { name: '設定的區塊' })).getByRole('link', { name }));
}

function removedList(): HTMLElement {
  return screen.getByRole('list', { name: '要刪除的類型' });
}

describe('設定頁：刪除類型', () => {
  it('刪除既有的類型：換到「要刪除」並說明，焦點到「復原」；送出時從其他類型的上層與檢視的篩選拿掉', async () => {
    // Arrange
    const { user, preview } = await openSettings(full());

    // Act
    await user.click(screen.getByRole('button', { name: '刪除類型「需求」' }));

    // Assert
    const row = within(removedList()).getByRole('listitem');
    expect(row).toHaveTextContent('還有這種項目時，預覽頁會擋下：要先刪除這些項目或改成別的類型。');
    expect(row).toHaveTextContent('也會從類型「任務」可以掛的上層、檢視「給客戶看」的類型篩選拿掉這個類型。');
    const restore = within(row).getByRole('button', { name: '復原類型「需求」' });
    expect(restore).toHaveFocus();
    expect(restore).toHaveAccessibleDescription(expect.stringContaining('還有這種項目時，預覽頁會擋下'));
    expect(within(screen.getByRole('list', { name: '類型' })).queryByRole('textbox', { name: '類型「需求」的名稱' })).not.toBeInTheDocument();
    await showSection(user, '欄位');
    expect(within(screen.getByRole('group', { name: '「報價金額」套用到的類型' })).queryByRole('checkbox', { name: '需求' })).not.toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.itemTypes).toEqual([changeRequest, { ...task, parents: ['change_request'] }]);
    expect(body.blueprint.views?.[1]?.filter?.itemTypes).toEqual(['change_request']);
    expect(body.migrations).toEqual({ moveStates: [], dropFields: [] });
  });

  it('拿掉之後清單變空的提醒：類型不能再有上層、檢視不再篩選類型（範圍變寬）', async () => {
    // Arrange
    const { user, preview } = await openSettings(full());

    // Act
    await user.click(screen.getByRole('button', { name: '刪除類型「需求」' }));
    await user.click(screen.getByRole('button', { name: '刪除類型「需求變更單」' }));

    // Assert
    const [, second] = within(removedList()).getAllByRole('listitem');
    expect(second).toHaveTextContent('注意：拿掉之後，「任務」不能再掛在任何類型底下；檢視「給客戶看」不再篩選類型（所有類型都會列出）。');
    expect(screen.getByText('還沒送出的修改：2 項')).toBeInTheDocument();
    const body = await preview();
    expect(body.blueprint.itemTypes).toEqual([{ ...task, parents: [] }]);
    expect(body.blueprint.views?.[1]?.filter?.itemTypes).toEqual([]);
  });

  it('復原：回到原本的位置，焦點回到「刪除」，沒有修改了', async () => {
    // Arrange
    const { user } = await openSettings(full());
    await user.click(screen.getByRole('button', { name: '刪除類型「需求變更單」' }));

    // Act
    await user.click(screen.getByRole('button', { name: '復原類型「需求變更單」' }));

    // Assert
    expect(screen.getByRole('button', { name: '刪除類型「需求變更單」' })).toHaveFocus();
    expect(screen.queryByRole('list', { name: '要刪除的類型' })).not.toBeInTheDocument();
    const names = within(screen.getByRole('list', { name: '類型' })).getAllByRole('textbox').map((input) => (input as HTMLInputElement).value);
    expect(names).toEqual(['需求', '需求變更單', '任務']);
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('要刪除的類型還在其他列的勾選裡（看不到）：改了別的勾選再復原，引用照樣回來', async () => {
    // Arrange
    const { user, preview } = await openSettings(full());
    await user.click(screen.getByRole('button', { name: '刪除類型「需求」' }));
    await showSection(user, '欄位');
    await user.click(within(screen.getByRole('group', { name: '「報價金額」套用到的類型' })).getByRole('checkbox', { name: '需求變更單' }));
    await showSection(user, '類型');
    await user.click(within(screen.getByRole('group', { name: '類型「任務」可以掛在哪些類型底下' })).getByRole('checkbox', { name: '需求變更單' }));

    // Act
    await user.click(screen.getByRole('button', { name: '復原類型「需求」' }));

    // Assert
    const body = await preview();
    expect(body.blueprint.itemTypes[0]).toEqual(agencyFullBlueprint.itemTypes[0]);
    expect(body.blueprint.itemTypes[1]?.fields).toEqual(['change_reason', 'impact', 'client_signoff_date']);
    expect(body.blueprint.itemTypes[2]?.parents).toEqual(['requirement']);
  });

  it('剛新增的類型刪掉就直接不見，焦點到「新增類型」', async () => {
    // Arrange
    const { user } = await openSettings(full());
    await user.click(screen.getByRole('button', { name: '新增類型' }));
    const dialog = screen.getByRole('dialog', { name: '新增類型' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Bug');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));

    // Act
    await user.click(screen.getByRole('button', { name: '刪除類型「bug」' }));

    // Assert
    expect(screen.queryByRole('textbox', { name: '類型「bug」的名稱' })).not.toBeInTheDocument();
    expect(screen.queryByRole('list', { name: '要刪除的類型' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增類型' })).toHaveFocus();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('自動化的條件用到這個類型：那一列提醒，不擋送出；伺服器擋下時說明標在那一列', async () => {
    // Arrange
    const detail = agyDetail({ blueprint: withTypeAutomation });
    const { user, preview } = await openSettings(detail, () =>
      invalidChangeSet([
        { code: 'automation.unknown_item_type', path: '/blueprint/automations/1/conditions/0/in/0', message: '條件裡的「requirement」是沒有定義的類型。' },
      ]),
    );
    await user.click(screen.getByRole('button', { name: '刪除類型「需求」' }));
    const row = within(removedList()).getByRole('listitem');
    expect(row).toHaveTextContent('自動化「需求更新時通知」用到這個類型：刪除之後，送出會被伺服器擋下。自動化目前只能用變更集文件修改。');

    // Act
    const body = await preview();

    // Assert
    expect(body.blueprint.automations).toEqual(withTypeAutomation.automations);
    expect(await within(row).findByText('條件裡的「requirement」是沒有定義的類型。')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '復原類型「需求」' })).toHaveAccessibleDescription(
      expect.stringContaining('條件裡的「requirement」是沒有定義的類型。'),
    );
  });

  it('只剩一個類型時「刪除」停用，說明寫在畫面上（和最後一位管理員一樣）', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    const remove = screen.getByRole('button', { name: '刪除類型「任務」' });
    const reason = '最後一個類型：要先新增另一個類型，才能刪除它。';

    // Act
    await user.click(remove);

    // Assert
    expect(remove).toHaveAttribute('aria-disabled', 'true');
    expect(remove).toHaveAccessibleDescription(reason);
    // 看得到的說明，不是只給報讀軟體的隱藏文字。
    expect(screen.getByText(reason)).not.toHaveClass('visually-hidden');
    expect(screen.queryByRole('list', { name: '要刪除的類型' })).not.toBeInTheDocument();
  });

  it('唯讀時沒有「刪除」', async () => {
    // Arrange & Act
    await openSettings(agyDetail({ myPermissions: ['item.read', 'blueprint.read'] }));

    // Assert
    expect(screen.queryByRole('button', { name: /^刪除類型/ })).not.toBeInTheDocument();
  });
});
