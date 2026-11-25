import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { ChangeSetDetail, ProjectDetail } from '../../api/types';
import { memberRef } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { makeChangeSet, makeChangeSetDetail, serveChangeSet } from '../../test/changesets';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { formatInstant } from '../../domain/time';

/** 把「進行中」拆成「開發」與「內部驗收」、刪掉「備註」、把「分類」改名。 */
const proposed: ProjectBlueprint = {
  ...simpleTasksBlueprint,
  fields: simpleTasksBlueprint.fields
    .filter((field) => field.key !== 'notes')
    .map((field) => (field.key === 'category' ? { ...field, name: '類別' } : field)),
  workflows: [
    {
      key: 'task_flow',
      name: '任務流程',
      states: [
        { key: 'todo', name: '待辦', category: 'not_started' },
        { key: 'building', name: '開發', category: 'in_progress' },
        { key: 'internal_qa', name: '內部驗收', category: 'in_progress' },
        { key: 'done', name: '完成', category: 'done' },
      ],
    },
  ],
};

function splitDetail(overrides: Partial<ChangeSetDetail> = {}): ChangeSetDetail {
  return makeChangeSetDetail(proposed, {
    baseBlueprint: simpleTasksBlueprint,
    changeSet: makeChangeSet({ number: 3, summary: '把進行中拆成開發與內部驗收', proposedBy: memberRef }),
    changes: [
      { kind: 'modified', resource: 'field', key: 'category', parentKey: null, changedProperties: ['name'] },
      { kind: 'removed', resource: 'field', key: 'notes', parentKey: null, changedProperties: [] },
      { kind: 'added', resource: 'state', key: 'building', parentKey: 'task_flow', changedProperties: [] },
      { kind: 'added', resource: 'state', key: 'internal_qa', parentKey: 'task_flow', changedProperties: [] },
      { kind: 'removed', resource: 'state', key: 'doing', parentKey: 'task_flow', changedProperties: [] },
    ],
    impacts: [
      { kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'building', affected: 12, blocksApply: false },
      { kind: 'field_drop', field: 'notes', affected: 8, blocksApply: false },
    ],
    ...overrides,
  });
}

function openChangeSet(detail: ChangeSetDetail, project: ProjectDetail = smkDetail()) {
  signedInAs(exampleOwner, { details: [project] });
  serveChangeSet('SMK', detail);
  return renderApp(`/projects/SMK/changesets/${String(detail.changeSet.number)}`);
}

describe('變更集的預覽頁：會動到的資料合計', () => {
  it('直接提出的：影響清單上方一行合計，分類列出筆數', async () => {
    // Arrange / Act
    openChangeSet(splitDetail());

    // Assert
    const total = await screen.findByText('最多動到 20 筆資料（搬移狀態 12、刪除或封存欄位的值 8）');
    expect(total.closest('[role]')).toBeNull();
    expect(screen.queryByText(/這份是 AI 提出的/)).not.toBeInTheDocument();
  });

  it.each(['assistant', 'agent'] as const)('%s 提出的：同一行改用警告的提示並提醒逐項確認', async (origin) => {
    // Arrange
    const detail = splitDetail({
      impacts: [
        { kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'building', affected: 3, blocksApply: false },
        { kind: 'field_detach', field: 'category', itemType: 'task', affected: 2, blocksApply: false },
        { kind: 'item_type_removal', itemType: 'task', affected: 1, blocksApply: true },
      ],
    });

    // Act
    openChangeSet({ ...detail, changeSet: { ...detail.changeSet, origin } });

    // Assert
    // 擋住套用的影響（刪掉還有項目的類型）不會真的動到資料，不算進合計。
    expect(await screen.findByText('最多動到 5 筆資料（搬移狀態 3、刪除或封存欄位的值 2）。這份是 AI 提出的，套用前請逐項確認。')).toBeInTheDocument();
  });

  it('已經不是等待核准（伺服器不再算數量）：不顯示合計', async () => {
    // Arrange / Act
    const applied = splitDetail();
    openChangeSet({ ...applied, changeSet: { ...applied.changeSet, status: 'applied' } });

    // Assert
    await screen.findByRole('list', { name: '對既有資料的影響' });
    expect(screen.queryByText(/最多動到 \d+ 筆資料/)).not.toBeInTheDocument();
  });

  it('沒有會動到資料的影響：不顯示合計', async () => {
    // Arrange / Act
    const none = splitDetail({ impacts: [{ kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'building', affected: 0, blocksApply: false }] });
    openChangeSet(none);

    // Assert
    await screen.findByRole('list', { name: '對既有資料的影響' });
    expect(screen.queryByText(/最多動到 \d+ 筆資料/)).not.toBeInTheDocument();
  });
});

describe('變更集的預覽頁', () => {
  it('AI 助理提出的變更集在提出的人旁邊有「AI 助理提出」，直接提出的沒有', async () => {
    // Arrange & Act
    const detail = splitDetail();
    openChangeSet({ ...detail, changeSet: { ...detail.changeSet, origin: 'assistant' } });

    // Assert
    expect(await screen.findByText('AI 助理提出')).toBeInTheDocument();
  });

  it('外部的 AI 代理（MCP）提出的變更集標「AI 代理提出」', async () => {
    // Arrange & Act
    const detail = splitDetail();
    openChangeSet({ ...detail, changeSet: { ...detail.changeSet, origin: 'agent' } });

    // Assert
    expect(await screen.findByText('AI 代理提出')).toBeInTheDocument();
    expect(screen.queryByText('AI 助理提出')).not.toBeInTheDocument();
  });

  it('直接提出的變更集沒有「AI 助理提出」', async () => {
    // Arrange & Act
    openChangeSet(splitDetail());

    // Assert
    await screen.findByRole('heading', { level: 1, name: '變更集 #3' });
    expect(screen.queryByText('AI 助理提出')).not.toBeInTheDocument();
  });

  it('標題、狀態、提出的人與時間、摘要', async () => {
    // Arrange & Act
    openChangeSet(splitDetail());

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '變更集 #3' })).toBeInTheDocument();
    expect(screen.getByText('把進行中拆成開發與內部驗收')).toBeInTheDocument();
    expect(screen.getByText('等待核准')).toBeInTheDocument();
    expect(screen.getByText(`範例成員 在 ${formatInstant('2026-11-15T02:00:00Z')} 提出`)).toBeInTheDocument();
    const crumbs = screen.getByRole('navigation', { name: '位置' });
    expect(within(crumbs).getByRole('link', { name: '變更集' })).toHaveAttribute('href', '/projects/SMK/changesets');
  });

  it('一眼看出會動到多少資料、有沒有擋住', async () => {
    // Arrange & Act
    openChangeSet(splitDetail());

    // Assert
    const overview = await screen.findByRole('list', { name: '摘要' });
    expect(within(overview).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '設定的修改5 項',
      '會動到的項目最多 20 個',
      '能不能套用可以套用',
    ]);
  });

  it('差異說成句子，名稱從新舊兩份藍圖找', async () => {
    // Arrange & Act
    openChangeSet(splitDetail());

    // Assert
    const changes = await screen.findByRole('list', { name: '設定的差異' });
    expect(within(changes).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '修改把欄位「分類」改名為「類別」',
      '刪除刪除欄位「備註」',
      '新增新增狀態「開發」',
      '新增新增狀態「內部驗收」',
      '刪除刪除狀態「進行中」',
    ]);
  });

  it('對既有資料的影響：每一項一句話加數量，擋住套用的標出來；擋住的原因另外列出', async () => {
    // Arrange
    const blocked = splitDetail({
      impacts: [
        { kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'building', affected: 12, blocksApply: false },
        { kind: 'item_type_removal', itemType: 'task', affected: 4, blocksApply: true },
      ],
      blockers: [{ code: 'changeset.items_of_removed_type', path: null, message: '類型「task」底下還有 4 個項目。' }],
      canApply: false,
    });

    // Act
    openChangeSet(blocked);

    // Assert
    const impacts = await screen.findByRole('list', { name: '對既有資料的影響' });
    const [move, removal] = within(impacts).getAllByRole('listitem');
    expect(move).toHaveTextContent('12 個項目會從「進行中」搬到「開發」');
    expect(move).not.toHaveTextContent('擋住套用');
    expect(removal).toHaveTextContent('還有 4 個「任務」：要先在項目頁刪除這些項目或改成別的類型，才能刪除這個類型擋住套用');
    const blockers = screen.getByRole('list', { name: '擋住套用的原因' });
    expect(blockers).toHaveTextContent('類型「task」底下還有 4 個項目。');
    expect(within(screen.getByRole('list', { name: '摘要' })).getByText('有 1 個原因擋住套用')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
  });

  it('流程從不限制切成限制：轉換太多時句子說幾條，底下一行一條列出能走的轉換與限制', async () => {
    // Arrange
    const [flow] = simpleTasksBlueprint.workflows;
    const states = flow?.states ?? [];
    const transitions = states.flatMap((from) =>
      states.filter((to) => to.key !== from.key).map((to) => ({ from: from.key, to: to.key, allowedRoles: to.key === 'done' ? ['editor'] : [], requireFields: [] })),
    );
    const restricted: ProjectBlueprint = { ...simpleTasksBlueprint, workflows: [{ ...(flow ?? { key: 'task_flow', name: '任務流程', states }), transitions }] };
    const detail = makeChangeSetDetail(restricted, {
      baseBlueprint: simpleTasksBlueprint,
      changeSet: makeChangeSet({ number: 3 }),
      changes: [{ kind: 'modified', resource: 'workflow', key: 'task_flow', parentKey: null, changedProperties: ['transitions'] }],
    });

    // Act
    openChangeSet(detail);

    // Assert
    const changes = await screen.findByRole('list', { name: '設定的差異' });
    expect(changes).toHaveTextContent('修改流程「任務流程」：開始限制轉換，只能走下面這 6 條');
    expect(within(within(changes).getByRole('list', { name: '可以走的轉換' })).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '「待辦」→「進行中」',
      '「待辦」→「完成」（限「編輯者」）',
      '「進行中」→「待辦」',
      '「進行中」→「完成」（限「編輯者」）',
      '「完成」→「待辦」',
      '「完成」→「進行中」',
    ]);
  });

  it('等待核准時，沒有項目要搬的狀態合成一行；確認套用的對話框也是', async () => {
    // Arrange
    const detail = splitDetail({
      impacts: [
        { kind: 'state_move', workflow: 'task_flow', from: 'todo', to: 'building', affected: 0, blocksApply: false },
        { kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'building', affected: 12, blocksApply: false },
        { kind: 'state_move', workflow: 'task_flow', from: 'done', to: 'building', affected: 0, blocksApply: false },
        { kind: 'field_drop', field: 'notes', affected: 8, blocksApply: false },
      ],
    });
    const { user } = openChangeSet(detail);

    // Act
    const impacts = await screen.findByRole('list', { name: '對既有資料的影響' });
    await user.click(screen.getByRole('button', { name: '套用' }));

    // Assert
    const expected = ['12 個項目會從「進行中」搬到「開發」', '其他 2 個狀態沒有項目要搬：「待辦」、「完成」', '8 個項目的「備註」會被封存'];
    expect(within(impacts).getAllByRole('listitem').map((item) => item.textContent)).toEqual(expected);
    const dialog = screen.getByRole('dialog', { name: '套用變更集 #3？' });
    expect(within(within(dialog).getByRole('list', { name: '會動到的資料' })).getAllByRole('listitem').map((item) => item.textContent)).toEqual(expected);
  });

  it('沒有差異、不動到資料時直接說', async () => {
    // Arrange & Act
    openChangeSet(makeChangeSetDetail(simpleTasksBlueprint, { changeSet: makeChangeSet({ number: 3 }) }));

    // Assert
    expect(await screen.findByText('和目前的設定一樣，沒有差異。')).toBeInTheDocument();
    expect(screen.getByText('不會動到既有的項目。')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: '摘要' })).getByText('沒有')).toBeInTheDocument();
  });

  it('已套用的變更集：說明是第幾版、誰在何時套用，影響不再算數量', async () => {
    // Arrange
    const applied = splitDetail({
      changeSet: makeChangeSet({
        number: 3,
        status: 'applied',
        appliedVersion: 2,
        decidedBy: memberRef,
        decidedAt: '2026-11-15T03:00:00Z',
      }),
      impacts: [{ kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'building', affected: 0, blocksApply: false }],
      canApply: false,
    });

    // Act
    openChangeSet(applied, smkDetail({ blueprint: proposed, project: { ...smkDetail().project, blueprintVersion: 2 } }));

    // Assert
    expect(await screen.findByText(`範例成員 在 ${formatInstant('2026-11-15T03:00:00Z')} 套用，藍圖是第 2 版`)).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: '對既有資料的影響' })).getByRole('listitem')).toHaveTextContent(
      '停在「進行中」的項目搬到「開發」',
    );
    expect(screen.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '放棄' })).not.toBeInTheDocument();
  });

  it('已套用、過期的變更集：「改之前」是基準版本的藍圖，不是專案目前的（已經是改之後的樣子）', async () => {
    // Arrange
    const recategorized: ProjectBlueprint = {
      ...proposed,
      workflows: proposed.workflows.map((workflow) => ({
        ...workflow,
        states: workflow.states.map((state) => (state.key === 'todo' ? { ...state, name: '待處理', category: 'backlog' } : state)),
      })),
    };
    const applied = makeChangeSetDetail(recategorized, {
      baseBlueprint: simpleTasksBlueprint,
      changeSet: makeChangeSet({ number: 3, status: 'applied', appliedVersion: 2, decidedBy: memberRef, decidedAt: '2026-11-15T03:00:00Z' }),
      changes: [
        { kind: 'modified', resource: 'field', key: 'category', parentKey: null, changedProperties: ['name'] },
        { kind: 'removed', resource: 'field', key: 'notes', parentKey: null, changedProperties: [] },
        { kind: 'modified', resource: 'state', key: 'todo', parentKey: 'task_flow', changedProperties: ['name', 'category'] },
        { kind: 'removed', resource: 'state', key: 'doing', parentKey: 'task_flow', changedProperties: [] },
      ],
      canApply: false,
    });

    // Act
    openChangeSet(applied, smkDetail({ blueprint: recategorized, project: { ...smkDetail().project, blueprintVersion: 2 } }));

    // Assert
    const changes = await screen.findByRole('list', { name: '設定的差異' });
    expect(within(changes).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '修改把欄位「分類」改名為「類別」',
      '刪除刪除欄位「備註」',
      '修改修改狀態「待辦」：改名為「待處理」、類別從「未開始」改成「待規劃」',
      '刪除刪除狀態「進行中」',
    ]);
    expect(screen.queryByText(/只能顯示 key/)).not.toBeInTheDocument();
  });

  it('欄位從類型拿掉：說出哪個類型的幾個項目的值會被封存，算進會動到的項目', async () => {
    // Arrange
    const detached = splitDetail({
      changes: [{ kind: 'modified', resource: 'item_type', key: 'task', parentKey: null, changedProperties: ['fields'] }],
      impacts: [{ kind: 'field_detach', itemType: 'task', field: 'summary', affected: 5, blocksApply: false }],
    });

    // Act
    openChangeSet(detached);

    // Assert
    const impacts = await screen.findByRole('list', { name: '對既有資料的影響' });
    expect(within(impacts).getByRole('listitem')).toHaveTextContent('5 個「任務」的「摘要」會被封存');
    expect(within(screen.getByRole('list', { name: '摘要' })).getByText('5 個')).toBeInTheDocument();
  });

  it('過期的變更集說明別的變更先套用了，連到設定頁重新編輯', async () => {
    // Arrange
    const expired = splitDetail({
      changeSet: makeChangeSet({ number: 3, status: 'expired', decidedAt: '2026-11-15T03:00:00Z' }),
      canApply: false,
    });

    // Act
    openChangeSet(expired);

    // Assert
    expect(await screen.findByText(/別的變更已經先套用，這份已經過期/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '到設定頁重新編輯' })).toHaveAttribute('href', '/projects/SMK/settings');
  });

  it('找不到的變更集與寫錯的編號說找不到', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/projects/SMK/changesets/9', () => failure(404, 'changeset.not_found', '找不到這份變更集。')));
    const { router } = renderApp('/projects/SMK/changesets/9');

    // Act & Assert
    expect(await screen.findByRole('heading', { level: 1, name: '找不到這份變更集' })).toBeInTheDocument();
    expect(screen.getByText('這個專案沒有編號是 #9 的變更集。')).toBeInTheDocument();
    await router.navigate('/projects/SMK/changesets/abc');
    expect(await screen.findByText('這個專案沒有編號是 #abc 的變更集。')).toBeInTheDocument();
  });

  it('沒有 blueprint.read 時說沒有權限，也不讀變更集', async () => {
    // Arrange（沒有宣告變更集的端點：讀了測試就會失敗）
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });

    // Act
    renderApp('/projects/SMK/changesets/3');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '沒有權限看這個專案的變更集' })).toBeInTheDocument();
  });
});
