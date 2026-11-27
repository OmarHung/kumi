import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { failure } from '../../test/api';
import { makeItemDeletionProposal, serveItemDeletionProposal } from '../../test/assistant';
import { smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { useItem } from '../../api/queries';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { ItemDeletionProposalCard } from './ItemDeletionProposalCard';

function renderCard(proposal = makeItemDeletionProposal(), onApply?: () => Response | null, applied?: Parameters<typeof serveItemDeletionProposal>[2]) {
  const backend = serveItemDeletionProposal(proposal, onApply, applied);
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <ItemDeletionProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
  return { ...view, backend, queryClient };
}

async function card() {
  return screen.findByRole('region', { name: '刪除建議 SMK' });
}

describe('刪除建議的卡片：內容', () => {
  it('標題寫個數與專案；每個項目一列，編號連到項目頁；多個項目時按鈕寫「刪除 2 個」', async () => {
    // Arrange / Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(view.getByText('建議刪除 2 個項目（SMK）')).toBeInTheDocument();
    expect(view.getAllByRole('listitem')).toHaveLength(2);
    expect(view.getByRole('link', { name: 'SMK-45' })).toHaveAttribute('href', '/projects/SMK/items/SMK-45');
    expect(view.getByText('重複的任務乙')).toBeInTheDocument();
    expect(view.getByRole('button', { name: /^刪除 2 個項目：SMK-45、SMK-46$/ })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '不要' })).toBeInTheDocument();
  });

  it('只有一個項目時按鈕寫「刪除」；有 reason 時多一行小字', async () => {
    // Arrange / Act
    renderCard(makeItemDeletionProposal({ items: [{ itemKey: 'SMK-45', title: '重複的任務甲' }], reason: '和 SMK-12 重複' }));

    // Assert
    const view = within(await card());
    expect(view.getByRole('button', { name: '刪除 SMK-45' })).toBeInTheDocument();
    expect(view.getByText('和 SMK-12 重複')).toBeInTheDocument();
  });

  it('讀不到時顯示原因', async () => {
    // Arrange
    server.use(http.get('/v1/ai/item-deletion-proposals/dp-x', () => failure(404, 'ai.item_deletion_proposal_not_found', '找不到這份建議。')));

    // Act
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ItemDeletionProposalCard proposalId="dp-x" />
      </QueryClientProvider>,
    );

    // Assert
    expect(await screen.findByText(/讀不到這份刪除建議：找不到這份建議。/)).toBeInTheDocument();
  });
});

describe('刪除建議的卡片：刪除與不要', () => {
  it('刪除後收成「已刪除 …，可以在「已刪除」復原」並連到已刪除頁，相關查詢失效', async () => {
    // Arrange
    const { user, backend, queryClient } = renderCard();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: /^刪除 2 個項目：SMK-45、SMK-46$/ }));

    // Assert
    const summary = await view.findByText(/^已刪除/);
    await waitFor(() => {
      expect(summary).toHaveTextContent('已刪除 SMK-45、SMK-46，可以在「已刪除」復原');
    });
    expect(view.getByRole('link', { name: '「已刪除」' })).toHaveAttribute('href', '/projects/SMK/deleted');
    expect(backend.applied()).toBe(1);
    expect(view.queryByRole('button', { name: /^刪除 2 個項目：SMK-45、SMK-46$/ })).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: [...queryKeys.project('SMK'), 'items'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.deletedItems('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: [...queryKeys.project('SMK'), 'parent-candidates'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.cycles('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.milestones('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.myWork });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.overviews });
  });

  it('部分失敗：只列真的刪掉的，error 用提示顯示', async () => {
    // Arrange
    const { user } = renderCard(undefined, undefined, { deletedKeys: ['SMK-45'], error: '有 1 筆沒有刪除：SMK-46（還有下層項目）。' });
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: /^刪除 2 個項目：SMK-45、SMK-46$/ }));

    // Assert
    expect(await view.findByText('有 1 筆沒有刪除：SMK-46（還有下層項目）。')).toBeInTheDocument();
    expect(view.getByText(/^已刪除/)).toHaveTextContent('已刪除 SMK-45，');
  });

  it('已經處理過（409）：顯示說明並重讀伺服器的狀態', async () => {
    // Arrange
    const { user, queryClient } = renderCard(makeItemDeletionProposal(), () => failure(409, 'ai.item_deletion_proposal_closed', '關了'));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: /^刪除 2 個項目：SMK-45、SMK-46$/ }));

    // Assert
    expect(await view.findByText('這份建議已經處理過了。')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.itemDeletionProposal('dp-1') });
  });

  it('全部失敗顯示原因，建議維持待處理，照樣可以「不要」', async () => {
    // Arrange
    const { user, backend } = renderCard(makeItemDeletionProposal(), () => failure(403, 'forbidden', '你沒有刪除項目的權限。'));
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: /^刪除 2 個項目：SMK-45、SMK-46$/ }));

    // Assert
    expect(await view.findByText('你沒有刪除項目的權限。')).toBeInTheDocument();
    await user.click(view.getByRole('button', { name: '不要' }));
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });

  it('作廢過的建議顯示「已略過」，沒有按鈕', async () => {
    // Arrange / Act
    renderCard(makeItemDeletionProposal({ status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' }));

    // Assert
    const view = within(await card());
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(view.queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('刪除建議的卡片：項目頁開著', () => {
  function OpenItemPage() {
    const item = useItem('SMK-45');
    return <p>{item.data?.title ?? '讀取中'}</p>;
  }

  it('刪除成功時不重抓被刪的項目（會是 404），上層項目失效', async () => {
    // Arrange
    let itemReads = 0;
    serveItem(detailOf(smkItem({ number: 45, title: '重複的任務甲', parentKey: 'SMK-40' })));
    server.use(
      http.get('/v1/items/SMK-45', () => {
        itemReads += 1;
        return failure(404, 'item.not_found', '找不到');
      }),
    );
    const proposal = makeItemDeletionProposal();
    serveItemDeletionProposal(proposal);
    const queryClient = createQueryClient({ queries: { retry: false } });
    queryClient.setQueryData(queryKeys.item('SMK-45'), { ...detailOf(smkItem({ number: 45, title: '重複的任務甲', parentKey: 'SMK-40' })) });
    queryClient.setQueryData(queryKeys.item('SMK-40'), detailOf(smkItem({ number: 40, title: '上層' })));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const { user } = renderInRouter(
      <QueryClientProvider client={queryClient}>
        <OpenItemPage />
        <ItemDeletionProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );
    const view = within(await card());
    await waitFor(() => {
      expect(itemReads).toBe(0);
    });

    // Act
    await user.click(view.getByRole('button', { name: /^刪除 2 個項目/ }));

    // Assert
    await view.findByText(/^已刪除/);
    expect(itemReads).toBe(0);
    expect(screen.getByText('重複的任務甲')).toBeInTheDocument();
    expect(invalidate).not.toHaveBeenCalledWith({ queryKey: queryKeys.item('SMK-45') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.item('SMK-40') });
  });
});
