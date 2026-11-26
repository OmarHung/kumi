import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { http } from 'msw';
import { ok, signedInAs } from '../../test/api';
import { memberPerson } from '../../test/people';
import { server } from '../../test/server';
import { serveBulkUpdate } from '../../test/bulk';
import { agyDetail, agyItem, exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { stubResizeObserver } from '../../test/resizeObserver';
import { chooseOption, openOptions } from '../../test/select';

const ITEMS: readonly Item[] = [1, 2, 3].map((number) => smkItem({ number, title: `任務 ${String(number)}` }));

interface OpenOptions {
  readonly permissions?: Parameters<typeof smkDetail>[0];
  readonly items?: readonly Item[];
  readonly select?: readonly string[];
}

/** 開 SMK 的表格，勾選指定的項目，回傳動作列。 */
async function openBar({ permissions, items = ITEMS, select = ['SMK-1', 'SMK-2'] }: OpenOptions = {}) {
  signedInAs(exampleOwner, { details: [smkDetail(permissions)] });
  serveItems('SMK', items);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  for (const key of select) {
    await view.user.click(within(table).getByRole('checkbox', { name: `選取 ${key}` }));
  }
  const bar = await screen.findByRole('toolbar', { name: '批次操作' });
  return { ...view, table, bar };
}

/** 動作列上每個控制項的名稱（下拉的按鈕是 combobox，其他是 button）。 */
function controlNames(bar: HTMLElement): (string | null)[] {
  return Array.from(bar.querySelectorAll('button')).map((control) => control.getAttribute('aria-label') ?? control.textContent);
}

function rowOf(table: HTMLElement, key: string): HTMLElement {
  return within(table).getByRole('row', { name: new RegExp(key) });
}

describe('批次動作列：內容與權限', () => {
  it('完整權限：已選幾個、狀態、負責人、優先度、到期日、刪除，最後是取消選取；沒有週期與里程碑時沒有那兩個', async () => {
    // Arrange / Act
    const { bar } = await openBar();

    // Assert
    expect(bar).toHaveTextContent('已選 2 個');
    const names = controlNames(bar);
    expect(names).toEqual(['狀態', '負責人', '優先度', '到期日', '刪除', '取消選取']);
  });

  it('只能改狀態的人只有「狀態」；只能改欄位的人沒有「狀態」與「刪除」；只能刪除的人只有「刪除」', async () => {
    // Arrange / Act
    const transition = await openBar({ permissions: { myPermissions: ['item.read', 'item.transition'] } });
    const onlyTransition = controlNames(transition.bar);
    transition.unmount();
    const update = await openBar({ permissions: { myPermissions: ['item.read', 'item.update'] } });
    const onlyUpdate = controlNames(update.bar);
    update.unmount();
    const remove = await openBar({ permissions: { myPermissions: ['item.read', 'item.delete'] } });
    const onlyDelete = controlNames(remove.bar);

    // Assert
    expect(onlyTransition).toEqual(['狀態', '取消選取']);
    expect(onlyUpdate).toEqual(['負責人', '優先度', '到期日', '取消選取']);
    expect(onlyDelete).toEqual(['刪除', '取消選取']);
  });

  it('專案有週期與里程碑時多兩個動作，排在到期日後面', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', ITEMS);
    serveCycles('SMK', [cycleOf('衝刺 1', '2026-11-16', '2026-11-29')]);
    serveMilestones('SMK', [milestoneOf('上線', '2026-12-31')]);
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });

    // Act
    await user.click(within(table).getByRole('checkbox', { name: '選取 SMK-1' }));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });

    // Assert
    expect(await within(bar).findByRole('combobox', { name: '週期' })).toBeInTheDocument();
    expect(within(bar).getByRole('combobox', { name: '里程碑' })).toBeInTheDocument();
  });
});

describe('批次動作列：送出', () => {
  it('改狀態：送出選取的編號與狀態，列在原位換成新的狀態；全部成功不另外提示，選取留著', async () => {
    // Arrange
    const { user, table, bar } = await openBar();
    const requests = serveBulkUpdate('SMK', ITEMS, { categories: { doing: 'in_progress' } });

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    expect(await within(rowOf(table, 'SMK-1')).findByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(within(rowOf(table, 'SMK-2')).getByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(within(rowOf(table, 'SMK-3')).getByRole('button', { name: '狀態：待辦' })).toBeInTheDocument();
    expect(requests.map((request) => request.body)).toEqual([{ keys: ['SMK-1', 'SMK-2'], state: 'doing' }]);
    expect(screen.queryByText(/沒改/)).not.toBeInTheDocument();
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 2 個');
  });

  it('改負責人：成員，與「沒有負責人」（送 null）；列上換成新的負責人（項目的 assignee 是人，不是 assigneeId）', async () => {
    // Arrange
    const { user, bar, table } = await openBar();
    const requests = serveBulkUpdate('SMK', ITEMS);

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '負責人' }), exampleMember.displayName);
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(await within(rowOf(table, 'SMK-1')).findByRole('button', { name: `負責人：${exampleMember.displayName}` })).toBeInTheDocument();
    await chooseOption(user, within(bar).getByRole('combobox', { name: '負責人' }), '沒有負責人');

    // Assert
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([
        { keys: ['SMK-1', 'SMK-2'], assigneeId: exampleMember.personId },
        { keys: ['SMK-1', 'SMK-2'], assigneeId: null },
      ]);
    });
    expect(await within(rowOf(table, 'SMK-1')).findByRole('button', { name: '負責人：未填' })).toBeInTheDocument();
  });

  it('負責人的選單：不打開不讀人員清單，打開才讀，選項名稱下面寫職稱', async () => {
    // Arrange
    let peopleRequests = 0;
    server.use(
      http.get('/v1/people', () => {
        peopleRequests += 1;
        return ok([{ ...memberPerson, jobTitle: '前端工程師' }]);
      }),
    );
    const { user, bar } = await openBar();
    expect(peopleRequests).toBe(0);

    // Act
    await openOptions(user, within(bar).getByRole('combobox', { name: '負責人' }));

    // Assert
    expect(await screen.findByRole('option', { name: /範例成員/ })).toHaveTextContent('前端工程師');
    expect(peopleRequests).toBe(1);
  });

  it('改優先度', async () => {
    // Arrange
    const { user, bar } = await openBar();
    const requests = serveBulkUpdate('SMK', ITEMS);

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '優先度' }), '高');

    // Assert
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([{ keys: ['SMK-1', 'SMK-2'], priority: 'high' }]);
    });
  });

  it('改到期日：在浮層裡打日期按 Enter 就送出；「清除到期日」送 null；送出後浮層收起來、焦點回到按鈕', async () => {
    // Arrange
    const { user, bar } = await openBar();
    const requests = serveBulkUpdate('SMK', ITEMS);

    // Act
    await user.click(within(bar).getByRole('button', { name: '到期日' }));
    const panel = await screen.findByRole('dialog', { name: '到期日' });
    await user.type(within(panel).getByRole('combobox', { name: '到期日' }), '2026-12-01{Enter}');

    // Assert
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([{ keys: ['SMK-1', 'SMK-2'], dueDate: '2026-12-01' }]);
    });
    expect(screen.queryByRole('dialog', { name: '到期日' })).not.toBeInTheDocument();
    expect(within(bar).getByRole('button', { name: '到期日' })).toHaveFocus();

    // Act
    await user.click(within(bar).getByRole('button', { name: '到期日' }));
    await user.click(await screen.findByRole('button', { name: '清除到期日' }));

    // Assert
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([
        { keys: ['SMK-1', 'SMK-2'], dueDate: '2026-12-01' },
        { keys: ['SMK-1', 'SMK-2'], dueDate: null },
      ]);
    });
  });

  it('改週期與里程碑：選項是專案的週期、里程碑加「沒有…」；選了就送出', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', ITEMS);
    const sprint = cycleOf('衝刺 1', '2026-11-16', '2026-11-29');
    const launch = milestoneOf('上線', '2026-12-31');
    serveCycles('SMK', [sprint]);
    serveMilestones('SMK', [launch]);
    const requests = serveBulkUpdate('SMK', ITEMS);
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('checkbox', { name: '選取 SMK-1' }));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });

    // Act
    const options = await openOptions(user, await within(bar).findByRole('combobox', { name: '週期' }));
    const labels = options.map((option) => option.textContent);
    await chooseOption(user, within(bar).getByRole('combobox', { name: '週期' }), '衝刺 1');
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    await chooseOption(user, within(bar).getByRole('combobox', { name: '里程碑' }), '沒有里程碑');

    // Assert
    expect(labels.map((label) => label.replace('✓', ''))).toEqual(['沒有週期', '衝刺 1']);
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([
        { keys: ['SMK-1'], cycleId: sprint.id },
        { keys: ['SMK-1'], milestoneId: null },
      ]);
    });
  });

  it('選取的項目分屬不同流程：狀態選項依流程分組，同名同 key 的只列一次', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const requirement = agyItem({ number: 1, title: '官網需求' });
    const task = agyItem({ number: 2, type: 'task', title: '切版', state: 'todo', stateCategory: 'not_started' });
    serveItems('AGY', [requirement, task]);
    const { user } = renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('checkbox', { name: '選取全部已載入的項目' }));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });

    // Act
    const options = await openOptions(user, within(bar).getByRole('combobox', { name: '狀態' }));

    // Assert
    const groups = Array.from(document.querySelectorAll('[data-group-label]')).map((label) => label.textContent);
    expect(groups).toEqual(['交付流程', '任務流程']);
    const values = options.map((option) => option.dataset.value);
    expect(values.filter((value) => value === 'closed')).toHaveLength(1);
    expect(values).toEqual(expect.arrayContaining(['confirming', 'todo', 'doing', 'done']));
  });

  it('只有一個流程時狀態選項不分組', async () => {
    // Arrange
    const { user, bar } = await openBar();

    // Act
    const options = await openOptions(user, within(bar).getByRole('combobox', { name: '狀態' }));

    // Assert
    expect(options.map((option) => option.textContent.replace('✓', ''))).toEqual(['待辦', '進行中', '完成']);
    expect(document.querySelector('[data-group-label]')).toBeNull();
  });

  it('送出中：動作列說「處理中…」、動作停住（停得到、不能再按），選取留著', async () => {
    // Arrange
    const { user, bar } = await openBar();
    let release = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const requests = serveBulkUpdate('SMK', ITEMS);
    const { http } = await import('msw');
    const { server } = await import('../../test/server');
    const { ok } = await import('../../test/api');
    server.use(
      http.post('/v1/projects/SMK/items/bulk-update', async () => {
        await gate;
        return ok({ updated: [], failed: [] });
      }),
    );

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    expect(await within(bar).findByText('處理中…')).toBeInTheDocument();
    expect(within(bar).getByRole('combobox', { name: '狀態' })).toHaveAttribute('aria-disabled', 'true');
    expect(within(bar).getByRole('button', { name: '刪除' })).toHaveAttribute('aria-disabled', 'true');
    expect(bar).toHaveTextContent('已選 2 個');
    await act(async () => {
      release();
      await gate;
    });
    await waitFor(() => {
      expect(within(bar).queryByText('處理中…')).not.toBeInTheDocument();
    });
    expect(requests).toHaveLength(0);
  });

  it('選了超過 200 個：動作停用並說明「一次最多 200 個」', async () => {
    // Arrange
    const many = Array.from({ length: 201 }, (_, index) => smkItem({ number: index + 1, title: `任務 ${String(index + 1)}` }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', many);
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });

    // Act
    await user.click(within(table).getByRole('checkbox', { name: '選取全部已載入的項目' }));

    // Assert
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });
    expect(bar).toHaveTextContent('已選 201 個');
    expect(bar).toHaveTextContent('一次最多 200 個');
    for (const name of ['狀態', '負責人', '優先度']) {
      expect(within(bar).getByRole('combobox', { name })).toHaveAttribute('aria-disabled', 'true');
      expect(within(bar).getByRole('combobox', { name })).toHaveAccessibleDescription('一次最多 200 個');
    }
    expect(within(bar).getByRole('button', { name: '刪除' })).toHaveAttribute('aria-disabled', 'true');
    expect(within(bar).getByRole('button', { name: '到期日' })).toHaveAttribute('aria-disabled', 'true');
  }, 20000);
});

describe('批次動作列：有失敗的結果', () => {
  it('部分失敗：表格上方的警告寫改好幾個、幾個沒改，逐筆列出編號（連結）與原因；失敗的留在選取裡', async () => {
    // Arrange
    const { user, bar } = await openBar();
    serveBulkUpdate('SMK', ITEMS, {
      failures: {
        'SMK-2': { code: 'item.transition_fields_required', message: '要先填「摘要」才能移到「進行中」。' },
      },
    });

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    const notice = await screen.findByText(/改好 1 個；1 個沒改：/);
    const container = notice.closest('[data-tone]') as HTMLElement;
    expect(container).toHaveAttribute('data-tone', 'warning');
    expect(container).toHaveAttribute('role', 'status');
    const line = within(container).getByRole('listitem');
    expect(line).toHaveTextContent('SMK-2：要先填「摘要」才能移到「進行中」。');
    expect(within(line).getByRole('link', { name: 'SMK-2' })).toHaveAttribute('href', '/projects/SMK/items/SMK-2');
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 1 個');
    expect(within(screen.getByRole('table', { name: '項目' })).getByRole('checkbox', { name: '選取 SMK-2' })).toBeChecked();
    expect(within(screen.getByRole('table', { name: '項目' })).getByRole('checkbox', { name: '選取 SMK-1' })).not.toBeChecked();
  });

  it('全部失敗：錯誤的提示，可以關掉', async () => {
    // Arrange
    const { user, bar } = await openBar();
    serveBulkUpdate('SMK', ITEMS, {
      failures: {
        'SMK-1': { code: 'item.transition_not_allowed', message: '不能這樣轉。' },
        'SMK-2': { code: 'item.transition_not_allowed', message: '不能這樣轉。' },
      },
    });

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '狀態' }), '完成');

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('2 個都沒改：');
    expect(within(alert).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 2 個');

    // Act
    await user.click(within(alert).getByRole('button', { name: '知道了' }));

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('整個請求失敗（沒有權限）：錯誤的提示說伺服器的訊息，選取不變', async () => {
    // Arrange
    const { user, bar } = await openBar();
    const { http } = await import('msw');
    const { server } = await import('../../test/server');
    const { failure } = await import('../../test/api');
    server.use(http.post('/v1/projects/SMK/items/bulk-update', () => failure(403, 'work.forbidden', '你沒有改狀態的權限。')));

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '狀態' }), '完成');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('你沒有改狀態的權限。');
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 2 個');
  });
});

describe('批次動作列：放不下時收進「更多動作」', () => {
  const BAR = '[role=toolbar][aria-label="批次操作"]';

  async function openNarrow(width: number) {
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', ITEMS);
    serveCycles('SMK', [cycleOf('衝刺 1', '2026-11-16', '2026-11-29')]);
    serveMilestones('SMK', [milestoneOf('上線', '2026-12-31')]);
    const resize = stubResizeObserver();
    const view = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await view.user.click(within(table).getByRole('checkbox', { name: '選取 SMK-1' }));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });
    await within(bar).findByRole('combobox', { name: '週期' });
    resize.report(BAR, width);
    return { ...view, bar };
  }

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('1024 寬：里程碑、週期、到期日依序收起來，刪除與取消選取留在列上', async () => {
    // Arrange / Act
    const { bar } = await openNarrow(752);

    // Assert
    await waitFor(() => {
      expect(within(bar).getByRole('button', { name: '更多動作' })).toBeInTheDocument();
    });
    expect(controlNames(bar)).toEqual(['狀態', '負責人', '優先度', '更多動作', '刪除', '取消選取']);
  });

  it('更多動作的浮層裡是被收起來的動作：下拉選了就送出並收起浮層、焦點回到「更多動作」', async () => {
    // Arrange
    const { user, bar } = await openNarrow(752);
    const requests = serveBulkUpdate('SMK', ITEMS);
    await user.click(await within(bar).findByRole('button', { name: '更多動作' }));
    const panel = await screen.findByRole('dialog', { name: '更多動作' });

    // Act
    await chooseOption(user, within(panel).getByRole('combobox', { name: '里程碑' }), '沒有里程碑');

    // Assert
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([{ keys: ['SMK-1'], milestoneId: null }]);
    });
    expect(screen.queryByRole('dialog', { name: '更多動作' })).not.toBeInTheDocument();
    expect(within(bar).getByRole('button', { name: '更多動作' })).toHaveFocus();
  });

  it('到期日收進去之後：浮層裡直接是日期欄位與「清除到期日」', async () => {
    // Arrange
    const { user, bar } = await openNarrow(752);
    const requests = serveBulkUpdate('SMK', ITEMS);
    await user.click(await within(bar).findByRole('button', { name: '更多動作' }));
    const panel = await screen.findByRole('dialog', { name: '更多動作' });

    // Act
    await user.type(within(panel).getByRole('combobox', { name: '到期日' }), '2026-12-01{Enter}');

    // Assert
    await waitFor(() => {
      expect(requests.map((request) => request.body)).toEqual([{ keys: ['SMK-1'], dueDate: '2026-12-01' }]);
    });
    expect(within(panel).queryByRole('button', { name: '清除到期日' })).not.toBeInTheDocument();
  });

  it('夠寬時全部排在列上，沒有「更多動作」；變窄再變寬會換回來', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', ITEMS);
    serveCycles('SMK', [cycleOf('衝刺 1', '2026-11-16', '2026-11-29')]);
    serveMilestones('SMK', [milestoneOf('上線', '2026-12-31')]);
    const resize = stubResizeObserver();
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('checkbox', { name: '選取 SMK-1' }));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });
    await within(bar).findByRole('combobox', { name: '週期' });

    // Act
    resize.report(BAR, 1100);

    // Assert
    expect(within(bar).queryByRole('button', { name: '更多動作' })).not.toBeInTheDocument();
    expect(controlNames(bar)).toEqual(['狀態', '負責人', '優先度', '到期日', '週期', '里程碑', '刪除', '取消選取']);

    // Act
    resize.report(BAR, 600);

    // Assert
    expect(within(bar).getByRole('button', { name: '更多動作' })).toBeInTheDocument();

    // Act
    resize.report(BAR, 1100);

    // Assert
    expect(within(bar).queryByRole('button', { name: '更多動作' })).not.toBeInTheDocument();
  });
});
