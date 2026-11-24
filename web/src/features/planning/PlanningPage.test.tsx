import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { cycleOf, milestoneOf } from '../../test/planning';
import { currentPath, renderApp } from '../../test/renderApp';
import { fakeToday, openPlanning } from './planningTestUtils';

afterEach(() => {
  vi.useRealTimers();
});

const sprint1 = cycleOf('衝刺 1', '2026-11-02', '2026-11-15', {
  id: 'c1',
  progress: { total: 5, done: 5, canceled: 0, estimateTotal: 10, estimateDone: 10 },
});
const sprint2 = cycleOf('衝刺 2', '2026-11-16', '2026-11-29', {
  id: 'c2',
  progress: { total: 10, done: 3, canceled: 1, estimateTotal: 21, estimateDone: 8 },
});
const sprint3 = cycleOf('衝刺 3', '2026-11-30', '2026-12-13', { id: 'c3' });

async function cycleRows() {
  const list = await screen.findByRole('list', { name: '週期' });
  return within(list).getAllByRole('listitem');
}

describe('規劃頁：週期', () => {
  it('依開始日列出名稱、日期範圍與狀態（進行中、之後、已結束）', async () => {
    // Arrange
    fakeToday();
    openPlanning({ cycles: [sprint3, sprint1, sprint2] });

    // Act
    const rows = await cycleRows();

    // Assert
    expect(rows.map((row) => within(row).getByRole('link').textContent)).toEqual(['衝刺 1', '衝刺 2', '衝刺 3']);
    expect(within(rows[0] as HTMLElement).getByText('2026/11/02 – 2026/11/15')).toBeInTheDocument();
    expect(within(rows[0] as HTMLElement).getByText('已結束')).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('進行中')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByText('之後')).toBeInTheDocument();
  });

  it('進度：取消的不算進分母。完成 3／9・取消 1、估計 8／21，進度條帶數值', async () => {
    // Arrange
    fakeToday();
    openPlanning({ cycles: [sprint2] });

    // Act
    const [row] = await cycleRows();

    // Assert
    expect(within(row as HTMLElement).getByText('完成 3／9・取消 1')).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText('估計 8／21')).toBeInTheDocument();
    const bar = within(row as HTMLElement).getByRole('progressbar', { name: '衝刺 2 的完成進度' });
    expect(bar).toHaveAttribute('value', '3');
    expect(bar).toHaveAttribute('max', '9');
    expect(bar).toHaveAccessibleDescription('');
  });

  it('沒有填估計時不顯示估計；沒有項目時說還沒有項目、沒有進度條', async () => {
    // Arrange
    fakeToday();
    openPlanning({ cycles: [sprint3] });

    // Act
    const [row] = await cycleRows();

    // Assert
    expect(within(row as HTMLElement).getByText('還沒有項目')).toBeInTheDocument();
    expect(within(row as HTMLElement).queryByText(/估計/)).not.toBeInTheDocument();
    expect(within(row as HTMLElement).queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('名稱連到專案的表格並篩這個週期', async () => {
    // Arrange
    fakeToday();
    const { user, router } = openPlanning({ cycles: [sprint2] });
    const [row] = await cycleRows();

    // Act
    await user.click(within(row as HTMLElement).getByRole('link', { name: '衝刺 2' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK?cycle=c2');
  });

  it('今天換了一天，狀態跟著換（視窗取得焦點時重取今天）', async () => {
    // Arrange
    fakeToday();
    openPlanning({ cycles: [sprint2] });
    const [row] = await cycleRows();
    expect(within(row as HTMLElement).getByText('進行中')).toBeInTheDocument();

    // Act
    vi.setSystemTime(new Date(2026, 10, 30, 9));
    window.dispatchEvent(new Event('focus'));

    // Assert
    expect(await within(row as HTMLElement).findByText('已結束')).toBeInTheDocument();
  });
});

describe('規劃頁：里程碑', () => {
  it('依到期日列出名稱、到期日、進度與說明；過了到期日還沒做完的標已過期', async () => {
    // Arrange
    fakeToday();
    openPlanning({
      milestones: [
        milestoneOf('v1.0 上線', '2026-12-16', { id: 'm2', description: '第一個對外的版本。', progress: { total: 4, done: 1, canceled: 0, estimateTotal: null, estimateDone: null } }),
        milestoneOf('內測', '2026-11-10', { id: 'm1', progress: { total: 4, done: 2, canceled: 0, estimateTotal: null, estimateDone: null } }),
        milestoneOf('企劃', '2026-11-05', { id: 'm0', progress: { total: 2, done: 2, canceled: 0, estimateTotal: null, estimateDone: null } }),
      ],
    });

    // Act
    const list = await screen.findByRole('list', { name: '里程碑' });
    const rows = within(list).getAllByRole('listitem');

    // Assert
    expect(rows.map((row) => within(row).getByRole('link').textContent)).toEqual(['企劃', '內測', 'v1.0 上線']);
    expect(within(rows[0] as HTMLElement).queryByText('已過期')).not.toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('已過期')).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('到期 2026/11/10')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByText('第一個對外的版本。')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByRole('progressbar', { name: 'v1.0 上線 的完成進度' })).toHaveAttribute('value', '1');
  });

  it('名稱連到專案的表格並篩這個里程碑', async () => {
    // Arrange
    fakeToday();
    const { user, router } = openPlanning({ milestones: [milestoneOf('v1', '2026-12-16', { id: 'm9' })] });
    const list = await screen.findByRole('list', { name: '里程碑' });

    // Act
    await user.click(within(list).getByRole('link', { name: 'v1' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK?milestone=m9');
  });
});

describe('規劃頁：沒有內容與沒有權限', () => {
  it('空白狀態：先說這裡會有什麼，再給「新增週期」「新增里程碑」', async () => {
    // Arrange & Act
    fakeToday();
    openPlanning();

    // Assert
    expect(await screen.findByText('還沒有週期')).toBeInTheDocument();
    expect(screen.getByText('還沒有里程碑')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增週期' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增里程碑' })).toBeInTheDocument();
  });

  it('沒有 item.update：看得到、沒有新增與更多的入口', async () => {
    // Arrange
    fakeToday();
    openPlanning({
      cycles: [sprint2],
      milestones: [milestoneOf('v1', '2026-12-16')],
      detail: smkDetail({ myPermissions: ['item.read'] }),
    });

    // Act
    await cycleRows();

    // Assert
    expect(screen.queryByRole('button', { name: /新增/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /更多動作/ })).not.toBeInTheDocument();
  });

  it('只看自己的項目（執行者）：有 item.update 也沒有新增與更多的入口', async () => {
    // Arrange
    fakeToday();
    openPlanning({
      cycles: [sprint2],
      detail: smkDetail({ myPermissions: ['item.read_own', 'item.create', 'item.update', 'item.transition', 'comment.create', 'blueprint.read'] }),
    });

    // Act
    await cycleRows();

    // Assert
    expect(screen.queryByRole('button', { name: /新增/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /更多動作/ })).not.toBeInTheDocument();
  });

  it('沒有 item.update 又沒有內容：空白狀態只說明，不給新增', async () => {
    // Arrange & Act
    fakeToday();
    openPlanning({ detail: smkDetail({ myPermissions: ['item.read'] }) });

    // Assert
    expect(await screen.findByText('還沒有週期')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /新增/ })).not.toBeInTheDocument();
  });

  it('專案不存在時是找不到頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [] });

    // Act
    renderApp('/projects/NOPE/planning');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: /找不到/ })).toBeInTheDocument();
  });

  it('頁列有專案的連結與「規劃」標題，專案頁有「規劃」的入口', async () => {
    // Arrange
    fakeToday();
    const { user, router } = openPlanning();
    await screen.findByRole('heading', { level: 1, name: '規劃' });
    await user.click(screen.getByRole('link', { name: smkDetail().project.name }));

    // Act
    const link = await screen.findByRole('link', { name: '規劃' });
    await user.click(link);

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK/planning');
  });
});
