import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { ChangeSetDetail, ChangeSetRequest, ProjectDetail } from '../../api/types';
import { ownerRef } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { captureProposals, makeChangeSet, makeChangeSetDetail, serveChangeSet, serveChangeSets, serveDecision } from '../../test/changesets';
import { exampleOwner, smkDetail, smkProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';
import { hasProposedDraft } from '../settings/settingsDraftMemory';

type View = ReturnType<typeof renderApp>;
type User = View['user'];

const SUMMARY = '拿掉待辦與標籤';

function sectionTabs() {
  return within(screen.getByRole('navigation', { name: '設定的區塊' }));
}

/** 像後端一樣：每送出一次就是新的一份等待核准的變更集（#7、#8…），基準版本是送來的那一版。 */
function numberedProposals(project: ProjectDetail) {
  let next = 7;
  return (body: ChangeSetRequest) => {
    const changeSet = makeChangeSet({ number: next, baseVersion: body.baseVersion, summary: body.summary ?? null });
    next += 1;
    return ok(makeChangeSetDetail(body.blueprint, { baseBlueprint: project.blueprint, changeSet }), 201);
  };
}

function decided(number: number, status: 'rejected' | 'applied'): ChangeSetDetail {
  const changeSet = makeChangeSet({
    number,
    status,
    appliedVersion: status === 'applied' ? 2 : null,
    decidedBy: ownerRef,
    decidedAt: '2026-11-15T03:00:00Z',
  });
  return makeChangeSetDetail(simpleTasksBlueprint, { changeSet, canApply: false });
}

async function openSettings(detail: ProjectDetail = smkDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets('SMK', []);
  const proposals = captureProposals(detail, numberedProposals(detail));
  const view = renderApp('/projects/SMK/settings/workflows');
  await screen.findByRole('heading', { level: 1, name: '設定' });
  return { ...view, proposals };
}

/** 流程刪掉「待辦」（項目搬到「進行中」），換到欄位刪掉「標籤」，填摘要：兩個區塊各一項修改。 */
async function editTwoSections(user: User) {
  await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));
  await chooseOption(user, screen.getByRole('combobox', { name: '停在「待辦」的項目搬到' }), '進行中');
  await user.click(sectionTabs().getByRole('link', { name: '欄位' }));
  await user.click(await screen.findByRole('button', { name: '刪除「標籤」' }));
  await user.type(screen.getByLabelText('摘要'), SUMMARY);
}

async function preview(user: User, number: number) {
  await user.click(screen.getByRole('button', { name: '預覽變更' }));
  return screen.findByRole('heading', { level: 1, name: `變更集 #${String(number)}` });
}

async function expectDraftBack(router: View['router'], section: string) {
  await waitFor(() => {
    expect(currentPath(router)).toBe(`/projects/SMK/settings/${section}`);
  });
  expect(await screen.findByText('還沒送出的修改：2 項')).toBeInTheDocument();
  expect(screen.getByLabelText('摘要')).toHaveValue(SUMMARY);
  expect(sectionTabs().getByRole('link', { name: '流程' })).toHaveAccessibleDescription('1 項還沒送出的修改');
  expect(sectionTabs().getByRole('link', { name: '欄位' })).toHaveAccessibleDescription('1 項還沒送出的修改');
}

describe('預覽之後回去修改', () => {
  it('放棄這份變更集、回到送出時的區塊：草稿、摘要、搬移目標與計數都在，焦點到標題並說明', async () => {
    // Arrange
    const { user, router } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    const decision = serveDecision('SMK', 7, 'reject', () => ok(decided(7, 'rejected')));

    // Act
    await user.click(await screen.findByRole('button', { name: '回去修改' }));

    // Assert
    await expectDraftBack(router, 'fields');
    expect(decision.count()).toBe(1);
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('已放棄變更集 #7，修改還在，可以繼續改。');
    });
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
    expect(screen.queryByText('已放棄變更集 #7，設定沒有改變。')).not.toBeInTheDocument();
    await user.click(sectionTabs().getByRole('link', { name: '流程' }));
    expect(await screen.findByRole('combobox', { name: '停在「待辦」的項目搬到' })).toHaveTextContent('進行中');
  });

  it('回去修改之後再預覽：是新的一份變更集，「回去修改」跟著換到新的那一份', async () => {
    // Arrange
    const { user, router, proposals } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    serveDecision('SMK', 7, 'reject', () => ok(decided(7, 'rejected')));
    await user.click(await screen.findByRole('button', { name: '回去修改' }));
    await expectDraftBack(router, 'fields');

    // Act
    await preview(user, 8);

    // Assert
    expect(proposals.map((proposal) => proposal.body.summary)).toEqual([SUMMARY, SUMMARY]);
    const decision = serveDecision('SMK', 8, 'reject', () => ok(decided(8, 'rejected')));
    await user.click(await screen.findByRole('button', { name: '回去修改' }));
    await expectDraftBack(router, 'fields');
    expect(decision.count()).toBe(1);
  });

  it('放棄失敗：留在預覽頁說明原因，草稿不動，再按一次成功就回去', async () => {
    // Arrange
    const { user, router } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    let attempts = 0;
    serveDecision('SMK', 7, 'reject', () => {
      attempts += 1;
      return attempts === 1 ? failure(503, 'server.not_ready', 'starting') : ok(decided(7, 'rejected'));
    });
    const revise = await screen.findByRole('button', { name: '回去修改' });

    // Act
    await user.click(revise);

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('放棄這份變更集失敗了，還不能回去修改；你的修改都還在。伺服器暫時無法處理，請稍後再試。');
    expect(alert).toHaveFocus();
    expect(currentPath(router)).toBe('/projects/SMK/changesets/7');
    await user.click(await screen.findByRole('button', { name: '回去修改' }));
    await expectDraftBack(router, 'fields');
    expect(attempts).toBe(2);
  });

  it('回到設定頁時別人已經套用了新版本：草稿留在原本的版本上，說明過時，可以以最新版本重新開始', async () => {
    // Arrange
    const [flow] = simpleTasksBlueprint.workflows;
    if (flow === undefined) throw new Error('沒有流程。');
    const renamed: ProjectBlueprint = {
      ...simpleTasksBlueprint,
      workflows: [{ ...flow, states: flow.states.map((state) => (state.key === 'doing' ? { ...state, name: '處理中' } : state)) }],
    };
    const newer = smkDetail({ project: { ...smkProject, blueprintVersion: 2 }, blueprint: renamed });
    const { user, queryClient } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    serveDecision('SMK', 7, 'reject', () => ok(decided(7, 'rejected')));
    server.use(http.get('/v1/projects/SMK', () => ok(newer)));
    queryClient.setQueryData(['project', 'SMK'], newer);

    // Act
    await user.click(await screen.findByRole('button', { name: '回去修改' }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('別人已經套用了新版本：設定在你編輯時已經更新為第 2 版，你的修改是以第 1 版為準');
    expect(screen.getByText(/藍圖第 1 版/)).toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：2 項')).toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: '以最新版本重新開始' }));
    expect(await screen.findByText(/藍圖第 2 版/)).toBeInTheDocument();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('回去修改之後離開設定頁（捨棄修改並離開），再回來是目前的設定', async () => {
    // Arrange
    const { user, router } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    serveDecision('SMK', 7, 'reject', () => ok(decided(7, 'rejected')));
    await user.click(await screen.findByRole('button', { name: '回去修改' }));
    await expectDraftBack(router, 'fields');

    // Act
    await user.click(within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' }));
    await user.click(within(await screen.findByRole('dialog', { name: '還有沒送出的修改' })).getByRole('button', { name: '捨棄修改並離開' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
    await router.navigate('/projects/SMK/settings/workflows');

    // Assert
    expect(await screen.findByText('還沒有修改')).toBeInTheDocument();
  });
});

describe('「回去修改」只給自己剛從設定頁送出、等待核准的那一份', () => {
  it('直接打開的變更集沒有「回去修改」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSet('SMK', makeChangeSetDetail(simpleTasksBlueprint, { changeSet: makeChangeSet({ number: 3 }) }));

    // Act
    renderApp('/projects/SMK/changesets/3');

    // Assert
    expect(await screen.findByRole('button', { name: '放棄' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '回去修改' })).not.toBeInTheDocument();
  });

  it('剛送出的那一份用原本的「放棄」：照舊說設定沒有改變，記憶體裡的草稿清掉', async () => {
    // Arrange
    const { user, queryClient } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    serveDecision('SMK', 7, 'reject', () => ok(decided(7, 'rejected')));
    expect(hasProposedDraft(queryClient, 'SMK', 1, 7, exampleOwner.personId)).toBe(true);

    // Act
    await user.click(screen.getByRole('button', { name: '放棄' }));
    await user.click(within(screen.getByRole('dialog', { name: '放棄變更集 #7？' })).getByRole('button', { name: '放棄' }));

    // Assert
    expect(await screen.findByText('已放棄變更集 #7，設定沒有改變。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '回去修改' })).not.toBeInTheDocument();
    expect(hasProposedDraft(queryClient, 'SMK', 1, 7, exampleOwner.personId)).toBe(false);
  });

  it('剛送出的那一份套用之後，記憶體裡的草稿清掉', async () => {
    // Arrange
    const { user, queryClient, router } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    serveChangeSet('SMK', decided(7, 'applied'));
    serveDecision('SMK', 7, 'apply', () => ok(decided(7, 'applied')));

    // Act
    await user.click(screen.getByRole('button', { name: '套用' }));
    await user.click(within(screen.getByRole('dialog', { name: '套用變更集 #7？' })).getByRole('button', { name: '套用' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    });
    expect(hasProposedDraft(queryClient, 'SMK', 1, 7, exampleOwner.personId)).toBe(false);
  });

  it('沒按「回去修改」直接回到設定頁是目前的設定；在那裡「捨棄修改」之後，剛送出的那一份不再有「回去修改」', async () => {
    // Arrange
    const { user, router } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    expect(await screen.findByRole('button', { name: '回去修改' })).toBeInTheDocument();
    await router.navigate('/projects/SMK/settings/workflows');
    expect(await screen.findByText('還沒有修改')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));

    // Act
    await user.click(screen.getByRole('button', { name: '捨棄修改' }));
    await user.click(within(screen.getByRole('dialog', { name: '捨棄所有修改？' })).getByRole('button', { name: '捨棄' }));
    await router.navigate('/projects/SMK/changesets/7');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '變更集 #7' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '放棄' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '回去修改' })).not.toBeInTheDocument();
  });
});

describe('回去修改：送出中、上一頁與版本過時', () => {
  it('送出中：按鈕寫「放棄中…」並標 aria-disabled，焦點留在它身上，再按也不重送', async () => {
    // Arrange
    const { user, router } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    let requests = 0;
    server.use(
      http.post('/v1/projects/SMK/changesets/7/reject', async () => {
        requests += 1;
        await held;
        return ok(decided(7, 'rejected'));
      }),
    );

    // Act
    await user.click(await screen.findByRole('button', { name: '回去修改' }));

    // Assert
    const pending = await screen.findByRole('button', { name: '放棄中…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toBeEnabled();
    expect(pending).toHaveFocus();
    await user.click(pending);
    expect(requests).toBe(1);
    release();
    await expectDraftBack(router, 'fields');
    expect(requests).toBe(1);
  });

  it('沒按「回去修改」、用上一頁回到設定頁：提示說剛送出的變更集還在等待核准，連到它的預覽頁', async () => {
    // Arrange
    const { user, router } = await openSettings();
    await editTwoSections(user);
    // 送出成功時清單會重讀：那時伺服器上已經有 #7。
    serveChangeSets('SMK', [makeChangeSet({ number: 7, baseVersion: 1, summary: SUMMARY })]);
    await preview(user, 7);

    // Act
    await router.navigate(-1);

    // Assert
    const link = await screen.findByRole('link', { name: '變更集 #7' });
    expect(link).toHaveAttribute('href', '/projects/SMK/changesets/7');
    expect(link.closest('p')).toHaveTextContent('你剛送出的變更集 #7 還在等待核准，可以到預覽頁按「回去修改」帶著修改回來。');
    expect(screen.getByRole('link', { name: '有 1 份變更等待核准' })).toBeInTheDocument();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('等待核准的變更集不是這個分頁剛送出的（記憶體裡沒有）：只說有幾份等待核准', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [makeChangeSet({ number: 3 })]);

    // Act
    renderApp('/projects/SMK/settings/workflows');

    // Assert
    expect(await screen.findByRole('link', { name: '有 1 份變更等待核准' })).toBeInTheDocument();
    expect(screen.queryByText(/你剛送出的變更集/)).not.toBeInTheDocument();
  });

  it('別人已經套用了新版本：換頁提示不說「可以繼續改」，說要先以最新版本重新開始', async () => {
    // Arrange
    const newer = smkDetail({ project: { ...smkProject, blueprintVersion: 2 } });
    const { user } = await openSettings();
    await editTwoSections(user);
    await preview(user, 7);
    serveDecision('SMK', 7, 'reject', () => ok(decided(7, 'rejected')));
    server.use(http.get('/v1/projects/SMK', () => ok(newer)));

    // Act
    await user.click(await screen.findByRole('button', { name: '回去修改' }));

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        '已放棄變更集 #7。修改還在，但別人已經套用了新版本，要先以最新版本重新開始才送得出去。',
      );
    });
    expect(screen.getByRole('alert')).toHaveTextContent('別人已經套用了新版本：設定在你編輯時已經更新為第 2 版');
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toHaveFocus();
  });
});
