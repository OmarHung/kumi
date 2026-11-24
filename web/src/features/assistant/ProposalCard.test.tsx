import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { makeItemProposal, serveItemProposal } from '../../test/assistant';
import { failure, ok } from '../../test/api';
import { exampleMember, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { ProposalCard } from './ProposalCard';

const ITEM = detailOf(smkItem({ number: 12, title: '修正登入頁', state: 'todo', fields: { category: 'bug' } }), '舊的說明');

function renderCard(proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12' }), onApply?: () => Response | null) {
  const backend = serveItemProposal(proposal, onApply);
  serveItem(ITEM);
  server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <ProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
  return { ...view, backend, queryClient };
}

async function card() {
  return screen.findByRole('region', { name: '修改建議 SMK-12' });
}

describe('修改建議的卡片：內容', () => {
  it('標題列連到項目；狀態、負責人、優先度、日期、估計、自訂欄位都用人看得懂的名稱：現在 → 建議', async () => {
    // Arrange
    const proposal = makeItemProposal({
      projectKey: 'SMK',
      itemKey: 'SMK-12',
      changes: [
        { field: 'status', value: 'doing' },
        { field: 'assignee', value: exampleMember.personId },
        { field: 'priority', value: 'urgent' },
        { field: 'dueDate', value: '2026-12-15' },
        { field: 'estimate', value: 3 },
        { field: 'fields.category', value: 'feature' },
      ],
    });

    // Act
    renderCard(proposal);

    // Assert
    const view = within(await card());
    expect(view.getByRole('link', { name: 'SMK-12' })).toHaveAttribute('href', '/projects/SMK/items/SMK-12');
    expect(view.getByText(/建議修改/)).toHaveTextContent('建議修改 SMK-12 修正登入頁');
    await waitFor(() => {
      expect(view.getByText('狀態').nextElementSibling).toHaveTextContent(/待辦 改成 → ?進行中/);
    });
    expect(view.getByText('負責人').nextElementSibling).toHaveTextContent(/範例使用者 改成 → ?範例成員/);
    expect(view.getByText('優先度').nextElementSibling).toHaveTextContent(/未設定 改成 → ?緊急/);
    expect(view.getByText('到期日').nextElementSibling).toHaveTextContent(/（空） 改成 → ?2026\/12\/15/);
    expect(view.getByText('估計').nextElementSibling).toHaveTextContent(/（空） 改成 → ?3/);
    expect(view.getByText('分類').nextElementSibling).toHaveTextContent(/錯誤 改成 → ?新功能/);
  });

  it('null 是「清掉」；說明只寫「改成新的內容」，展開看全文；留言畫成引用', async () => {
    // Arrange
    const proposal = makeItemProposal({
      projectKey: 'SMK',
      itemKey: 'SMK-12',
      changes: [
        { field: 'dueDate', value: null },
        { field: 'description', value: '新的說明內容' },
      ],
      comment: '建議先完成登入再改版',
    });
    const { user } = renderCard(proposal);
    const view = within(await card());

    // Assert
    expect(await view.findByText('改成新的內容')).toBeInTheDocument();
    expect(view.getByText('到期日').nextElementSibling).toHaveTextContent('（清掉）');
    expect(view.queryByText('新的說明內容')).not.toBeInTheDocument();
    expect(view.getByText('留言').nextElementSibling?.querySelector('blockquote')).toHaveTextContent('建議先完成登入再改版');

    // Act
    await user.click(view.getByRole('button', { name: '展開' }));

    // Assert
    expect(await view.findByText('新的說明內容')).toBeInTheDocument();
    await user.click(view.getByRole('button', { name: '收起' }));
    expect(view.queryByText('新的說明內容')).not.toBeInTheDocument();
  });
});

describe('修改建議的卡片：說明與負責人', () => {
  it('清空說明寫「（清掉）」，不寫「改成新的內容」', async () => {
    // Arrange / Act
    renderCard(makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '' }] }));

    // Assert
    const view = within(await card());
    expect(await view.findByText('說明')).toBeInTheDocument();
    expect(view.getByText('說明').nextElementSibling).toHaveTextContent('（清掉）');
    expect(view.queryByText('改成新的內容')).not.toBeInTheDocument();
  });

  it('現在的負責人用項目自己帶的名字（對方已經不是成員也寫得出來）；建議的負責人才查成員清單', async () => {
    // Arrange
    const gone = detailOf(smkItem({ number: 12, assignee: { personId: 'gone-1', displayName: '離開的人' } }));
    serveItem(gone);
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'assignee', value: exampleMember.personId }] });
    serveItemProposal(proposal);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    const queryClient = createQueryClient({ queries: { retry: false } });

    // Act
    renderInRouter(
      <QueryClientProvider client={queryClient}>
        <ProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );

    // Assert
    const view = within(await card());
    await waitFor(() => {
      expect(view.getByText('負責人').nextElementSibling).toHaveTextContent(/離開的人 改成 → ?範例成員/);
    });
  });

  it('說明的「展開」「收起」用按鈕的文字表示狀態，不另外標 aria-expanded（DESIGN.md 第 3 節）', async () => {
    // Arrange
    const { user } = renderCard(makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '新的' }] }));
    const view = within(await card());

    // Act
    const toggle = await view.findByRole('button', { name: '展開' });
    await user.click(toggle);

    // Assert
    const collapse = view.getByRole('button', { name: '收起' });
    expect(collapse).not.toHaveAttribute('aria-expanded');
    expect(view.getByText('新的')).toBeInTheDocument();
  });
});

describe('修改建議的卡片：套用與不要', () => {
  it('套用：呼叫一次、卡片改成「已套用」加欄位名稱，那個項目的查詢失效', async () => {
    // Arrange
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'status', value: 'done' }], comment: '好了' });
    const { user, backend, queryClient } = renderCard(proposal);
    const view = within(await card());
    await waitFor(() => {
      expect(view.getByText('狀態').nextElementSibling).toHaveTextContent(/待辦 改成 → ?完成/);
    });
    await waitFor(() => {
      expect(queryClient.getQueryState(queryKeys.item('SMK-12'))?.isInvalidated).toBe(false);
    });

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText(/已套用/)).toBeInTheDocument();
    expect(view.getByText('狀態、留言')).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
    expect(view.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(queryClient.getQueryState(queryKeys.item('SMK-12'))?.dataUpdateCount).toBeGreaterThan(1);
    });
  });

  it('處理中按鈕不能再按（aria-disabled），不會送出兩次', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12' });
    const backend = serveItemProposal(proposal);
    server.use(
      http.post(`/v1/ai/item-proposals/${proposal.id}/apply`, async () => {
        await gate;
        return ok({ ...proposal, status: 'applied', decidedAt: '2026-11-23T02:05:00Z' });
      }),
    );
    serveItem(ITEM);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    const { user } = renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );
    const view = within(await card());

    // Act
    await user.click(await view.findByRole('button', { name: '套用' }));
    const working = await view.findByRole('button', { name: '套用中…' });
    await user.click(working);
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(working).toHaveAttribute('aria-disabled', 'true');
    expect(backend.dismissed()).toBe(0);
    release();
    expect(await view.findByText(/已套用/)).toBeInTheDocument();
  });

  it('說明衝突：卡片上說明原因，建議還在、可以「不要」', async () => {
    // Arrange
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '新' }] });
    const { user, backend } = renderCard(proposal, () => failure(409, 'item.description_conflict', '這段說明在你編輯時被別人改過了。'));
    const view = within(await card());

    // Act
    await user.click(await view.findByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('這個項目在建議之後被改過，請重新請助理建議。')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '套用' })).toBeInTheDocument();

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });

  it('只顯示最近一次動作的錯誤：套用衝突之後「不要」失敗，換成不要的錯誤', async () => {
    // Arrange
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '新' }] });
    const { user } = renderCard(proposal, () => failure(409, 'item.description_conflict', '衝突'));
    server.use(http.post(`/v1/ai/item-proposals/${proposal.id}/dismiss`, () => failure(403, 'forbidden', '伺服器忙線中。')));
    const view = within(await card());
    await user.click(await view.findByRole('button', { name: '套用' }));
    expect(await view.findByText('這個項目在建議之後被改過，請重新請助理建議。')).toBeInTheDocument();

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText('伺服器忙線中。')).toBeInTheDocument();
    expect(view.queryByText('這個項目在建議之後被改過，請重新請助理建議。')).not.toBeInTheDocument();
  });

  it('部分失敗：已套用，並顯示哪些沒有套用', async () => {
    // Arrange
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', comment: '留言' });
    const backend = serveItemProposal(proposal);
    server.use(
      http.post(`/v1/ai/item-proposals/${proposal.id}/apply`, () =>
        ok({ ...proposal, status: 'applied', decidedAt: '2026-11-23T02:05:00Z', error: '留言沒有加上：你沒有留言的權限。' }),
      ),
    );
    serveItem(ITEM);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    const { user } = renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );
    const view = within(await card());

    // Act
    await user.click(await view.findByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText(/已套用/)).toBeInTheDocument();
    expect(view.getByText('留言沒有加上：你沒有留言的權限。')).toBeInTheDocument();
    expect(backend.applied()).toBe(0);
  });

  it('已經套用或作廢的建議：只有狀態，沒有按鈕', async () => {
    // Arrange / Act
    renderCard(makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', status: 'dismissed', decidedAt: '2026-11-23T02:05:00Z' }));

    // Assert
    const view = within(await card());
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(view.queryByRole('button')).not.toBeInTheDocument();
  });

  it('讀不到建議（別人的、不存在）：寫出原因', async () => {
    // Arrange
    server.use(http.get('/v1/ai/item-proposals/prop-x', () => failure(404, 'ai.item_proposal_not_found', '找不到這份修改建議。')));
    const client = createQueryClient({ queries: { retry: false } });

    // Act
    renderInRouter(
      <QueryClientProvider client={client}>
        <ProposalCard proposalId="prop-x" />
      </QueryClientProvider>,
    );

    // Assert
    expect(await screen.findByText(/找不到這份修改建議/)).toBeInTheDocument();
  });
});
