import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { serveGroupedItems } from '../../test/boardGroup';
import { exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const owner = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const nameOf = (personId: string) => (personId === exampleMember.personId ? exampleMember.displayName : exampleOwner.displayName);
const bugOne = smkItem({ number: 1, title: '修錯字', fields: { category: 'bug' }, assignee: owner });

/** 讓修改卡在送出中，直到測試放行（放行時回錯誤或照常）。 */
function holdPatch(shouldFail: boolean) {
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  server.use(
    http.patch('/v1/items/:key', async () => {
      await gate;
      if (shouldFail) return failure(409, 'item.assignee_not_member', '這個人不是專案成員。');
      return undefined;
    }),
  );
  return release;
}

async function startMoveToMember(shouldFail: boolean) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveGroupedItems('SMK', [bugOne], nameOf);
  const release = holdPatch(shouldFail);
  const view = renderApp('/projects/SMK?view=board&group=assignee');
  const board = await screen.findByRole('region', { name: '看板' });
  const card = await within(board).findByRole('article', { name: bugOne.title });
  await view.user.click(within(card).getByRole('button', { name: 'SMK-1 移到…' }));
  const menu = within(card).getByRole('menu', { name: '把 SMK-1 移到' });
  return { ...view, board, menu, release };
}

describe('換分組時送出中的移動', () => {
  it('依負責人拖到一半切到依分類：送出中的移動不套到分類的欄', async () => {
    // Arrange
    const { board, menu, user, release } = await startMoveToMember(false);
    await user.click(within(menu).getByRole('menuitem', { name: exampleMember.displayName }));
    await screen.findByText(`正在把 SMK-1 移到「${exampleMember.displayName}」…`);

    // Act
    await chooseOption(user, await screen.findByRole('combobox', { name: '分組' }), '依「分類」分組');

    // Assert：在分類的看板上，這張卡片仍在「錯誤」那一欄，也沒有拿人員編號當選項的說明
    const bugLane = await within(board).findByRole('region', { name: '錯誤' });
    expect(within(bugLane).getByRole('article', { name: bugOne.title })).toBeInTheDocument();
    expect(screen.queryByText(/正在把 SMK-1/)).not.toBeInTheDocument();
    release();
  });

  it('失敗的說明只在原本的分組上出現', async () => {
    // Arrange
    const { menu, user, release } = await startMoveToMember(true);
    await user.click(within(menu).getByRole('menuitem', { name: exampleMember.displayName }));
    await chooseOption(user, await screen.findByRole('combobox', { name: '分組' }), '依「分類」分組');

    // Act
    release();

    // Assert
    await waitFor(() => {
      expect(screen.queryByText(/正在把 SMK-1/)).not.toBeInTheDocument();
    });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await chooseOption(user, await screen.findByRole('combobox', { name: '分組' }), '依負責人分組');
    expect(await screen.findByRole('alert')).toHaveTextContent('這個人不是專案成員。');
  });
});
