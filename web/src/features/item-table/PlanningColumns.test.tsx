import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';

const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';

async function openTable({ withPlanning = true } = {}) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveCycles('SMK', withPlanning ? [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 })] : []);
  serveMilestones('SMK', withPlanning ? [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })] : []);
  serveItems('SMK', [smkItem({ number: 1, title: '首頁設計稿', cycleId: SPRINT_2, milestoneId: V1 }), smkItem({ number: 2, title: '聯絡表單' })]);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

describe('表格的週期與里程碑欄', () => {
  it('預設不顯示，從「顯示的欄位」打開，格子是目前的名稱', async () => {
    // Arrange
    const { user, table } = await openTable();
    expect(within(table).queryByRole('columnheader', { name: '週期' })).not.toBeInTheDocument();

    // Act
    await user.click(await screen.findByRole('button', { name: '顯示的欄位' }));
    const group = screen.getByRole('group', { name: '顯示的欄位' });
    expect(within(group).getByRole('checkbox', { name: '週期' })).not.toBeChecked();
    await user.click(within(group).getByRole('checkbox', { name: '週期' }));
    await user.click(within(group).getByRole('checkbox', { name: '里程碑' }));

    // Assert
    expect(within(table).getByRole('columnheader', { name: '週期' })).toBeInTheDocument();
    const row = within(table).getByRole('row', { name: /首頁設計稿/ });
    expect(within(row).getByRole('button', { name: '週期：衝刺 2' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '里程碑：v1.0 上線' })).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /聯絡表單/ })).getByRole('button', { name: '週期：未填' })).toBeInTheDocument();
  });

  it('預設就不顯示的週期與里程碑不算進「隱藏了 N 欄」：沒動過欄位時不出現這句，自己關掉別欄才算', async () => {
    // Arrange
    const { user, table } = await openTable();
    await within(table).findByRole('row', { name: /首頁設計稿/ });
    expect(screen.queryByText(/隱藏了/)).not.toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '顯示的欄位' }));
    await user.click(within(screen.getByRole('group', { name: '顯示的欄位' })).getByRole('checkbox', { name: '狀態' }));

    // Assert
    expect(screen.getByText('隱藏了 1 欄')).toBeInTheDocument();
  });

  it('專案沒有週期、里程碑時，「顯示的欄位」裡也沒有這兩欄', async () => {
    // Arrange
    const { user } = await openTable({ withPlanning: false });

    // Act
    await user.click(await screen.findByRole('button', { name: '顯示的欄位' }));

    // Assert
    const group = screen.getByRole('group', { name: '顯示的欄位' });
    expect(within(group).queryByRole('checkbox', { name: '週期' })).not.toBeInTheDocument();
    expect(within(group).queryByRole('checkbox', { name: '里程碑' })).not.toBeInTheDocument();
  });
});
