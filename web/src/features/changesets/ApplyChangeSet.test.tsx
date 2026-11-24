import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ChangeSetDetail, ProjectDetail } from '../../api/types';
import { memberRef, ownerRef, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { makeChangeSet, makeChangeSetDetail, serveChangeSet, serveChangeSets, serveDecision } from '../../test/changesets';
import { exampleOwner, smkDetail, smkItem, smkProject } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const pending = makeChangeSetDetail(simpleTasksBlueprint, {
  changeSet: makeChangeSet({ number: 3, summary: '拿掉進行中', proposedBy: ownerRef }),
  changes: [{ kind: 'removed', resource: 'state', key: 'doing', parentKey: 'task_flow', changedProperties: [] }],
  impacts: [
    { kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'todo', affected: 12, blocksApply: false },
    { kind: 'field_drop', field: 'notes', affected: 8, blocksApply: false },
  ],
});

const applied: ChangeSetDetail = {
  ...pending,
  changeSet: { ...pending.changeSet, status: 'applied', appliedVersion: 2, decidedBy: ownerRef, decidedAt: '2026-11-15T03:00:00Z' },
  impacts: pending.impacts.map((impact) => ({ ...impact, affected: 0 })),
  canApply: false,
};

function openChangeSet(detail: ChangeSetDetail, project: ProjectDetail = smkDetail()) {
  signedInAs(exampleOwner, { details: [project] });
  const served = serveChangeSet('SMK', detail);
  const view = renderApp(`/projects/SMK/changesets/${String(detail.changeSet.number)}`);
  return { ...view, served };
}

describe('套用變更集', () => {
  it('確認對話框說明會動到多少項目、會是第幾版、不能一鍵還原；取消不送出', async () => {
    // Arrange
    const { user } = openChangeSet(pending);
    const decision = serveDecision('SMK', 3, 'apply', () => ok(applied));

    // Act
    await user.click(await screen.findByRole('button', { name: '套用' }));

    // Assert
    const dialog = screen.getByRole('dialog', { name: '套用變更集 #3？' });
    expect(dialog).toHaveAccessibleDescription('藍圖會存成第 2 版，既有的項目照計畫一次搬移完成。');
    expect(within(within(dialog).getByRole('list', { name: '會動到的資料' })).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '12 個項目會從「進行中」搬到「待辦」',
      '8 個項目的「備註」會被封存',
    ]);
    expect(dialog).toHaveTextContent('這個動作不能一鍵還原');
    await user.click(within(dialog).getByRole('button', { name: '取消' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '套用' })).toHaveFocus();
    expect(decision.count()).toBe(0);
  });

  it('套用成功：專案、項目清單、項目頁、我的工作的快取都失效，回到設定頁並說是第幾版', async () => {
    // Arrange
    const task = detailOf(smkItem({ number: 7 }));
    let project = smkDetail();
    let myWorkReads = 0;
    signedInAs(exampleOwner, { details: [project] });
    server.use(
      http.get('/v1/projects/SMK', () => ok(project)),
      http.get('/v1/my-work', () => {
        myWorkReads += 1;
        return ok({ items: [] });
      }),
    );
    const itemRequests = serveItems('SMK', [task]);
    serveItem(task);
    const activity = serveActivity(task.key, []);
    const served = serveChangeSet('SMK', pending);
    serveChangeSets('SMK', []);
    const decision = serveDecision('SMK', 3, 'apply', () => {
      project = smkDetail({ project: { ...smkProject, blueprintVersion: 2 } });
      served.set(applied);
      return ok(applied);
    });
    const { user, router } = renderApp('/my-work');
    await waitFor(() => { expect(myWorkReads).toBe(1); });
    await router.navigate('/projects/SMK/items/SMK-7');
    await waitFor(() => { expect(activity.requests).toHaveLength(1); });
    await router.navigate('/projects/SMK');
    await waitFor(() => { expect(itemRequests).toHaveLength(1); });
    await router.navigate('/projects/SMK/changesets/3');

    // Act
    await user.click(await screen.findByRole('button', { name: '套用' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: '套用' }));

    // Assert
    expect(await screen.findByText('已套用變更集 #3，藍圖是第 2 版。')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('已套用變更集 #3，藍圖是第 2 版。');
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
    expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    expect(screen.getByText(/藍圖第 2 版/)).toBeInTheDocument();
    expect(decision.count()).toBe(1);
    await router.navigate('/projects/SMK');
    await waitFor(() => { expect(itemRequests).toHaveLength(2); });
    await router.navigate('/projects/SMK/items/SMK-7');
    await waitFor(() => { expect(activity.requests).toHaveLength(2); });
    await router.navigate('/my-work');
    await waitFor(() => { expect(myWorkReads).toBe(2); });
  });

  it('changeset.outdated：關上對話框，說明別的變更先套用了，重新讀這份變更集', async () => {
    // Arrange
    const { user, served } = openChangeSet(pending);
    serveChangeSets('SMK', []);
    serveDecision('SMK', 3, 'apply', () => {
      served.set({ ...pending, changeSet: { ...pending.changeSet, status: 'expired', decidedAt: '2026-11-15T03:00:00Z' }, canApply: false });
      return failure(409, 'changeset.outdated', '基準版本已經不是目前的版本。');
    });

    // Act
    await user.click(await screen.findByRole('button', { name: '套用' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: '套用' }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('別的變更已經先套用，這份變更集已經過期，請到設定頁重新編輯。');
    expect(within(alert).getByRole('link', { name: '到設定頁重新編輯' })).toHaveAttribute('href', '/projects/SMK/settings');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await within(screen.getByRole('list', { name: '摘要' })).findByText('已過期')).toBeInTheDocument();
    expect(served.reads()).toBe(2);
    expect(screen.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
    expect(alert).toHaveFocus();
  });

  it('changeset.apply_blocked：列出每一個原因，重新讀這份變更集', async () => {
    // Arrange
    const { user, served } = openChangeSet(pending);
    serveChangeSets('SMK', []);
    serveDecision('SMK', 3, 'apply', () =>
      failure(409, 'changeset.apply_blocked', '現在套用不了。', [
        { code: 'changeset.too_many_items', path: null, message: '要搬移的項目超過 5,000 個。' },
      ]),
    );

    // Act
    await user.click(await screen.findByRole('button', { name: '套用' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: '套用' }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('現在的資料讓這份變更集套用不了：要搬移的項目超過 5,000 個。');
    await waitFor(() => { expect(served.reads()).toBe(2); });
    expect(alert).toHaveFocus();
    await user.click(within(alert).getByRole('button', { name: '知道了' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '變更集 #3' })).toHaveFocus();
  });

  it('changeset.busy：留在對話框裡說明專案正在被修改，可以再試一次', async () => {
    // Arrange
    const { user, router } = openChangeSet(pending);
    serveChangeSets('SMK', []);
    let attempts = 0;
    const decision = serveDecision('SMK', 3, 'apply', () => {
      attempts += 1;
      return attempts === 1 ? failure(409, 'changeset.busy', 'Could not acquire lock.') : ok(applied);
    });
    await user.click(await screen.findByRole('button', { name: '套用' }));
    const dialog = screen.getByRole('dialog');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '套用' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('專案正在被修改，請稍後再試。');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(within(dialog).getByRole('button', { name: '套用' })).toBeEnabled();
    await user.click(within(dialog).getByRole('button', { name: '套用' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    });
    expect(decision.count()).toBe(2);
  });

  it('其他失敗留在對話框裡說明，可以再試', async () => {
    // Arrange
    const { user } = openChangeSet(pending);
    serveDecision('SMK', 3, 'apply', () => failure(503, 'server.not_ready', 'starting'));

    // Act
    await user.click(await screen.findByRole('button', { name: '套用' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: '套用' }));

    // Assert
    const dialog = screen.getByRole('dialog');
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
    expect(within(dialog).getByRole('button', { name: '套用' })).toBeEnabled();
  });
});

describe('放棄變更集', () => {
  it('提出的人可以放棄：確認之後狀態變成已放棄，按鈕不見', async () => {
    // Arrange
    const { user } = openChangeSet(pending, smkDetail({ myPermissions: ['item.read', 'blueprint.read', 'blueprint.propose'] }));
    serveChangeSets('SMK', []);
    const rejected: ChangeSetDetail = {
      ...pending,
      changeSet: { ...pending.changeSet, status: 'rejected', decidedBy: ownerRef, decidedAt: '2026-11-15T03:00:00Z' },
      canApply: false,
    };
    const decision = serveDecision('SMK', 3, 'reject', () => ok(rejected));

    // Act
    await user.click(await screen.findByRole('button', { name: '放棄' }));
    const dialog = screen.getByRole('dialog', { name: '放棄變更集 #3？' });
    await user.click(within(dialog).getByRole('button', { name: '放棄' }));

    // Assert
    expect(await screen.findByText('已放棄變更集 #3，設定沒有改變。')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '變更集 #3' })).toHaveFocus();
    expect(within(screen.getByRole('list', { name: '摘要' })).getByText('已放棄')).toBeInTheDocument();
    expect(decision.count()).toBe(1);
    expect(screen.queryByRole('button', { name: '放棄' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
  });

  it('別人提出、我又沒有 blueprint.apply：沒有任何按鈕', async () => {
    // Arrange
    const others = { ...pending, changeSet: { ...pending.changeSet, proposedBy: memberRef }, canApply: false };

    // Act
    openChangeSet(others, smkDetail({ myPermissions: ['item.read', 'blueprint.read', 'blueprint.propose'] }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '變更集 #3' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '放棄' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: '摘要' })).getByText('要有核准權限的人才能套用')).toBeInTheDocument();
  });

  it('有 blueprint.apply 的人可以放棄別人提出的', async () => {
    // Arrange & Act
    openChangeSet({ ...pending, changeSet: { ...pending.changeSet, proposedBy: memberRef } });

    // Assert
    expect(await screen.findByRole('button', { name: '放棄' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '套用' })).toBeInTheDocument();
  });
});
