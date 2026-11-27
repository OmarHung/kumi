import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { queryKeys } from '../../api/queryKeys';
import { failure, ok } from '../../test/api';
import {
  makeCommentProposal,
  makeItemRestoreProposal,
  makePlanningProposal,
  makeProjectChangeProposal,
  serveCommentProposal,
  serveItemRestoreProposal,
  servePlanningProposal,
  serveProjectChangeProposal,
} from '../../test/assistant';
import { smkDetail } from '../../test/fixtures';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { CommentProposalCard } from './CommentProposalCard';
import { ItemRestoreProposalCard } from './ItemRestoreProposalCard';
import { PlanningProposalCard } from './PlanningProposalCard';
import { ProjectChangeProposalCard } from './ProjectChangeProposalCard';

function render(element: ReactElement) {
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(<QueryClientProvider client={queryClient}>{element}</QueryClientProvider>);
  return { ...view, queryClient };
}

describe('週期與里程碑建議的卡片', () => {
  it('每一筆寫動作、週期或里程碑、名稱與日期；刪除的寫裡面的項目會變成沒有', async () => {
    // Arrange
    servePlanningProposal(makePlanningProposal());

    // Act
    render(<PlanningProposalCard proposalId="pl-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));
    expect(view.getByText('新增週期')).toBeInTheDocument();
    expect(view.getByText('十二月週期（2026/12/01 – 2026/12/14）')).toBeInTheDocument();
    expect(view.getByText('刪除里程碑')).toBeInTheDocument();
    expect(view.getByText('第一版上線（到期 2026/12/31）')).toBeInTheDocument();
    expect(view.getByText('裡面的 3 個項目會變成沒有里程碑')).toBeInTheDocument();
  });

  it('修改的寫之前 → 之後', async () => {
    // Arrange
    const values = { startDate: null, endDate: null, description: null };
    servePlanningProposal(
      makePlanningProposal({
        changes: [
          {
            target: 'milestone',
            action: 'update',
            id: 'ms-1',
            before: { name: '上線', dueDate: '2026-12-31', ...values },
            after: { name: '正式上線', dueDate: '2027-01-15', ...values },
            itemCount: null,
            error: null,
          },
        ],
      }),
    );

    // Act
    render(<PlanningProposalCard proposalId="pl-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));
    expect(view.getByText('修改里程碑')).toBeInTheDocument();
    expect(view.getByText('上線（到期 2026/12/31）')).toBeInTheDocument();
    expect(view.getByText('正式上線（到期 2027/01/15）')).toBeInTheDocument();
  });

  it('套用：呼叫一次、收成已套用、週期與里程碑與項目清單失效；某一筆的錯誤也顯示', async () => {
    // Arrange
    const proposal = makePlanningProposal();
    const failed = proposal.changes.map((change, index) => (index === 1 ? { ...change, error: '里程碑已經不存在' } : change));
    const backend = servePlanningProposal(proposal, undefined, { changes: failed, error: '有 1 筆沒套用' });
    const { user, queryClient } = render(<PlanningProposalCard proposalId="pl-1" />);
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText(/^已套用/)).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
    expect(view.getByText('里程碑已經不存在')).toBeInTheDocument();
    expect(view.getByText('有 1 筆沒套用')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.cycles('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.milestones('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: [...queryKeys.project('SMK'), 'items'] });
  });

  it('applying（另一個分頁按的）：寫「套用中…」、沒有按鈕', async () => {
    // Arrange
    servePlanningProposal(makePlanningProposal({ status: 'applying' }));

    // Act
    render(<PlanningProposalCard proposalId="pl-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));
    expect(await view.findByText('套用中…')).toBeInTheDocument();
    expect(view.queryByRole('button')).not.toBeInTheDocument();
  });

  it('不要：呼叫 dismiss、收成已略過', async () => {
    // Arrange
    const backend = servePlanningProposal(makePlanningProposal());
    const { user } = render(<PlanningProposalCard proposalId="pl-1" />);
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/^已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });

  it('已經處理過（409）顯示說明並重讀；讀不到顯示原因', async () => {
    // Arrange
    servePlanningProposal(makePlanningProposal(), () => failure(409, 'ai.planning_proposal_closed', '關了'));
    const { user, queryClient } = render(<PlanningProposalCard proposalId="pl-1" />);
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('這份建議已經處理過了。')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.planningProposal('pl-1') });
  });

  it('讀不到時顯示原因', async () => {
    // Arrange
    server.use(http.get('/v1/ai/planning-proposals/pl-x', () => failure(404, 'ai.planning_proposal_not_found', '找不到這份建議。')));

    // Act
    render(<PlanningProposalCard proposalId="pl-x" />);

    // Assert
    expect(await screen.findByText(/讀不到這份週期與里程碑建議：找不到這份建議。/)).toBeInTheDocument();
  });
});

describe('專案變更建議的卡片', () => {
  it('名稱寫之前 → 之後，封存狀態前後都寫使用中或已封存，說明並排現在的與建議的', async () => {
    // Arrange
    serveProjectChangeProposal(
      makeProjectChangeProposal({
        archived: true,
        changesDescription: true,
        description: '## 部署\n正式機在 example.test',
        before: { name: '舊的名稱', archived: false, description: '舊的說明', descriptionUpdatedAt: '2026-11-01T00:00:00Z' },
        reason: '專案結束了',
      }),
    );

    // Act
    render(<ProjectChangeProposalCard proposalId="pc-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '專案變更建議 SMK' }));
    expect(view.getByText('舊的名稱')).toBeInTheDocument();
    expect(view.getByText('新的名稱')).toBeInTheDocument();
    expect(view.getByText('使用中')).toBeInTheDocument();
    expect(view.getByText('已封存')).toBeInTheDocument();
    expect(view.getByRole('region', { name: '現在的專案說明' })).toHaveTextContent('舊的說明');
    expect(view.getByRole('region', { name: '建議的專案說明' })).toHaveTextContent('正式機在 example.test');
    expect(view.getByText('專案結束了')).toBeInTheDocument();
  });

  it('說明清掉寫「（清掉）」；沒有要改的欄位不出現', async () => {
    // Arrange
    serveProjectChangeProposal(makeProjectChangeProposal({ name: null, changesDescription: true, description: null }));

    // Act
    render(<ProjectChangeProposalCard proposalId="pc-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '專案變更建議 SMK' }));
    expect(view.getByText('（清掉）')).toBeInTheDocument();
    expect(view.queryByText('名稱')).not.toBeInTheDocument();
  });

  it('套用：專案、專案清單、總覽失效；衝突的錯誤原樣顯示', async () => {
    // Arrange
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    serveProjectChangeProposal(makeProjectChangeProposal(), () => failure(409, 'ai.project_change_conflict', '專案說明在建議之後被改過了。'));
    const { user, queryClient } = render(<ProjectChangeProposalCard proposalId="pc-1" />);
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await screen.findByRole('region', { name: '專案變更建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('專案說明在建議之後被改過了。')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.project('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.projects });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.overviews });
  });

  it('套用成功後收成已套用，部分失敗的 error 用提示顯示', async () => {
    // Arrange
    const backend = serveProjectChangeProposal(makeProjectChangeProposal(), undefined, { error: '名稱改了，封存沒成功' });
    const { user } = render(<ProjectChangeProposalCard proposalId="pc-1" />);
    const view = within(await screen.findByRole('region', { name: '專案變更建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText(/^已套用/)).toBeInTheDocument();
    expect(view.getByText('名稱改了，封存沒成功')).toBeInTheDocument();
    expect(backend.applied()).toBe(1);
  });
});

describe('留言建議的卡片', () => {
  it('每一筆寫項目、作者、動作、原本的摘錄；編輯的再寫新的內容', async () => {
    // Arrange
    serveCommentProposal(makeCommentProposal());

    // Act
    render(<CommentProposalCard proposalId="cm-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '留言建議 SMK' }));
    expect(view.getByText('建議處理 2 則留言（SMK）')).toBeInTheDocument();
    expect(view.getByText(/^編輯留言/)).toBeInTheDocument();
    expect(view.getByRole('link', { name: 'SMK-12' })).toHaveAttribute('href', '/projects/SMK/items/SMK-12');
    expect(view.getByText('先等客戶回覆')).toBeInTheDocument();
    expect(view.getByText('客戶已經回覆，可以開始了')).toBeInTheDocument();
    expect(view.getByText(/^刪除留言/)).toBeInTheDocument();
    expect(view.getByText('貼錯地方了')).toBeInTheDocument();
  });

  it('套用：每個項目的活動失效；某一筆的錯誤顯示在那一筆底下', async () => {
    // Arrange
    const proposal = makeCommentProposal();
    const changes = proposal.changes.map((change, index) => (index === 0 ? { ...change, error: '這則留言是匯入的' } : change));
    serveCommentProposal(proposal, undefined, { changes });
    const { user, queryClient } = render(<CommentProposalCard proposalId="cm-1" />);
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await screen.findByRole('region', { name: '留言建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('這則留言是匯入的')).toBeInTheDocument();
    await waitFor(() => {
      expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.activity('SMK-12') });
    });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.activity('SMK-13') });
  });

  it('不要：收成已略過', async () => {
    // Arrange
    const backend = serveCommentProposal(makeCommentProposal());
    const { user } = render(<CommentProposalCard proposalId="cm-1" />);
    const view = within(await screen.findByRole('region', { name: '留言建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/^已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });
});

describe('復原項目建議的卡片', () => {
  it('每個項目寫編號、標題與刪除的時間；按鈕是「復原」', async () => {
    // Arrange
    serveItemRestoreProposal(makeItemRestoreProposal({ reason: '刪錯了' }));

    // Act
    render(<ItemRestoreProposalCard proposalId="ir-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '復原建議 SMK' }));
    expect(view.getByText('建議復原 2 個已刪除的項目（SMK）')).toBeInTheDocument();
    expect(view.getByText('重複的任務甲')).toBeInTheDocument();
    expect(view.getAllByText(/^刪除於 2026\/11\/26/)).toHaveLength(2);
    expect(view.getByText('刪錯了')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '復原' })).toBeInTheDocument();
  });

  it('復原後列出復原的項目、清單與已刪除的查詢失效', async () => {
    // Arrange
    const backend = serveItemRestoreProposal(makeItemRestoreProposal());
    const { user, queryClient } = render(<ItemRestoreProposalCard proposalId="ir-1" />);
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const view = within(await screen.findByRole('region', { name: '復原建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '復原' }));

    // Assert
    const summary = await view.findByText(/^已復原/);
    await waitFor(() => {
      expect(summary).toHaveTextContent('已復原 SMK-45、SMK-46');
    });
    expect(backend.applied()).toBe(1);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.deletedItems('SMK') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.myWork });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.item('SMK-45') });
  });

  it('部分失敗：只列真的復原的，error 顯示；全部失敗維持待處理並顯示原因', async () => {
    // Arrange
    serveItemRestoreProposal(makeItemRestoreProposal(), undefined, { restoredKeys: ['SMK-45'], error: 'SMK-46 的上層還在已刪除裡' });
    const { user } = render(<ItemRestoreProposalCard proposalId="ir-1" />);
    const view = within(await screen.findByRole('region', { name: '復原建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '復原' }));

    // Assert
    expect(await view.findByText('SMK-46 的上層還在已刪除裡')).toBeInTheDocument();
    expect(view.getByText(/^已復原/)).toHaveTextContent('已復原 SMK-45');
  });

  it('全部失敗：原因顯示在卡片上，按鈕還在', async () => {
    // Arrange
    serveItemRestoreProposal(makeItemRestoreProposal(), () => failure(403, 'forbidden', '你沒有復原項目的權限。'));
    const { user } = render(<ItemRestoreProposalCard proposalId="ir-1" />);
    const view = within(await screen.findByRole('region', { name: '復原建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '復原' }));

    // Assert
    expect(await view.findByText('你沒有復原項目的權限。')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '復原' })).toBeInTheDocument();
  });
});

describe('新種類建議卡片：狀態區與重讀', () => {
  it('applying 時定時重讀，伺服器變成 applied 後卡片自己收成已套用', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let current = makePlanningProposal({ status: 'applying' });
    server.use(http.get('/v1/ai/planning-proposals/pl-1', () => ok(current)));
    render(<PlanningProposalCard proposalId="pl-1" />);
    const view = within(await screen.findByRole('region', { name: '週期與里程碑建議 SMK' }));
    expect(await view.findByText('套用中…')).toBeInTheDocument();

    // Act
    current = { ...current, status: 'applied', decidedAt: '2026-11-27T02:05:00Z' };
    await vi.advanceTimersByTimeAsync(2500);

    // Assert
    expect(await view.findByText(/^已套用/)).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('別的分頁已處理（409）：重讀後待處理區塊消失，「已經處理過了」仍留在卡片上', async () => {
    // Arrange
    let current = makeItemRestoreProposal();
    server.use(
      http.get('/v1/ai/item-restore-proposals/ir-1', () => ok(current)),
      http.post('/v1/ai/item-restore-proposals/ir-1/apply', () => {
        current = { ...current, status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' };
        return failure(409, 'ai.item_restore_proposal_closed', '關了');
      }),
    );
    const { user } = render(<ItemRestoreProposalCard proposalId="ir-1" />);
    const view = within(await screen.findByRole('region', { name: '復原建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '復原' }));

    // Assert
    expect(await view.findByText(/^已略過/)).toBeInTheDocument();
    expect(view.getByText('這份建議已經處理過了。')).toBeInTheDocument();
    expect(view.queryByRole('button', { name: '復原' })).not.toBeInTheDocument();
  });

  it('留言建議套用後照樣列出每一筆：成功的標已套用，失敗的顯示原因；刪除的留言沒有摘錄時寫看不到內容', async () => {
    // Arrange
    const proposal = makeCommentProposal();
    const changes = proposal.changes.map((change, index) =>
      index === 0 ? { ...change, error: '這則留言是匯入的' } : { ...change, excerpt: null },
    );
    serveCommentProposal(proposal, undefined, { changes });
    const { user } = render(<CommentProposalCard proposalId="cm-1" />);
    const view = within(await screen.findByRole('region', { name: '留言建議 SMK' }));

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('這則留言是匯入的')).toBeInTheDocument();
    expect(view.getAllByRole('link')).toHaveLength(2);
    expect(view.getByText('已套用', { selector: 'span' })).toBeInTheDocument();
    expect(view.getByText('（已刪除，看不到內容）')).toBeInTheDocument();
  });

  it('三個欄位都沒要改時專案變更卡片不畫空的清單', async () => {
    // Arrange
    serveProjectChangeProposal(makeProjectChangeProposal({ name: null }));

    // Act
    render(<ProjectChangeProposalCard proposalId="pc-1" />);

    // Assert
    const view = within(await screen.findByRole('region', { name: '專案變更建議 SMK' }));
    expect(view.queryByRole('term')).not.toBeInTheDocument();
    expect(view.getByRole('button', { name: '套用' })).toBeInTheDocument();
  });
});
