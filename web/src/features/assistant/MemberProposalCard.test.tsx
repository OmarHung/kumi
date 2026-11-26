import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { failure } from '../../test/api';
import { makeMemberChange, makeMemberProposal, serveMemberProposal } from '../../test/assistant';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { MemberProposalCard } from './MemberProposalCard';

function renderCard(proposal = makeMemberProposal(), onApply?: () => Response | null, applied?: Parameters<typeof serveMemberProposal>[2]) {
  const backend = serveMemberProposal(proposal, onApply, applied);
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <MemberProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
  return { ...view, backend, queryClient };
}

async function card() {
  return screen.findByRole('region', { name: '成員建議 SMK' });
}

const threeChanges = makeMemberProposal({
  changes: [
    makeMemberChange(),
    makeMemberChange({ personId: 'p-3', displayName: '阿明', role: 'editor', roleName: '編輯者', previousRole: 'contributor', previousRoleName: '執行者' }),
    makeMemberChange({ personId: 'p-4', displayName: '小華', role: null, roleName: null, previousRole: 'viewer', previousRoleName: '檢視者' }),
  ],
});

describe('成員建議的卡片：內容', () => {
  it('標題寫專案代號；加入、改角色、移出三種變動各一列', async () => {
    // Arrange / Act
    renderCard(threeChanges);

    // Assert
    const view = within(await card());
    expect(view.getByText('建議成員變動（SMK）')).toBeInTheDocument();
    expect(view.getAllByRole('listitem')).toHaveLength(3);
    expect(view.getByText('加入為 執行者')).toBeInTheDocument();
    expect(view.getByText('執行者 → 編輯者')).toBeInTheDocument();
    expect(view.getByText('移出專案')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '套用' })).toBeInTheDocument();
    expect(view.getByRole('button', { name: '不要' })).toBeInTheDocument();
  });

  it('角色沒有名稱時用角色的 key', async () => {
    // Arrange / Act
    renderCard(makeMemberProposal({ changes: [makeMemberChange({ roleName: null, role: 'qa' })] }));

    // Assert
    expect(await within(await card()).findByText('加入為 qa')).toBeInTheDocument();
  });

  it('讀不到時顯示原因', async () => {
    // Arrange
    server.use(http.get('/v1/ai/member-proposals/mp-x', () => failure(404, 'ai.member_proposal_not_found', '找不到這份建議。')));

    // Act
    renderInRouter(
      <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
        <MemberProposalCard proposalId="mp-x" />
      </QueryClientProvider>,
    );

    // Assert
    expect(await screen.findByText(/讀不到這份成員建議：找不到這份建議。/)).toBeInTheDocument();
  });
});

describe('成員建議的卡片：套用與不要', () => {
  it('套用後收成「已套用」，專案詳細資料、人員、專案清單失效', async () => {
    // Arrange
    const { user, backend, queryClient } = renderCard();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText(/已套用/)).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
    expect(view.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.project('SMK'), exact: true });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.people });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.projects });
  });

  it('部分失敗：已套用加 error 提示', async () => {
    // Arrange
    const { user } = renderCard(threeChanges, undefined, { error: '小華沒有移出：他還有負責的項目。' });
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('小華沒有移出：他還有負責的項目。')).toBeInTheDocument();
    expect(view.getByText(/已套用/)).toBeInTheDocument();
  });

  it('已經處理過（409）：顯示說明並重讀伺服器的狀態', async () => {
    // Arrange
    const { user, queryClient } = renderCard(makeMemberProposal(), () => failure(409, 'ai.member_proposal_closed', '關了'));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('這份建議已經處理過了。')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.memberProposal('mp-1') });
  });

  it('套用失敗顯示原因，照樣可以「不要」', async () => {
    // Arrange
    const { user, backend } = renderCard(makeMemberProposal(), () => failure(403, 'forbidden', '你沒有管理成員的權限。'));
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('你沒有管理成員的權限。')).toBeInTheDocument();
    await user.click(view.getByRole('button', { name: '不要' }));
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });

  it('處理中再按「套用」或「不要」不會多送一次', async () => {
    // Arrange
    const { user, backend } = renderCard();
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    let applyRequests = 0;
    server.use(
      http.post('/v1/ai/member-proposals/mp-1/apply', async () => {
        applyRequests += 1;
        await gate;
        return failure(403, 'forbidden', '你沒有管理成員的權限。');
      }),
    );
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));
    const working = await view.findByRole('button', { name: '套用中…' });
    await user.click(working);
    await user.click(view.getByRole('button', { name: '不要' }));
    release();

    // Assert
    expect(await view.findByText('你沒有管理成員的權限。')).toBeInTheDocument();
    expect(applyRequests).toBe(1);
    expect(backend.dismissed()).toBe(0);
  });

  it('套用後「我的工作」也失效（自己被加入或移出時範圍會變）', async () => {
    // Arrange
    const { user, queryClient } = renderCard();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText(/已套用/)).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.myWork });
  });

  it('不要遇到 409（別的分頁已處理）：重讀這份建議', async () => {
    // Arrange
    const { user, queryClient } = renderCard();
    server.use(http.post('/v1/ai/member-proposals/mp-1/dismiss', () => failure(409, 'ai.member_proposal_closed', '關了')));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    await waitFor(() => {
      expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.memberProposal('mp-1') });
    });
  });

  it('不要：收成「已略過」', async () => {
    // Arrange
    const { user, backend } = renderCard();
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
    expect(backend.applied()).toBe(0);
  });
});
