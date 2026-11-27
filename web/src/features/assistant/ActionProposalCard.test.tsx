import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { invalidateForActionKind } from '../../api/actionProposals';
import { queryKeys } from '../../api/queryKeys';
import { failure, ok } from '../../test/api';
import { makeActionProposal, makeActionStep, serveActionProposal } from '../../test/assistant';
import { renderInRouter } from '../../test/renderInRouter';
import { server } from '../../test/server';
import { ActionProposalCard } from './ActionProposalCard';

function renderCard(proposal = makeActionProposal()) {
  const queryClient = createQueryClient({ queries: { retry: false } });
  const view = renderInRouter(
    <QueryClientProvider client={queryClient}>
      <ActionProposalCard proposalId={proposal.id} />
    </QueryClientProvider>,
  );
  return { ...view, queryClient };
}

afterEach(() => {
  vi.useRealTimers();
});

const card = () => screen.findByRole('region', { name: /^動作建議/ });

const passwordProposal = makeActionProposal({
  kind: 'people',
  title: '建議新增人員',
  steps: [makeActionStep({ label: '新增人員', target: '王小明', after: '成員' })],
  inputs: [{ key: 'password:0', label: '王小明的初始密碼', kind: 'password', hint: '至少 12 個字元' }],
});

describe('動作建議卡片：內容', () => {
  it('標題、原因、每一步的標籤、對象與之前 → 之後、備註', async () => {
    // Arrange
    serveActionProposal(
      makeActionProposal({
        reason: '讓新人看得到流程',
        steps: [makeActionStep({ label: '改名', target: '空間 HR', before: '人資', after: '人力資源', note: '連結不會變' })],
      }),
    );

    // Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(view.getByText('建議整理知識庫')).toBeInTheDocument();
    expect(view.getByText('讓新人看得到流程')).toBeInTheDocument();
    expect(view.getByText('改名')).toBeInTheDocument();
    expect(view.getByText('空間 HR')).toBeInTheDocument();
    expect(view.getByText('人資')).toBeInTheDocument();
    expect(view.getByText('人力資源')).toBeInTheDocument();
    expect(view.getByText('連結不會變')).toBeInTheDocument();
  });

  it('markdown 的步驟用說明差異的方式列出現在的與建議的', async () => {
    // Arrange
    serveActionProposal(makeActionProposal({ steps: [makeActionStep({ format: 'markdown', before: '舊的**內文**', after: '新的內文' })] }));

    // Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(view.getByRole('region', { name: /現在的內容/ })).toBeInTheDocument();
    expect(within(view.getByRole('region', { name: /建議的內容/ })).getByText('新的內文')).toBeInTheDocument();
  });

  it('套用後：完成的步驟打勾並有連結，失敗的步驟在底下顯示原因', async () => {
    // Arrange
    serveActionProposal(
      makeActionProposal({
        status: 'applied',
        decidedAt: '2026-11-27T02:05:00Z',
        steps: [makeActionStep({ done: true, link: '/knowledge/HR/12' }), makeActionStep({ label: '搬移頁面', error: '看不到那個頁面' })],
      }),
    );

    // Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(await view.findByText(/^已套用/, { selector: 'p' })).toBeInTheDocument();
    expect(view.getByText('已套用', { selector: 'span' })).toBeInTheDocument();
    expect(view.getByRole('link', { name: '查看 入職流程' })).toHaveAttribute('href', '/knowledge/HR/12');
    expect(view.getByText('看不到那個頁面')).toBeInTheDocument();
    expect(view.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
  });

  it('不要之後收成已略過', async () => {
    // Arrange
    const backend = serveActionProposal(makeActionProposal());
    const { user } = renderCard();
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '不要' }));

    // Assert
    expect(await view.findByText(/^已略過/)).toBeInTheDocument();
    expect(backend.dismissed()).toBe(1);
  });

  it('applying 時定時重讀，變成 applied 後自己收成已套用', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let current = makeActionProposal({ status: 'applying' });
    server.use(http.get('/v1/ai/action-proposals/ac-1', () => ok(current)));
    renderCard();
    const view = within(await card());
    expect(await view.findByText('套用中…')).toBeInTheDocument();

    // Act
    current = { ...current, status: 'applied', decidedAt: '2026-11-27T02:05:00Z' };
    await vi.advanceTimersByTimeAsync(2500);

    // Assert
    expect(await view.findByText(/^已套用/)).toBeInTheDocument();
  });

  it('別的分頁已處理（409）：留下「已經處理過了」', async () => {
    // Arrange
    let current = makeActionProposal();
    server.use(
      http.get('/v1/ai/action-proposals/ac-1', () => ok(current)),
      http.post('/v1/ai/action-proposals/ac-1/apply', () => {
        current = { ...current, status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' };
        return failure(409, 'ai.action_proposal_closed', '已處理');
      }),
    );
    const { user } = renderCard();
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('這份建議已經處理過了。')).toBeInTheDocument();
  });

  it('讀不到時顯示原因', async () => {
    // Arrange
    server.use(http.get('/v1/ai/action-proposals/ac-1', () => failure(404, 'ai.action_proposal_not_found', '找不到')));

    // Act
    renderCard();

    // Assert
    expect(await screen.findByText(/讀不到這份動作建議/)).toBeInTheDocument();
  });
});

describe('動作建議卡片：卡片輸入', () => {
  it('密碼框不記住：type=password、autocomplete=new-password；沒填好「套用」不能按', async () => {
    // Arrange
    const backend = serveActionProposal(passwordProposal);
    const { user } = renderCard(passwordProposal);
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    const field = view.getByLabelText('王小明的初始密碼');
    expect(field).toHaveAttribute('type', 'password');
    expect(field).toHaveAttribute('autocomplete', 'new-password');
    expect(view.getByText('至少 12 個字元')).toBeInTheDocument();
    expect(view.getByRole('button', { name: '套用' })).toHaveAttribute('aria-disabled', 'true');
    expect(backend.applied()).toBe(0);
  });

  it('填好後送出帶 inputs，送出後清空、不寫進快取或瀏覽器儲存空間', async () => {
    // Arrange
    let body: unknown = null;
    serveActionProposal(passwordProposal, (sent) => {
      body = sent;
      return failure(422, 'people.weak_password', '密碼太弱');
    });
    const { user, queryClient } = renderCard(passwordProposal);
    const view = within(await card());
    await user.type(view.getByLabelText('王小明的初始密碼'), 'correct-horse-battery');

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    expect(await view.findByText('密碼太弱')).toBeInTheDocument();
    expect(body).toEqual({ inputs: { 'password:0': 'correct-horse-battery' } });
    await waitFor(() => {
      expect(view.getByLabelText('王小明的初始密碼')).toHaveValue('');
    });
    expect(view.getByRole('button', { name: '套用' })).toHaveAttribute('aria-disabled', 'true');
    const cached = JSON.stringify(queryClient.getQueryCache().getAll().map((query) => query.state.data));
    expect(cached).not.toContain('correct-horse-battery');
    expect(JSON.stringify(queryClient.getMutationCache().getAll().map((mutation) => mutation.state.variables))).not.toContain('correct-horse-battery');
    expect(JSON.stringify(Object.entries(localStorage))).not.toContain('correct-horse-battery');
  });

  it('文字輸入也算必填', async () => {
    // Arrange
    serveActionProposal(makeActionProposal({ inputs: [{ key: 'text:0', label: '備註', kind: 'text', hint: null }] }));
    renderCard();

    // Act
    const view = within(await card());

    // Assert
    expect(view.getByLabelText('備註')).not.toHaveAttribute('type', 'password');
    expect(view.getByRole('button', { name: '套用' })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('動作建議卡片：套用後依種類讓快取失效', () => {
  it.each([
    ['knowledge', ['knowledge', 'space', 'HR', 'tree']],
    ['people', [...queryKeys.people]],
    ['people', [...queryKeys.session]],
    ['people', [...queryKeys.auditEvents]],
    ['people', ['project', 'SMK']],
    ['attachments', ['item', 'SMK-1']],
    ['attachments', ['item', 'SMK-1', 'attachments']],
    ['attachments', ['item', 'SMK-1', 'activity']],
    ['attachments', ['knowledge', 'space', 'HR', 'page', 3, 'attachments']],
    ['saved_views', ['project', 'SMK', 'saved-views']],
    ['saved_views', [...queryKeys.myWorkViews]],
    ['description_templates', [...queryKeys.descriptionTemplates]],
  ] as const)('%s：讓 %j 失效', (kind, key) => {
    // Arrange
    const queryClient = createQueryClient({ queries: { retry: false } });
    queryClient.setQueryData([...key], 'x');
    queryClient.setQueryData(['unrelated'], 'y');

    // Act
    invalidateForActionKind(queryClient, kind);

    // Assert
    expect(queryClient.getQueryState([...key])?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(['unrelated'])?.isInvalidated).toBe(false);
  });

  it('按套用之後真的失效對應的快取', async () => {
    // Arrange
    serveActionProposal(makeActionProposal({ kind: 'description_templates' }));
    const { user, queryClient } = renderCard();
    queryClient.setQueryData([...queryKeys.descriptionTemplates], []);
    const view = within(await card());

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    await view.findByText(/^已套用/);
    expect(queryClient.getQueryState([...queryKeys.descriptionTemplates])?.isInvalidated).toBe(true);
  });
});

describe('動作建議卡片：細節', () => {
  it('只有站內路徑才畫連結', async () => {
    // Arrange
    serveActionProposal(
      makeActionProposal({
        status: 'applied',
        steps: [makeActionStep({ done: true, target: '甲', link: '//example.com/x' }), makeActionStep({ done: true, target: '乙', link: 'https://example.com' })],
      }),
    );

    // Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(await view.findAllByText('已套用', { selector: 'span' })).toHaveLength(2);
    expect(view.queryByRole('link')).not.toBeInTheDocument();
  });

  it('步驟列表在 role=status 外面，狀態區只有摘要', async () => {
    // Arrange
    serveActionProposal(makeActionProposal({ status: 'applied', decidedAt: '2026-11-27T02:05:00Z', steps: [makeActionStep({ done: true, label: '新增頁面乙' })] }));

    // Act
    renderCard();

    // Assert
    const view = within(await card());
    expect(await view.findByText(/^已套用/, { selector: 'p' })).toBeInTheDocument();
    expect(within(view.getByRole('status')).queryByText('新增頁面乙')).not.toBeInTheDocument();
    expect(view.getByText('新增頁面乙')).toBeInTheDocument();
  });

  it('沒填時「套用」用隱藏的說明講原因；密碼管理員的忽略屬性都在', async () => {
    // Arrange
    serveActionProposal(passwordProposal);
    renderCard(passwordProposal);

    // Act
    const view = within(await card());

    // Assert
    const button = view.getByRole('button', { name: '套用' });
    expect(button).toHaveAccessibleDescription('還有欄位沒填');
    const field = view.getByLabelText('王小明的初始密碼');
    expect(field).toHaveAttribute('data-1p-ignore');
    expect(field).toHaveAttribute('data-lpignore', 'true');
    expect(field).toHaveAttribute('data-bwignore');
  });

  it('422 的 details 路徑是 inputs.<key> 時錯誤顯示在那個欄位上', async () => {
    // Arrange
    serveActionProposal(passwordProposal, () =>
      new Response(
        JSON.stringify({ success: false, error: { code: 'validation', message: '輸入有誤', details: [{ code: 'weak', path: 'inputs.password:0', message: '密碼太短' }] } }),
        { status: 422, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    const { user } = renderCard(passwordProposal);
    const view = within(await card());
    await user.type(view.getByLabelText('王小明的初始密碼'), 'abc');

    // Act
    await user.click(view.getByRole('button', { name: '套用' }));

    // Assert
    await waitFor(() => {
      expect(view.getByLabelText('王小明的初始密碼')).toHaveAccessibleDescription(/密碼太短/);
    });
  });
});
