import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { ProjectDetail } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { captureProposals, invalidChangeSet, makeChangeSet, makeChangeSetDetail, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner, smkDetail, smkProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
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
  return { ...view, proposals };
}

async function addState(user: ReturnType<typeof renderApp>['user'], workflow: string, name: string) {
  await user.click(screen.getByRole('button', { name: `在「${workflow}」新增狀態` }));
  const dialog = screen.getByRole('dialog', { name: '新增狀態' });
  await user.type(within(dialog).getByLabelText('名稱'), name);
  await user.click(within(dialog).getByRole('button', { name: '加入' }));
}

describe('設定頁：伺服器擋下時', () => {
  it('changeset.invalid 的每一個問題標在對應的那一列；對不到的列在送出列', async () => {
    // Arrange
    const details = [
      { code: 'workflow.unreachable_state', path: '/blueprint/workflows/0/states/5', message: '流程「delivery」的狀態「review」無法從起始狀態「confirming」走到。' },
      { code: 'changeset.move_unknown_target', path: '/migrations/moveStates/0/to', message: '搬移的目的狀態不在新藍圖的流程裡。' },
      { code: 'automation.unknown_state', path: '/blueprint/automations/0/actions/0/to', message: '自動化「合併請求合併後轉內部驗收」要轉去的狀態不存在。' },
    ];
    const { user, router, proposals } = await openSettings(agyDetail(), () => invalidChangeSet(details));
    await addState(user, '交付流程', 'Review');
    await user.click(screen.getByRole('button', { name: '刪除「內部驗收」' }));
    await chooseOption(user, screen.getByRole('combobox', { name: '停在「內部驗收」的項目搬到' }), '開發');
    await user.click(screen.getByRole('checkbox', { name: '從「開發」到「客戶驗收」（交付流程）' }));

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器檢查後擋下了這份變更（3 個問題），請看標示的地方。');
    expect(proposals[0]?.body.blueprint.workflows[0]?.states[5]?.key).toBe('review');
    expect(screen.getByRole('textbox', { name: '「review」的名稱' })).toHaveAccessibleDescription(details[0]?.message);
    expect(screen.getByRole('combobox', { name: '停在「內部驗收」的項目搬到' })).toHaveAccessibleDescription(details[1]?.message);
    expect(within(screen.getByRole('list', { name: '其他問題' })).getByRole('listitem')).toHaveTextContent(details[2]?.message ?? '');
    expect(currentPath(router)).toBe('/projects/AGY/settings/workflows');
  });

  it('轉換的問題（包含要先填的欄位）標在那一格與表格下方，不在欄位那一列', async () => {
    // Arrange
    const details = [
      {
        code: 'transition.field_missing_on_type',
        path: '/blueprint/workflows/0/transitions/7/requireFields/0',
        message: '類型「change_request」使用流程「delivery」，但沒有這個轉換要求先填的欄位「client_signoff_date」。',
      },
    ];
    const { user } = await openSettings(agyDetail(), () => invalidChangeSet(details));
    const name = screen.getByRole('textbox', { name: '「結案」的名稱' });
    await user.clear(name);
    await user.type(name, '完成結案');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器檢查後擋下了這份變更（1 個問題）');
    const delivery = screen.getByRole('region', { name: '交付流程' });
    expect(within(delivery).getByRole('checkbox', { name: '從「客戶驗收」到「結案」（交付流程）' })).toHaveAccessibleDescription(
      expect.stringContaining(details[0]?.message ?? ''),
    );
    expect(within(delivery).getByText(`「客戶驗收」→「完成結案」：${details[0]?.message ?? ''}`)).toBeInTheDocument();
    await showSection(user, '欄位');
    expect(screen.getByRole('textbox', { name: '「客戶驗收日」的名稱' })).not.toHaveAccessibleDescription(details[0]?.message);
    expect(screen.queryByRole('list', { name: '其他問題' })).not.toBeInTheDocument();
  });

  it('摘要最多 500 個字：輸入框擋下更多的字；伺服器說摘要有問題時標在摘要旁邊', async () => {
    // Arrange
    const details = [{ code: 'changeset.summary_too_long', path: '/summary', message: '摘要最多 500 個字。' }];
    const { user } = await openSettings(smkDetail(), () => invalidChangeSet(details));
    await addState(user, '任務流程', '審查中');
    const summary = screen.getByLabelText('摘要');
    await user.type(summary, '拆開審查');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(summary).toHaveAttribute('maxlength', '500');
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器檢查後擋下了這份變更（1 個問題），請看標示的地方。');
    expect(summary).toHaveAccessibleDescription(expect.stringContaining('摘要最多 500 個字。'));
    expect(screen.queryByRole('list', { name: '其他問題' })).not.toBeInTheDocument();
  });

  it('其他失敗（例如沒有權限）顯示後端的訊息，草稿留著', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail(), () => failure(403, 'work.forbidden', '你沒有權限修改這個專案的設定。'));
    await addState(user, '任務流程', '審查中');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('你沒有權限修改這個專案的設定。');
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
  });

  it('摘要有控制字元時不送出，說明在摘要旁邊', async () => {
    // Arrange
    const { user, proposals } = await openSettings(smkDetail());
    await addState(user, '任務流程', '審查中');
    const summary = screen.getByLabelText('摘要');
    await user.click(summary);
    await user.paste(`拆開${String.fromCharCode(0)}審查`);

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 1 個地方要修正');
    expect(summary).toHaveAccessibleDescription(expect.stringContaining('摘要不能有控制字元。'));
    expect(proposals).toHaveLength(0);
  });

  it('送出中按鈕停用並顯示「送出中…」', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { user } = await openSettings(smkDetail(), () => failure(500, 'server.error', 'x'));
    server.use(
      http.post('/v1/projects/SMK/changesets', async () => {
        await held;
        return failure(422, 'changeset.invalid', '變更集沒有通過檢查。', []);
      }),
    );
    await addState(user, '任務流程', '審查中');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(screen.getByRole('button', { name: '送出中…' })).toBeDisabled();
    release();
    expect(await screen.findByRole('alert')).toHaveTextContent('變更集沒有通過檢查。');
  });
});

describe('設定頁：離開與捨棄', () => {
  it('有沒送出的修改時離開要先確認：留下來保留草稿，確認之後才離開', async () => {
    // Arrange
    const { user, router } = await openSettings(smkDetail());
    await addState(user, '任務流程', '審查中');
    const nav = screen.getByRole('navigation', { name: '主要導覽' });

    // Act
    await user.click(within(nav).getByRole('link', { name: '我的工作' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '還有沒送出的修改' });
    await user.click(within(dialog).getByRole('button', { name: '留下來' }));
    expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    await user.click(within(nav).getByRole('link', { name: '我的工作' }));
    await user.click(within(await screen.findByRole('dialog', { name: '還有沒送出的修改' })).getByRole('button', { name: '捨棄修改並離開' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });

  it('沒有修改時直接離開，不必確認', async () => {
    // Arrange
    const { user, router } = await openSettings(smkDetail());

    // Act
    await user.click(within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('重新整理或關掉分頁時，有修改才請瀏覽器提醒', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    const before = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(before);
    await addState(user, '任務流程', '審查中');

    // Act
    const after = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(after);

    // Assert
    expect(before.defaultPrevented).toBe(false);
    expect(after.defaultPrevented).toBe(true);
  });

  it('捨棄修改：確認之後回到目前的設定', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));

    // Act
    await user.click(screen.getByRole('button', { name: '捨棄修改' }));
    const dialog = screen.getByRole('dialog', { name: '捨棄所有修改？' });
    await user.click(within(dialog).getByRole('button', { name: '捨棄' }));

    // Assert
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: '「任務流程」要刪除的狀態' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
  });
});

describe('設定頁：別人先改了設定', () => {
  it('沒有修改時直接換成最新的一版；有修改時說明過時，可以捨棄後重新開始', async () => {
    // Arrange
    const first = smkDetail();
    const newer = smkDetail({ project: { ...smkProject, blueprintVersion: 2 } });
    const { user, queryClient } = await openSettings(first);
    let current = first;
    server.use(http.get('/v1/projects/SMK', () => ok(current)));
    expect(screen.getByText(/藍圖第 1 版/)).toBeInTheDocument();

    // Act
    current = newer;
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK'], exact: true });

    // Assert
    expect(await screen.findByText(/藍圖第 2 版/)).toBeInTheDocument();
    await addState(user, '任務流程', '審查中');
    current = smkDetail({ project: { ...smkProject, blueprintVersion: 3 } });
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK'], exact: true });
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('別人已經套用了新版本：設定在你編輯時已經更新為第 3 版，你的修改是以第 2 版為準');
    await user.click(within(alert).getByRole('button', { name: '以最新版本重新開始' }));
    expect(await screen.findByText(/藍圖第 3 版/)).toBeInTheDocument();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
  });

  it('送出時伺服器說版本過時：重抓專案、說明別人已經套用了新版本，以最新版本重新開始之後送得出去', async () => {
    // Arrange
    const [flow] = simpleTasksBlueprint.workflows;
    if (flow === undefined) throw new Error('沒有流程。');
    const renamed: ProjectBlueprint = {
      ...simpleTasksBlueprint,
      workflows: [{ ...flow, states: flow.states.map((state) => (state.key === 'todo' ? { ...state, name: '待處理' } : state)) }],
    };
    const newer = smkDetail({ project: { ...smkProject, blueprintVersion: 2 }, blueprint: renamed });
    const mismatch = { code: 'plan.base_version_mismatch', path: '/baseVersion', message: '變更集以第 1 版為準，但專案目前是第 2 版。' };
    const { user, router, proposals } = await openSettings(smkDetail(), (body) =>
      body.baseVersion === 1
        ? invalidChangeSet([mismatch])
        : ok(makeChangeSetDetail(body.blueprint, { baseBlueprint: renamed, changeSet: makeChangeSet({ number: 8, baseVersion: 2 }) }), 201),
    );
    let projectReads = 0;
    server.use(
      http.get('/v1/projects/SMK', () => {
        projectReads += 1;
        return ok(newer);
      }),
    );
    await addState(user, '任務流程', '審查中');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    const restart = await screen.findByRole('button', { name: '以最新版本重新開始' });
    const notice = restart.closest('[role="alert"]');
    expect(notice).toHaveTextContent('別人已經套用了新版本');
    expect(notice).toHaveFocus();
    await waitFor(() => {
      expect(notice).toHaveTextContent('設定在你編輯時已經更新為第 2 版，你的修改是以第 1 版為準，送不出去。');
    });
    expect(projectReads).toBeGreaterThan(0);
    expect(screen.queryByRole('list', { name: '其他問題' })).not.toBeInTheDocument();
    expect(screen.getByText(/別人已經套用了新版本，這份修改送不出去/)).toBeInTheDocument();

    await user.click(restart);
    expect(await screen.findByText(/藍圖第 2 版/)).toBeInTheDocument();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '「待處理」的名稱' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '以最新版本重新開始' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();

    await addState(user, '任務流程', '審查中');
    await user.click(screen.getByRole('button', { name: '預覽變更' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/changesets/8');
    });
    expect(proposals.map((proposal) => proposal.body.baseVersion)).toEqual([1, 2]);
    expect(proposals[1]?.body.blueprint.workflows[0]?.states[0]?.name).toBe('待處理');
  });
});

describe('設定頁：重新開始讀不到最新的設定', () => {
  it('說明讀不到，草稿留著，可以再按一次', async () => {
    // Arrange
    const { user, queryClient } = await openSettings(smkDetail());
    const newer = smkDetail({ project: { ...smkProject, blueprintVersion: 2 } });
    let isDown = false;
    server.use(http.get('/v1/projects/SMK', () => (isDown ? failure(503, 'server.not_ready', 'starting') : ok(newer))));
    await addState(user, '任務流程', '審查中');
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK'], exact: true });
    const restart = await screen.findByRole('button', { name: '以最新版本重新開始' });
    isDown = true;

    // Act
    await user.click(restart);

    // Assert
    const notice = restart.closest('[role="alert"]');
    expect(await within(notice instanceof HTMLElement ? notice : document.body).findByText('伺服器暫時無法處理，請稍後再試。')).toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    expect(restart).toBeEnabled();
  });
});

describe('設定頁：套用後導回的提示', () => {
  it('提示放在一直掛著的 status 裡（先空著，再填入），焦點到頁面的標題', async () => {
    // Arrange
    const { router } = await openSettings(smkDetail());
    const status = screen.getByRole('status');
    expect(status).toBeEmptyDOMElement();

    // Act
    await router.navigate('/projects/SMK/settings', { state: { notice: '已套用變更集 #3，藍圖是第 2 版。' } });

    // Assert
    await waitFor(() => {
      expect(status).toHaveTextContent('已套用變更集 #3，藍圖是第 2 版。');
    });
    expect(screen.getByRole('status')).toBe(status);
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
  });
});
