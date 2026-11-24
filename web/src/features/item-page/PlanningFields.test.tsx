import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { created, serveActivity, showAllActivity, updated } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, servePatch } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const SPRINT_1 = '0190a000-0000-7000-8000-0000000000d1';
const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';

const CYCLES = [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 }), cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 })];
const MILESTONES = [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })];

interface Options {
  readonly cycles?: typeof CYCLES;
  readonly milestones?: typeof MILESTONES;
  readonly overrides?: Parameters<typeof smkItem>[0];
}

function openItem({ cycles = CYCLES, milestones = MILESTONES, overrides = {} }: Options = {}) {
  const item = detailOf(smkItem({ number: 7, title: '首頁設計稿', ...overrides }), '說明');
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveCycles('SMK', cycles);
  serveMilestones('SMK', milestones);
  serveItem(item);
  const activity = serveActivity(item.key, [created()]);
  const patches = servePatch(item);
  const view = renderApp('/projects/SMK/items/SMK-7');
  return { ...view, item, activity, patches };
}

describe('項目頁的週期與里程碑欄位', () => {
  it('欄位區多了「週期」「里程碑」：顯示目前的名稱，沒有時是「未填」', async () => {
    // Arrange / Act
    openItem({ overrides: { cycleId: SPRINT_2 } });
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Assert
    expect(await within(fields).findByRole('button', { name: '週期：衝刺 2' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '里程碑：未填' })).toBeInTheDocument();
  });

  it('選項是「沒有週期」加上每一個週期；選了就送出 cycleId', async () => {
    // Arrange
    const { user, patches } = openItem();
    const fields = await screen.findByRole('region', { name: '欄位' });
    await user.click(await within(fields).findByRole('button', { name: '週期：未填' }));
    const select = within(fields).getByRole('combobox', { name: '週期' });

    // Act
    const options = await openOptions(user, select);
    expect(options.map((option) => option.textContent)).toEqual(['沒有週期', '衝刺 1', '衝刺 2']);
    await chooseOption(user, select, '衝刺 2');

    // Assert
    await waitFor(() => {
      expect(patches.map((request) => request.body)).toEqual([{ cycleId: SPRINT_2 }]);
    });
    expect(await within(fields).findByRole('button', { name: '週期：衝刺 2' })).toBeInTheDocument();
  });

  it('改成「沒有週期」送 null（清掉）', async () => {
    // Arrange
    const { user, patches } = openItem({ overrides: { cycleId: SPRINT_1 } });
    const fields = await screen.findByRole('region', { name: '欄位' });
    await user.click(await within(fields).findByRole('button', { name: '週期：衝刺 1' }));

    // Act
    await chooseOption(user, within(fields).getByRole('combobox', { name: '週期' }), '沒有週期');

    // Assert
    await waitFor(() => {
      expect(patches.map((request) => request.body)).toEqual([{ cycleId: null }]);
    });
  });

  it('里程碑也一樣', async () => {
    // Arrange
    const { user, patches } = openItem();
    const fields = await screen.findByRole('region', { name: '欄位' });
    await user.click(await within(fields).findByRole('button', { name: '里程碑：未填' }));

    // Act
    await chooseOption(user, within(fields).getByRole('combobox', { name: '里程碑' }), 'v1.0 上線');

    // Assert
    await waitFor(() => {
      expect(patches.map((request) => request.body)).toEqual([{ milestoneId: V1 }]);
    });
  });

  it('專案還沒有週期時不顯示「週期」，沒有里程碑時不顯示「里程碑」', async () => {
    // Arrange / Act
    openItem({ cycles: [], milestones: [] });
    const fields = await screen.findByRole('region', { name: '欄位' });
    await within(fields).findByRole('button', { name: '優先度：未填' });

    // Assert
    expect(within(fields).queryByRole('button', { name: /^週期/ })).not.toBeInTheDocument();
    expect(within(fields).queryByRole('button', { name: /^里程碑/ })).not.toBeInTheDocument();
  });

  it('項目指著已經查不到的週期時顯示「已刪除的週期」', async () => {
    // Arrange / Act
    openItem({ cycles: CYCLES.slice(1), overrides: { cycleId: SPRINT_1 } });
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Assert
    expect(await within(fields).findByRole('button', { name: '週期：已刪除的週期' })).toBeInTheDocument();
  });

  it('沒有 item.update 的人看得到、不能改', async () => {
    // Arrange
    const item = detailOf(smkItem({ number: 7, title: '首頁設計稿', cycleId: SPRINT_2 }));
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });
    serveCycles('SMK', CYCLES);
    serveMilestones('SMK', MILESTONES);
    serveItem(item);
    serveActivity(item.key, [created()]);

    // Act
    renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Assert
    expect(await within(fields).findByText('衝刺 2')).toBeInTheDocument();
    expect(within(fields).queryByRole('button', { name: /^週期/ })).not.toBeInTheDocument();
  });
});

describe('活動的說法', () => {
  it('把「週期」從「衝刺 2」改成「衝刺 1」，用目前的名稱', async () => {
    // Arrange
    const item = detailOf(smkItem({ number: 7, title: '首頁設計稿', cycleId: SPRINT_1 }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveCycles('SMK', CYCLES);
    serveMilestones('SMK', MILESTONES);
    serveItem(item);
    serveActivity(item.key, [created(), updated([{ field: 'cycle', from: SPRINT_2, to: SPRINT_1 }]), updated([{ field: 'milestone', from: 'gone', to: null }])]);

    // Act
    renderApp('/projects/SMK/items/SMK-7');
    const section = await screen.findByRole('region', { name: '活動與留言' });

    // Assert
    const list = await within(section).findByRole('list', { name: '活動與留言' });
    await waitFor(() => {
      expect(list).toHaveTextContent('把「週期」從 衝刺 2 改成 衝刺 1');
    });
    expect(list).toHaveTextContent('清除了「里程碑」（原本是 已刪除的里程碑）');
  });
});
