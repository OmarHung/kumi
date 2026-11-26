import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { failure, ok } from '../../test/api';
import { makeNewItemDraft, makeNewItemProposal, serveNewItemProposal } from '../../test/assistant';
import { exampleMember, smkDetail } from '../../test/fixtures';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { NewItemsProposalCard } from './NewItemsProposalCard';

function renderCard(proposal = makeNewItemProposal(), onApply?: () => Response | null, applied?: Parameters<typeof serveNewItemProposal>[2]) {
  const backend = serveNewItemProposal(proposal, onApply, applied);
  server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <NewItemsProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
  return { ...view, backend, queryClient };
}

async function card() {
  return screen.findByRole('region', { name: '新增項目建議 SMK' });
}

describe('新增項目建議的卡片：內容', () => {
  it('標題列寫幾個項目與專案代號，每個項目一列，有「全部建立」與「不要」', async () => {
    // Arrange / Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(view.getByText(/建議新增 2 個項目/)).toHaveTextContent('建議新增 2 個項目（SMK）');
    expect(view.getAllByRole('listitem')).toHaveLength(2);
    expect(view.getByText('盤點 GCP 資源')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '全部建立' })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '不要' })).toBeInTheDocument();
  });

  it('類型用藍圖的名稱；負責人、優先度、日期、估計、自訂欄位列在第二行', async () => {
    // Arrange
    const proposal = makeNewItemProposal({
      items: [
        makeNewItemDraft({
          assigneeId: exampleMember.personId,
          priority: 'high',
          startDate: '2026-12-01',
          dueDate: '2026-12-15',
          estimate: 3,
          fields: { category: 'bug' },
        }),
      ],
    });

    // Act
    renderCard(proposal);

    // Assert
    const view = within(await card());
    expect(await view.findByText('任務')).toBeInTheDocument();
    await waitFor(() => {
      expect(view.getByText(/負責人：/)).toHaveTextContent('負責人：範例成員 · 優先度：高 · 開始日：2026/12/01 · 到期日：2026/12/15 · 估計：3 · 分類：錯誤');
    });
  });

  it('掛在既有項目底下的寫編號並連到項目；掛在前面項目底下的寫「在「標題」底下」並縮排', async () => {
    // Arrange
    const proposal = makeNewItemProposal({
      items: [
        makeNewItemDraft({ title: '總計畫', ref: 'plan', parentKey: 'SMK-3' }),
        makeNewItemDraft({ title: '遷移資料庫', parentRef: 'plan' }),
        makeNewItemDraft({ title: '驗證備份', ref: 'verify', parentRef: 'plan' }),
        makeNewItemDraft({ title: '寫報告', parentRef: 'verify' }),
      ],
    });

    // Act
    renderCard(proposal);

    // Assert
    const view = within(await card());
    const rows = view.getAllByRole('listitem');
    expect(within(rows[0] as HTMLElement).getByRole('link', { name: 'SMK-3' })).toHaveAttribute('href', '/projects/SMK/items/SMK-3');
    expect(rows[0]?.className).not.toMatch(/depth/);
    expect(rows[1]).toHaveTextContent('在「總計畫」底下');
    expect(rows[1]?.className).toMatch(/depth1/);
    expect(rows[3]).toHaveTextContent('在「驗證備份」底下');
    expect(rows[3]?.className).toMatch(/depth2/);
  });

  it('說明預設收起，按「展開說明」看全文（Markdown，連結列出網域），再按收起', async () => {
    // Arrange
    const { user } = renderCard(makeNewItemProposal({ items: [makeNewItemDraft({ description: '## 範圍\n\n- 見 [文件](https://example.com/doc)' })] }));
    const view = within(await card());
    expect(view.queryByRole('heading', { name: '範圍' })).not.toBeInTheDocument();

    // Act
    await user.click(view.getByRole('button', { name: '展開說明' }));

    // Assert
    expect(await view.findByRole('heading', { name: '範圍' })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '收起說明' })).toHaveAttribute('aria-expanded', 'true');
    expect(view.getByText(/example\.com/, { selector: 'span, small, a' })).toBeInTheDocument();
    await user.click(view.getByRole('button', { name: '收起說明' }));
    expect(view.queryByRole('heading', { name: '範圍' })).not.toBeInTheDocument();
  });

  it('只有一個項目時按鈕寫「建立」，標題寫 1 個項目', async () => {
    // Arrange / Act
    renderCard(makeNewItemProposal({ items: [makeNewItemDraft()] }));

    // Assert
    const view = within(await card());
    expect(view.getByRole('button', { name: '建立' })).toBeInTheDocument();
    expect(view.queryByRole('button', { name: '全部建立' })).not.toBeInTheDocument();
    expect(view.getByText(/建議新增 1 個項目/)).toBeInTheDocument();
  });
});

describe('新增項目建議的卡片：全部建立', () => {
  it('建立後收成「已建立」加每個編號的連結，並讓清單、我的工作、專案清單失效', async () => {
    // Arrange
    const { user, backend, queryClient } = renderCard();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部建立' }));

    // Assert
    expect((await view.findByRole('link', { name: 'SMK-13' })).closest('p')).toHaveTextContent(/^已建立 SMK-13、SMK-14/);
    expect(backend.applied()).toBe(1);
    expect(view.getByRole('link', { name: 'SMK-13' })).toHaveAttribute('href', '/projects/SMK/items/SMK-13');
    expect(view.getByRole('link', { name: 'SMK-14' })).toHaveAttribute('href', '/projects/SMK/items/SMK-14');
    expect(view.queryByRole('button', { name: '全部建立' })).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.items('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.myWork });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.projects });
  });

  it('部分失敗：只列建立成功的編號，error 用提示顯示', async () => {
    // Arrange
    const { user } = renderCard(makeNewItemProposal(), undefined, { createdKeys: ['SMK-13', null], error: '「遷移資料庫」沒有建立：沒有權限。' });
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部建立' }));

    // Assert
    expect((await view.findByRole('link', { name: 'SMK-13' })).closest('p')).toHaveTextContent(/^已建立 SMK-13/);
    expect(view.queryByRole('link', { name: 'SMK-14' })).not.toBeInTheDocument();
    expect(view.getByText('「遷移資料庫」沒有建立：沒有權限。')).toBeInTheDocument();
  });

  it('已經處理過（409）：顯示說明並重讀伺服器的狀態', async () => {
    // Arrange
    const { user, queryClient } = renderCard(makeNewItemProposal(), () => failure(409, 'ai.new_item_proposal_closed', '關了'));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部建立' }));

    // Assert
    expect(await view.findByText('這份建議已經處理過了。')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.newItemProposal('nip-1') });
  });

  it('第一個就失敗：顯示原因，建議維持待處理，可以再按或不要', async () => {
    // Arrange
    const { user, backend } = renderCard(makeNewItemProposal(), () => failure(403, 'forbidden', '你沒有建立項目的權限。'));
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部建立' }));

    // Assert
    expect(await view.findByText('你沒有建立項目的權限。')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '全部建立' })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '不要' })).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
  });
});

describe('新增項目建議的卡片：不要與其他狀態', () => {
  it('按「不要」收成「已略過」', async () => {
    // Arrange
    const { user, backend } = renderCard();
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
    expect(view.queryByRole('button', { name: '全部建立' })).not.toBeInTheDocument();
  });

  it('applying（別的分頁按的）：顯示「建立中…」、沒有按鈕，伺服器變成 applied 後自動換成已建立', async () => {
    // Arrange
    let current = makeNewItemProposal({ status: 'applying' });
    server.use(
      http.get('/v1/ai/new-item-proposals/nip-1', () => ok(current)),
      http.get('/v1/projects/SMK', () => ok(smkDetail())),
    );
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const queryClient = createQueryClient({ queries: { retry: false } });
    renderInRouter(
      <QueryClientProvider client={queryClient}>
        <NewItemsProposalCard proposalId="nip-1" />
      </QueryClientProvider>,
    );
    const view = within(await card());
    expect(view.getByText('建立中…')).toBeInTheDocument();
    expect(view.queryByRole('button')).not.toBeInTheDocument();

    // Act
    current = makeNewItemProposal({ status: 'applied', createdKeys: ['SMK-13', 'SMK-14'], decidedAt: '2026-11-26T02:05:00Z' });
    await vi.advanceTimersByTimeAsync(2100);

    // Assert
    expect((await view.findByRole('link', { name: 'SMK-13' })).closest('p')).toHaveTextContent(/^已建立 SMK-13、SMK-14/);
    vi.useRealTimers();
  });

  it('已套用但一個都沒建立（過程中斷）：寫「沒有建立任何項目」並顯示說明', async () => {
    // Arrange / Act
    renderCard(makeNewItemProposal({ status: 'applied', decidedAt: '2026-11-26T02:05:00Z', error: '建立的過程中斷了。' }));

    // Assert
    const view = within(await card());
    expect(await view.findByText(/^沒有建立任何項目/)).toBeInTheDocument();
    expect(view.getByText('建立的過程中斷了。')).toBeInTheDocument();
  });

  it('讀不到建議（404）時顯示提示', async () => {
    // Arrange
    server.use(http.get('/v1/ai/new-item-proposals/nip-x', () => failure(404, 'ai.new_item_proposal_not_found', '找不到這份建議。')));
    const queryClient = createQueryClient({ queries: { retry: false } });

    // Act
    renderInRouter(
      <QueryClientProvider client={queryClient}>
        <NewItemsProposalCard proposalId="nip-x" />
      </QueryClientProvider>,
    );

    // Assert
    expect(await screen.findByText(/讀不到這份新增項目建議/)).toBeInTheDocument();
  });
});
