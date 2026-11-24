import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import type { ChangeSetDetail } from '../../api/types';
import { ownerRef } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { makeChangeSet, makeChangeSetDetail, serveChangeSet, serveChangeSets, serveDecision } from '../../test/changesets';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const pending = makeChangeSetDetail(simpleTasksBlueprint, {
  changeSet: makeChangeSet({ number: 3, summary: '拿掉進行中', proposedBy: ownerRef }),
  changes: [{ kind: 'removed', resource: 'state', key: 'doing', parentKey: 'task_flow', changedProperties: [] }],
  impacts: [{ kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'todo', affected: 12, blocksApply: false }],
});

const STALE = '無法更新這份變更集的最新內容，畫面上可能是舊的。';

let scrollTo: MockInstance<typeof window.scrollTo>;

beforeEach(() => {
  scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
});

function openPending() {
  signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read', 'blueprint.read', 'blueprint.propose'] })] });
  serveChangeSet('SMK', pending);
  return renderApp('/projects/SMK/changesets/3');
}

describe('變更集預覽的外框', () => {
  it('載入中與載入後的分頁標題都是「變更集 #3」', async () => {
    // Arrange：先不回這份變更集，停在預覽頁自己的「載入中」。
    signedInAs(exampleOwner, { details: [smkDetail()] });
    let isRequested = false;
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/projects/SMK/changesets/3', async () => {
        isRequested = true;
        await released;
        return ok(pending);
      }),
    );

    // Act
    renderApp('/projects/SMK/changesets/3');
    await waitFor(() => {
      expect(isRequested).toBe(true);
    });
    const whileLoading = document.title;
    release();
    await screen.findByRole('heading', { level: 1, name: '變更集 #3' });

    // Assert
    expect(whileLoading.startsWith('變更集 #3 · ')).toBe(true);
    expect(document.title.startsWith('變更集 #3 · ')).toBe(true);
  });

  it('放棄之後焦點到標題，畫面捲回最上面', async () => {
    // Arrange
    const { user } = openPending();
    serveChangeSets('SMK', []);
    const rejected: ChangeSetDetail = {
      ...pending,
      changeSet: { ...pending.changeSet, status: 'rejected', decidedBy: ownerRef, decidedAt: '2026-11-15T03:00:00Z' },
      canApply: false,
    };
    serveDecision('SMK', 3, 'reject', () => ok(rejected));
    await user.click(await screen.findByRole('button', { name: '放棄' }));
    scrollTo.mockClear();

    // Act
    await user.click(within(screen.getByRole('dialog', { name: '放棄變更集 #3？' })).getByRole('button', { name: '放棄' }));

    // Assert
    expect(await screen.findByText('已放棄變更集 #3，設定沒有改變。')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '變更集 #3' })).toHaveFocus();
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('背景重抓失敗時內容留著，提示放在標題前面固定的位置', async () => {
    // Arrange
    const { queryClient } = openPending();
    const heading = await screen.findByRole('heading', { level: 1, name: '變更集 #3' });
    server.use(http.get('/v1/projects/SMK/changesets/3', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK', 'changesets', 3] });

    // Assert
    const notice = await screen.findByText(STALE);
    expect(screen.getByRole('heading', { level: 1, name: '變更集 #3' })).toBe(heading);
    expect(screen.getByText('拿掉進行中')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: '摘要' })).toBeInTheDocument();
    expect(notice.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
