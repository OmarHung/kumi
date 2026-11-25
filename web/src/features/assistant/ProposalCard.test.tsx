import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
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

  it('null 是「清掉」；說明一開始就展開，並排顯示現在的與建議的；留言畫成引用', async () => {
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
    renderCard(proposal);
    const view = within(await card());

    // Assert
    expect(await view.findByText('新的說明內容')).toBeInTheDocument();
    expect(view.getByRole('region', { name: '建議的說明' })).toHaveTextContent('新的說明內容');
    await waitFor(() => {
      expect(view.getByRole('region', { name: '現在的說明' })).toHaveTextContent('舊的說明');
    });
    expect(view.queryByRole('button', { name: '展開' })).not.toBeInTheDocument();
    expect(view.getByText('到期日').nextElementSibling).toHaveTextContent('（清掉）');
    expect(view.getByText('留言').nextElementSibling?.querySelector('blockquote')).toHaveTextContent('建議先完成登入再改版');
  });

  it('現在沒有說明時現在的那一格寫「（空）」', async () => {
    // Arrange
    serveItem(detailOf(smkItem({ number: 12 }), null));
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '全新的說明' }] });
    serveItemProposal(proposal);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );
    const view = within(await card());

    // Assert
    await waitFor(() => {
      expect(view.getByRole('region', { name: '現在的說明' })).toHaveTextContent('（空）');
    });
    expect(view.getByRole('region', { name: '建議的說明' })).toHaveTextContent('全新的說明');
  });

  it('讀不到項目時沒有「現在的」，建議的照樣完整顯示', async () => {
    // Arrange
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '只有建議的' }] });
    serveItemProposal(proposal);
    server.use(
      http.get('/v1/items/SMK-12', () => failure(404, 'item.not_found', '找不到這個項目。')),
      http.get('/v1/projects/SMK', () => ok(smkDetail())),
    );
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );
    const view = within(await card());

    // Assert
    expect(await view.findByRole('region', { name: '建議的說明' })).toHaveTextContent('只有建議的');
    expect(view.queryByRole('region', { name: '現在的說明' })).not.toBeInTheDocument();
  });

  it('建議的說明裡新出現的外部連結（實際的網域）與提及列在上方；原本就有的不算', async () => {
    // Arrange
    const before = '看[舊文件](https://docs.example.org/a)';
    serveItem(detailOf(smkItem({ number: 12 }), before));
    const after = `${before}，另見[說明](https://example.com/x)與 https://sub.example.net/y，請 @[隨便寫的名字](mention:${exampleMember.personId}) 看`;
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: after }] });
    serveItemProposal(proposal);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <ProposalCard proposalId={proposal.id} />
      </QueryClientProvider>,
    );
    const view = within(await card());

    // Assert
    expect(await view.findByText('這份說明會新增 2 個連結：example.com、sub.example.net；提及 1 個人：範例成員。')).toBeInTheDocument();
  });

  it('沒有新的連結與提及時不顯示那一行', async () => {
    // Arrange
    renderCard(makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: '只改文字' }] }));
    const view = within(await card());

    // Assert
    expect(await view.findByRole('region', { name: '建議的說明' })).toBeInTheDocument();
    expect(view.queryByText(/這份說明會新增/)).not.toBeInTheDocument();
  });

  it('有留言時說明送出後看得到這個項目的人都看得到', async () => {
    // Arrange
    renderCard(makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'title', value: '新標題' }], comment: '一則留言' }));
    const view = within(await card());

    // Assert
    expect(await view.findByText('留言送出後，看得到這個項目的人都看得到。')).toBeInTheDocument();
  });

  it('沒有留言時不寫這句', async () => {
    // Arrange
    renderCard(makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'title', value: '新標題' }], comment: null }));
    const view = within(await card());

    // Assert
    expect(await view.findByText('標題')).toBeInTheDocument();
    expect(view.queryByText(/留言送出後/)).not.toBeInTheDocument();
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

function renderDescriptionCard(after: string, serveProject: () => Promise<Response> | Response = () => ok(smkDetail())) {
  const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', changes: [{ field: 'description', value: after }] });
  serveItemProposal(proposal);
  server.use(http.get('/v1/projects/SMK', serveProject));
  return renderInRouter(
    <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
      <ProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
}

describe('修改建議的卡片：說明的連結摘要在資料不全時不誤導', () => {
  it('讀不到項目現在的說明：列出建議裡全部的連結，不說「新增」', async () => {
    // Arrange
    server.use(http.get('/v1/items/SMK-12', () => failure(404, 'item.not_found', '找不到這個項目。')));

    // Act
    renderDescriptionCard('見 https://example.com/a');

    // Assert
    const view = within(await card());
    expect(await view.findByText('這份說明裡有 1 個連結：example.com。')).toBeInTheDocument();
    expect(view.queryByText(/新增/)).not.toBeInTheDocument();
  });

  it('專案成員還沒載入：只說提及幾個人，載入之後才寫名字', async () => {
    // Arrange
    serveItem(ITEM);
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });

    // Act
    renderDescriptionCard(`請 [某人](mention:${exampleMember.personId}) 看`, async () => {
      await gate;
      return ok(smkDetail());
    });

    // Assert
    const view = within(await card());
    expect(await view.findByText('這份說明會提及 1 個人。')).toBeInTheDocument();
    expect(view.queryByText(/不是成員的人/)).not.toBeInTheDocument();
    release();
    expect(await view.findByText('這份說明會提及 1 個人：範例成員。')).toBeInTheDocument();
  });
});

describe('修改建議的卡片：說明的格子很高時', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  function stubHeights(scrollHeight: number, clientHeight: number) {
    vi.spyOn(Element.prototype, 'scrollHeight', 'get').mockReturnValue(scrollHeight);
    vi.spyOn(Element.prototype, 'clientHeight', 'get').mockReturnValue(clientHeight);
  }

  it('內容溢出：格子下面說還有更多，格子可以用鍵盤聚焦捲動', async () => {
    // Arrange
    serveItem(ITEM);
    stubHeights(500, 224);

    // Act
    renderDescriptionCard('很長的說明');

    // Assert
    const view = within(await card());
    const box = await view.findByRole('region', { name: '建議的說明' });
    expect((await view.findAllByText('內容較長，捲動看完整的說明')).length).toBeGreaterThan(0);
    expect(box).toHaveAttribute('tabindex', '0');
    expect(box).toHaveAccessibleDescription('內容較長，捲動看完整的說明');
  });

  it('內容放得下：沒有提示，也不多一個 Tab 停駐點', async () => {
    // Arrange
    serveItem(ITEM);
    stubHeights(100, 100);

    // Act
    renderDescriptionCard('短的說明');

    // Assert
    const view = within(await card());
    const box = await view.findByRole('region', { name: '建議的說明' });
    expect(view.queryByText('內容較長，捲動看完整的說明')).not.toBeInTheDocument();
    expect(box).not.toHaveAttribute('tabindex');
  });
});
