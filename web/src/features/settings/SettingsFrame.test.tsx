import { screen, waitFor, within } from '@testing-library/react';
import { http, type HttpResponse, type JsonBodyType } from 'msw';
import { beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { serveChangeSets } from '../../test/changesets';
import { exampleOwner, smkDetail, smkProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const STALE = '無法更新專案的最新設定，畫面上可能是舊的。';

let scrollTo: MockInstance<typeof window.scrollTo>;

/** 先不回應這個 GET，release() 才回：看得到頁面自己的「載入中」。 */
function holdRequest(path: string, respond: () => HttpResponse<JsonBodyType>) {
  let isRequested = false;
  let release = (): void => undefined;
  const released = new Promise<void>((resolve) => {
    release = resolve;
  });
  server.use(
    http.get(path, async () => {
      isRequested = true;
      await released;
      return respond();
    }),
  );
  return {
    isRequested: () => isRequested,
    release: () => {
      release();
    },
  };
}

beforeEach(() => {
  scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
});

async function openSettings(detail: ProjectDetail = smkDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const view = renderApp(`/projects/${detail.project.key}/settings`);
  await screen.findByRole('heading', { level: 1, name: '設定' });
  return view;
}

describe('設定頁的外框', () => {
  it('載入中與載入後的分頁標題都是「設定」', async () => {
    // Arrange：先不回專案內容，停在設定頁自己的「載入中」。
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', []);
    const gate = holdRequest('/v1/projects/SMK', () => ok(smkDetail()));

    // Act
    renderApp('/projects/SMK/settings');
    await waitFor(() => {
      expect(gate.isRequested()).toBe(true);
    });
    const whileLoading = document.title;
    gate.release();
    await screen.findByRole('heading', { level: 1, name: '設定' });

    // Assert
    expect(whileLoading.startsWith('設定 · ')).toBe(true);
    expect(document.title.startsWith('設定 · ')).toBe(true);
  });

  it('捨棄修改之後焦點到標題，畫面捲回最上面', async () => {
    // Arrange
    const { user } = await openSettings();
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));
    await user.click(screen.getByRole('button', { name: '捨棄修改' }));
    scrollTo.mockClear();

    // Act
    await user.click(within(screen.getByRole('dialog', { name: '捨棄所有修改？' })).getByRole('button', { name: '捨棄' }));

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('以最新版本重新開始之後焦點到標題，畫面捲回最上面', async () => {
    // Arrange
    const { user, queryClient } = await openSettings();
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail({ project: { ...smkProject, blueprintVersion: 2 } }))));
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK'], exact: true });
    const alert = await screen.findByRole('alert');
    scrollTo.mockClear();

    // Act
    await user.click(within(alert).getByRole('button', { name: '以最新版本重新開始' }));

    // Assert
    expect(await screen.findByText(/藍圖第 2 版/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('背景重抓失敗時草稿留著，提示放在標題下的說明與第一個區塊之間', async () => {
    // Arrange
    const { user, queryClient } = await openSettings();
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));
    server.use(http.get('/v1/projects/SMK', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK'], exact: true });

    // Assert
    const notice = await screen.findByText(STALE);
    expect(screen.getByRole('list', { name: '「任務流程」要刪除的狀態' })).toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
    const intro = screen.getByText(/藍圖第 1 版/);
    const firstSection = screen.getByRole('heading', { level: 2, name: '流程' });
    expect(intro.compareDocumentPosition(notice) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(notice.compareDocumentPosition(firstSection) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
