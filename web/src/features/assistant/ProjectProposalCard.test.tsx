import { QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { failure, ok } from '../../test/api';
import { makeProjectProposal, serveProjectProposal } from '../../test/assistant';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { OPEN_ASSISTANT_PANEL_EVENT } from '../assistant-panel/panelRequests';
import { ProjectProposalCard } from './ProjectProposalCard';

function renderCard(proposal = makeProjectProposal(), onApply?: () => Response | null, path = '/') {
  const backend = serveProjectProposal(proposal, onApply);
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <ProjectProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
    path,
  );
  return { ...view, backend, queryClient };
}

async function card() {
  return screen.findByRole('region', { name: '新專案建議 WEB' });
}

describe('新專案建議的卡片：內容', () => {
  it('列出代號、名稱、模板與是否附範例，說明用 Markdown 顯示', async () => {
    // Arrange / Act
    renderCard(makeProjectProposal({ description: '## 網址\n\n- 測試機：**example**', includeExamples: true }));

    // Assert
    const view = within(await card());
    expect(view.getByText('代號').nextElementSibling).toHaveTextContent('WEB');
    expect(view.getByText('名稱').nextElementSibling).toHaveTextContent('網站改版');
    expect(view.getByText('模板').nextElementSibling).toHaveTextContent('簡單任務');
    expect(view.getByText('範例項目').nextElementSibling).toHaveTextContent('附範例項目');
    expect(await view.findByRole('heading', { name: '網址' })).toBeInTheDocument();
    expect(view.getByText('example').tagName).toBe('STRONG');
    expect(view.getByRole('button', { name: '建立專案' })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '不要' })).toBeInTheDocument();
  });

  it('有說明時「說明」只出現一次（欄位名稱，說明框裡不再重複一個小標題）', async () => {
    // Arrange / Act
    renderCard(makeProjectProposal({ description: '目標：三月底前上線' }));

    // Assert
    const view = within(await card());
    expect(await view.findByText('目標：三月底前上線')).toBeInTheDocument();
    expect(view.getAllByText('說明')).toHaveLength(1);
  });

  it('沒有說明時不畫說明那一格；沒附範例寫「不附範例」', async () => {
    // Arrange / Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(view.queryByText('說明')).not.toBeInTheDocument();
    expect(view.getByText('範例項目').nextElementSibling).toHaveTextContent('不附範例');
  });
});

describe('新專案建議的卡片：建立專案', () => {
  it('建立後顯示「已建立」與兩個去處，並讓專案清單失效', async () => {
    // Arrange
    const { user, backend, queryClient } = renderCard();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '建立專案' }));

    // Assert
    expect(await view.findByText(/已建立/)).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
    expect(view.getByRole('link', { name: '到專案 WEB' })).toHaveAttribute('href', '/projects/WEB');
    expect(view.getByRole('button', { name: '在這個專案開 AI 助理' })).toBeInTheDocument();
    expect(view.queryByRole('button', { name: '建立專案' })).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.projects });
  });

  it('「在這個專案開 AI 助理」通知浮動面板切到那個專案', async () => {
    // Arrange
    const { user } = renderCard(makeProjectProposal({ status: 'applied', createdProjectKey: 'WEB', decidedAt: '2026-11-26T02:05:00Z' }));
    const heard: string[] = [];
    const listen = (event: Event) => {
      heard.push((event as CustomEvent<{ projectKey: string }>).detail.projectKey);
    };
    window.addEventListener(OPEN_ASSISTANT_PANEL_EVENT, listen);

    // Act
    await user.click(await screen.findByRole('button', { name: '在這個專案開 AI 助理' }));

    // Assert
    window.removeEventListener(OPEN_ASSISTANT_PANEL_EVENT, listen);
    expect(heard).toEqual(['WEB']);
  });

  it('整頁的 AI 助理裡（面板收起來）：改成連到專案自己的 AI 助理頁', async () => {
    // Arrange / Act
    renderCard(makeProjectProposal({ status: 'applied', createdProjectKey: 'WEB', decidedAt: '2026-11-26T02:05:00Z' }), undefined, '/assistant');

    // Assert
    expect(await screen.findByRole('link', { name: '在這個專案開 AI 助理' })).toHaveAttribute('href', '/projects/WEB/assistant');
  });

  it('不是管理員（403）：照伺服器的說明顯示，建議維持待處理、可以再試', async () => {
    // Arrange
    const { user, backend } = renderCard(makeProjectProposal(), () => failure(403, 'forbidden', '只有組織管理員可以建立專案。'));
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '建立專案' }));

    // Assert
    expect(await view.findByText('只有組織管理員可以建立專案。')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '建立專案' })).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
  });

  it('代號被用掉（409）：顯示說明，改請助理換一個代號', async () => {
    // Arrange
    const { user } = renderCard(makeProjectProposal(), () => failure(409, 'project.key_taken', '代號 WEB 已經有人用了。'));
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '建立專案' }));

    // Assert
    expect(await view.findByText(/代號 WEB 已經有人用了。/)).toBeInTheDocument();
    expect(view.getByText('請在對話裡請助理換一個代號。')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '建立專案' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('代號被用掉之後再按「建立專案」不會再送出；其他錯誤（403）照舊可以重試', async () => {
    // Arrange
    const taken = renderCard(makeProjectProposal(), () => failure(409, 'project.key_taken', '代號 WEB 已經有人用了。'));
    const takenView = within(await card());
    await taken.user.click(takenView.getByRole('button', { name: '建立專案' }));
    await takenView.findByText('請在對話裡請助理換一個代號。');

    // Act
    await taken.user.click(takenView.getByRole('button', { name: '建立專案' }));

    // Assert
    expect(taken.backend.applied()).toBe(1);
    taken.unmount();
    const forbidden = renderCard(makeProjectProposal(), () => failure(403, 'forbidden', '只有組織管理員可以建立專案。'));
    const forbiddenView = within(await card());
    await forbidden.user.click(forbiddenView.getByRole('button', { name: '建立專案' }));
    await forbiddenView.findByText('只有組織管理員可以建立專案。');
    expect(forbiddenView.queryByText('請在對話裡請助理換一個代號。')).not.toBeInTheDocument();
    expect(forbiddenView.getByRole('button', { name: '建立專案' })).not.toHaveAttribute('aria-disabled');
  });

  it('已經處理過（409）：照實說，並重讀成伺服器現在的狀態（已建立）', async () => {
    // Arrange：別的分頁已經建立；GET 在套用失敗之後回 applied
    let isApplied = false;
    const pending = makeProjectProposal();
    const done = makeProjectProposal({ status: 'applied', decidedAt: '2026-11-26T02:05:00Z', createdProjectKey: 'WEB' });
    server.use(
      http.get('/v1/ai/project-proposals/pp-1', () => ok(isApplied ? done : pending)),
      http.post('/v1/ai/project-proposals/pp-1/apply', () => {
        isApplied = true;
        return failure(409, 'ai.project_proposal_closed', '這份建議已經處理過了。');
      }),
    );
    const queryClient = createQueryClient({ queries: { retry: false } });
    const { user } = renderInRouter(
      <QueryClientProvider client={queryClient}>
        <ProjectProposalCard proposalId="pp-1" />
      </QueryClientProvider>,
    );
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '建立專案' }));

    // Assert
    expect(await view.findByText(/已建立/)).toBeInTheDocument();
    expect(view.getByRole('link', { name: '到專案 WEB' })).toBeInTheDocument();
    expect(view.queryByRole('button', { name: '建立專案' })).not.toBeInTheDocument();
  });
});

describe('新專案建議的卡片：不要', () => {
  it('按「不要」顯示「已略過」，不建立專案', async () => {
    // Arrange
    const { user, backend } = renderCard();
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
    expect(backend.applied()).toBe(0);
    expect(view.queryByRole('link', { name: '到專案 WEB' })).not.toBeInTheDocument();
  });
});

describe('新專案建議的卡片：讀取', () => {
  it('讀不到時說明原因，不是空白', async () => {
    // Arrange
    server.use(http.get('/v1/ai/project-proposals/gone', () => failure(404, 'ai.project_proposal_not_found', '找不到這份建議。')));

    // Act
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ProjectProposalCard proposalId="gone" />
      </QueryClientProvider>,
    );

    // Assert
    expect(await screen.findByText(/讀不到這份新專案建議：找不到這份建議。/)).toBeInTheDocument();
  });
});
