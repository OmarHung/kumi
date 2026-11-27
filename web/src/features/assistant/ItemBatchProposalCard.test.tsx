import { QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { delay, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { failure, ok } from '../../test/api';
import { makeItemBatchEntry, makeItemBatchProposal, serveItemBatchProposal } from '../../test/assistant';
import { smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { ItemBatchProposalCard } from './ItemBatchProposalCard';

function renderCard(proposal = makeItemBatchProposal(), onApply?: () => Response | null, applied?: Parameters<typeof serveItemBatchProposal>[2]) {
  const backend = serveItemBatchProposal(proposal, onApply, applied);
  serveItem(detailOf(smkItem({ number: 12, title: '修正登入頁', dueDate: '2026-11-20' })));
  serveItem(detailOf(smkItem({ number: 13, title: '改版首頁', dueDate: null })));
  server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <ItemBatchProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
  return { ...view, backend, queryClient };
}

async function card() {
  return screen.findByRole('region', { name: '批次修改建議 SMK' });
}

describe('批次修改建議的卡片：內容', () => {
  it('標題寫個數與專案；每個項目一段，欄位寫「現在 → 建議」；按鈕寫「全部套用」', async () => {
    // Arrange / Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(view.getByText('建議修改 2 個項目（SMK）')).toBeInTheDocument();
    expect(view.getByRole('link', { name: 'SMK-12' })).toHaveAttribute('href', '/projects/SMK/items/SMK-12');
    expect(view.getByText('改版首頁')).toBeInTheDocument();
    expect(await view.findByText(/2026\/11\/20/)).toBeInTheDocument();
    expect(view.getAllByText('到期日', { exact: false }).length).toBeGreaterThanOrEqual(2);
    expect(view.getByRole('button', { name: '全部套用' })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '不要' })).toBeInTheDocument();
  });

  it('只有一個項目時按鈕寫「套用」；留言預設收起，點開才看得到', async () => {
    // Arrange
    const { user } = renderCard(makeItemBatchProposal({ items: [makeItemBatchEntry({ comment: '順延一週' })] }));
    const view = within(await card());

    // Assert
    expect(view.getByRole('button', { name: '套用' })).toBeInTheDocument();
    expect(view.queryByText('順延一週')).not.toBeInTheDocument();

    // Act
    await user.click(view.getByRole('button', { name: '加留言 SMK-12' }));

    // Assert
    expect(await view.findByText('順延一週')).toBeInTheDocument();
  });

  it('讀不到時顯示原因', async () => {
    // Arrange
    server.use(http.get('/v1/ai/item-batch-proposals/bp-x', () => failure(404, 'ai.item_batch_proposal_not_found', '找不到這份建議。')));

    // Act
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ItemBatchProposalCard proposalId="bp-x" />
      </QueryClientProvider>,
    );

    // Assert
    expect(await screen.findByText(/讀不到這份批次修改建議：找不到這份建議。/)).toBeInTheDocument();
  });
});

describe('批次修改建議的卡片：套用與不要', () => {
  it('套用後每個項目打勾，查詢失效', async () => {
    // Arrange
    const { user, backend, queryClient } = renderCard();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部套用' }));

    // Assert
    expect(await view.findAllByText('已套用')).toHaveLength(2);
    expect(backend.applied()).toBe(1);
    expect(view.queryByRole('button', { name: '全部套用' })).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.item('SMK-12') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: [...queryKeys.project('SMK'), 'items'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: [...queryKeys.project('SMK'), 'parent-candidates'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.cycles('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.milestones('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.myWork });
  });

  it('部分失敗：失敗的那一筆顯示原因，整份的 error 用提示', async () => {
    // Arrange
    const proposal = makeItemBatchProposal();
    const failed = proposal.items.map((entry) => (entry.itemKey === 'SMK-13' ? { ...entry, error: '到期日不能早於開始日。' } : entry));
    const { user } = renderCard(proposal, undefined, { items: failed, error: '有 1 個項目沒有完全套用。' });
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部套用' }));

    // Assert
    expect(await view.findByText('其餘已套用；沒套用的：到期日不能早於開始日。')).toBeInTheDocument();
    expect(view.getByText('有 1 個項目沒有完全套用。')).toBeInTheDocument();
    expect(view.getAllByText('已套用')).toHaveLength(1);
  });

  it('已經處理過（409）：顯示說明並重讀伺服器的狀態', async () => {
    // Arrange
    const { user, queryClient } = renderCard(makeItemBatchProposal(), () => failure(409, 'ai.item_batch_proposal_closed', '關了'));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部套用' }));

    // Assert
    expect(await view.findByText('這份建議已經處理過了。')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.itemBatchProposal('bp-1') });
  });

  it('說明被改過（409 衝突）照原文顯示原因，照樣可以「不要」', async () => {
    // Arrange
    const { user, backend } = renderCard(makeItemBatchProposal(), () => failure(409, 'item.description_conflict', '說明被改過'));
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部套用' }));

    // Assert
    expect(await view.findByText('這個項目在建議之後被改過，請重新請助理建議。')).toBeInTheDocument();
    await user.click(view.getByRole('button', { name: '不要' }));
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });
});

describe('批次修改建議的卡片：現在的值與說明', () => {
  it('某個項目讀不到時，那一欄只寫建議的值（沒有「現在 →」），讀得到的照舊', async () => {
    // Arrange
    renderCard();
    server.use(http.get('/v1/items/SMK-13', () => failure(404, 'item.not_found', '找不到')));

    // Act
    const view = within(await card());

    // Assert
    expect(await view.findByText(/2026\/11\/20/)).toBeInTheDocument();
    const [first, second] = view.getAllByRole('listitem');
    expect(within(first as HTMLElement).getAllByText('→').length).toBe(1);
    expect(within(second as HTMLElement).queryByText('→')).not.toBeInTheDocument();
    expect(within(second as HTMLElement).getByText(/2026\/12\/15/)).toBeInTheDocument();
  });

  it('說明預設收起；按鈕名稱帶項目編號並隨狀態變，aria-expanded 跟著，展開才看得到內容', async () => {
    // Arrange
    const entry = makeItemBatchEntry({ changes: [{ field: 'description', value: '新的說明內容' }] });
    const { user } = renderCard(makeItemBatchProposal({ items: [entry] }));
    const view = within(await card());

    // Assert
    const open = view.getByRole('button', { name: '展開說明 SMK-12' });
    expect(open).toHaveAttribute('aria-expanded', 'false');
    expect(view.queryByText('新的說明內容')).not.toBeInTheDocument();

    // Act
    await user.click(open);

    // Assert
    const close = await view.findByRole('button', { name: '收起說明 SMK-12' });
    expect(close).toHaveAttribute('aria-expanded', 'true');
    expect(await view.findByText('新的說明內容')).toBeInTheDocument();

    // Act
    await user.click(close);

    // Assert
    expect(view.getByRole('button', { name: '展開說明 SMK-12' })).toHaveAttribute('aria-expanded', 'false');
    expect(view.queryByText('新的說明內容')).not.toBeInTheDocument();
  });

  it('清空說明時寫「（清掉）」，沒有東西可展開所以不畫展開按鈕', async () => {
    // Arrange
    const entry = makeItemBatchEntry({ changes: [{ field: 'description', value: null }] });
    renderCard(makeItemBatchProposal({ items: [entry] }));

    // Act
    const view = within(await card());

    // Assert
    expect(await view.findByText('（清掉）')).toBeInTheDocument();
    expect(view.queryByRole('button', { name: /展開說明|收起說明/ })).not.toBeInTheDocument();
  });

  it('多個欄位與很長的標題都排得下，每個欄位一行', async () => {
    // Arrange
    const longTitle = '很長的標題'.repeat(30);
    const entry = makeItemBatchEntry({
      itemTitle: longTitle,
      changes: [
        { field: 'dueDate', value: '2026-12-15' },
        { field: 'priority', value: 'high' },
        { field: 'title', value: '新的標題' },
      ],
    });
    renderCard(makeItemBatchProposal({ items: [entry] }));

    // Act
    const view = within(await card());

    // Assert
    expect(await view.findByText(longTitle)).toBeInTheDocument();
    expect(view.getByText('高')).toBeInTheDocument();
    expect(view.getByText('新的標題')).toBeInTheDocument();
    expect(view.getByText('優先度：')).toBeInTheDocument();
  });
});

describe('批次修改建議的卡片：處理中與唯讀', () => {
  it('套用中按鈕寫「套用中…」、兩顆都 aria-disabled，再按不會多送', async () => {
    // Arrange
    const { user, backend } = renderCard();
    server.use(
      http.post('/v1/ai/item-batch-proposals/bp-1/apply', async () => {
        await delay('infinite');
        return ok({});
      }),
    );
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '全部套用' }));

    // Assert
    const working = await view.findByRole('button', { name: '套用中…' });
    expect(working).toHaveAttribute('aria-disabled', 'true');
    expect(view.getByRole('button', { name: '不要' })).toHaveAttribute('aria-disabled', 'true');
    await user.click(working);
    await user.click(view.getByRole('button', { name: '不要' }));
    expect(backend.applied()).toBe(0);
    expect(backend.dismissed()).toBe(0);
  });

  it('載入時就是已套用：唯讀，保留每個項目改了哪些欄位，沒有按鈕', async () => {
    // Arrange / Act
    renderCard(makeItemBatchProposal({ status: 'applied', decidedAt: '2026-11-27T02:05:00Z' }));

    // Assert
    const view = within(await card());
    expect(await view.findAllByText('已套用')).toHaveLength(2);
    expect(view.getAllByText('到期日：')).toHaveLength(2);
    expect(view.getAllByText(/2026\/12\/15/)).toHaveLength(2);
    expect(view.queryByRole('button')).not.toBeInTheDocument();
  });

  it('載入時就是已略過：只顯示已略過，沒有按鈕', async () => {
    // Arrange / Act
    renderCard(makeItemBatchProposal({ status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' }));

    // Assert
    const view = within(await card());
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(view.queryByRole('button')).not.toBeInTheDocument();
    expect(view.queryByRole('listitem')).not.toBeInTheDocument();
  });
});
