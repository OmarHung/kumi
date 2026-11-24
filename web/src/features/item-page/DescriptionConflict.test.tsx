import { delay, http } from 'msw';
import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ok } from '../../test/api';
import { server } from '../../test/server';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { descriptionRevisionOf, detailOf, serveItem, servePatch } from '../../test/items';
import { createFakeLiveConnection, type FakeLiveConnection } from '../../test/liveConnection';
import { renderApp } from '../../test/renderApp';
import { findEditor, typeInEditor } from '../../test/richText';

const item = smkItem({ number: 7, title: '首頁設計稿' });
const mine = detailOf(item, '原本的說明');
const theirs = detailOf(item, '別人改過的說明');
const CHANGED = '這段說明在你編輯時被別人改過了。';

function editorText(editor: HTMLElement): string {
  return editor.textContent;
}

async function openAndStartEditing() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(mine);
  serveActivity(item.key, []);
  const live = createFakeLiveConnection();
  const view = renderApp(`/projects/SMK/items/${item.key}`, { liveConnection: () => live });
  const section = await screen.findByRole('region', { name: '說明' });
  await view.user.click(within(section).getByRole('button', { name: '編輯說明' }));
  const editor = await findEditor('說明', section);
  return { ...view, live, section, editor };
}

/** 別人在伺服器上改了說明；即時更新通知畫面重抓。 */
function someoneElseEdits(live: FakeLiveConnection) {
  serveItem(theirs);
  live.emitItems({ projectKey: 'SMK', itemKeys: [item.key] });
}

async function userSave(section: HTMLElement) {
  const { default: userEvent } = await import('@testing-library/user-event');
  await userEvent.setup().click(within(section).getByRole('button', { name: '儲存' }));
}

describe('說明的編輯衝突', () => {
  it('編輯中別人改了說明（即時更新）：提示、不打斷編輯；「看新的內容」換成新的並留在編輯中', async () => {
    // Arrange
    const { user, live, section, editor } = await openAndStartEditing();
    await typeInEditor(user, editor, '，我補的');
    const patches = servePatch(theirs);

    // Act
    someoneElseEdits(live);

    // Assert
    const notice = await within(section).findByText(CHANGED);
    expect(notice).toHaveAttribute('role', 'status');
    expect(editorText(editor)).toContain('我補的');
    expect(await within(section).findByRole('button', { name: '繼續編輯' })).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '用我的版本覆蓋' })).not.toBeInTheDocument();

    // Act
    await user.click(within(section).getByRole('button', { name: '看新的內容' }));

    // Assert
    await waitFor(() => {
      expect(editorText(editor)).toBe('別人改過的說明');
    });
    expect(editor).toHaveFocus();
    expect(within(section).queryByText(CHANGED)).not.toBeInTheDocument();
    expect(sessionStorage.getItem('draft.description.SMK-7')).toBeNull();

    // Act：換成新的之後再改再存，帶的是新的版本
    await typeInEditor(user, editor, '，接著寫');
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(patches).toHaveLength(1);
    });
    expect(patches[0]?.body).toEqual({ description: '別人改過的說明，接著寫', expectedDescriptionRevision: descriptionRevisionOf('別人改過的說明') });
  });

  it('「繼續編輯」關掉提示；儲存時被擋下才有「用我的版本覆蓋」', async () => {
    // Arrange
    const { user, live, section, editor } = await openAndStartEditing();
    await typeInEditor(user, editor, '，我補的');
    const patches = servePatch(theirs);
    someoneElseEdits(live);
    await within(section).findByText(CHANGED);

    // Act
    await user.click(await within(section).findByRole('button', { name: '繼續編輯' }));

    // Assert
    expect(within(section).queryByText(CHANGED)).not.toBeInTheDocument();
    expect(editor).toHaveFocus();
    expect(editorText(editor)).toContain('我補的');

    // Act：儲存時伺服器擋下（開始編輯時的版本已經過時）
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert：自己的內容還在編輯器裡，提示換成衝突的動作，編輯器沒有另外的錯誤訊息
    expect(await within(section).findByText(CHANGED)).toBeInTheDocument();
    expect(await within(section).findByRole('button', { name: '用我的版本覆蓋' })).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '繼續編輯' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('alert')).not.toBeInTheDocument();
    expect(editorText(editor)).toContain('我補的');
    expect(patches).toHaveLength(1);
    expect(patches[0]?.body).toMatchObject({ expectedDescriptionRevision: descriptionRevisionOf('原本的說明') });
  });

  it('儲存被擋（409）：「用我的版本覆蓋」用現在的版本再送一次，成功就結束編輯', async () => {
    // Arrange：畫面還不知道別人改過（沒有即時更新）
    const { user, section, editor } = await openAndStartEditing();
    await typeInEditor(user, editor, '，我補的');
    const patches = servePatch(theirs);
    serveItem(theirs);

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));
    await user.click(await within(section).findByRole('button', { name: '用我的版本覆蓋' }));

    // Assert
    await waitFor(() => {
      expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    });
    expect(patches.map((request) => request.body)).toEqual([
      { description: '原本的說明，我補的', expectedDescriptionRevision: descriptionRevisionOf('原本的說明') },
      { description: '原本的說明，我補的', expectedDescriptionRevision: descriptionRevisionOf('別人改過的說明') },
    ]);
    expect(await within(section).findByText('原本的說明，我補的')).toBeInTheDocument();
  });

  it('儲存被擋（409）：「看新的內容」換成伺服器上現在的說明，自己的修改不再送', async () => {
    // Arrange
    const { user, section, editor } = await openAndStartEditing();
    await typeInEditor(user, editor, '，我補的');
    const patches = servePatch(theirs);
    serveItem(theirs);
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Act
    await user.click(await within(section).findByRole('button', { name: '看新的內容' }));

    // Assert
    await waitFor(() => {
      expect(editorText(editor)).toBe('別人改過的說明');
    });
    expect(within(section).queryByText(CHANGED)).not.toBeInTheDocument();
    expect(patches).toHaveLength(1);
  });

  it('「用我的版本覆蓋」在重抓與送出期間停用（aria-disabled），連按只送一次', async () => {
    // Arrange
    const { user, section, editor } = await openAndStartEditing();
    await typeInEditor(user, editor, '，我補的');
    const patches = servePatch(theirs);
    server.use(
      http.get(`/v1/items/${item.key}`, async () => {
        await delay(300);
        return ok(theirs);
      }),
    );
    await user.click(within(section).getByRole('button', { name: '儲存' }));
    const overwrite = await within(section).findByRole('button', { name: '用我的版本覆蓋' });

    // Act
    await user.click(overwrite);
    await user.click(overwrite);

    // Assert
    expect(overwrite).toHaveAttribute('aria-disabled', 'true');
    await waitFor(() => {
      expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    });
    expect(patches).toHaveLength(2);
  });

  it('還原了過時的草稿：送的是顯示過時提示那一刻的版本，之後別人又改了，儲存照樣 409', async () => {
    // Arrange：離開之後別人改了說明（theirs），打開時畫面用的是 theirs；之後別人又改了（newest）
    const newest = detailOf(item, '又被別人改過的說明');
    sessionStorage.setItem('draft.description.SMK-7', JSON.stringify({ text: '我的版本', base: '原本的說明' }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(theirs);
    serveActivity(item.key, []);
    const patches = servePatch(newest);
    const live = createFakeLiveConnection();
    renderApp(`/projects/SMK/items/${item.key}`, { liveConnection: () => live });
    const section = await screen.findByRole('region', { name: '說明' });
    await within(section).findByRole('button', { name: '看伺服器上的內容（捨棄還沒儲存的說明）' });

    // Act：之後又被改了，畫面重抓到
    serveItem(newest);
    live.emitItems({ projectKey: 'SMK', itemKeys: [item.key], actorId: 'someone-else' });
    await waitFor(() => {
      expect(screen.queryByText('別人改過的說明')).not.toBeInTheDocument();
    });
    await new Promise((resolve) => setTimeout(resolve, 300));
    await userSave(section);

    // Assert
    expect(await within(section).findByRole('button', { name: '用我的版本覆蓋' })).toBeInTheDocument();
    expect(patches).toHaveLength(1);
    expect(patches[0]?.body).toEqual({ description: '我的版本', expectedDescriptionRevision: descriptionRevisionOf('別人改過的說明') });
  });

  it('自己儲存之後不會誤報衝突（別人沒改）', async () => {
    // Arrange
    const { user, live, section, editor } = await openAndStartEditing();
    const patches = servePatch(mine);
    await typeInEditor(user, editor, '，補');

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));
    await within(section).findByRole('button', { name: '編輯說明' });
    live.emitItems({ projectKey: 'SMK', itemKeys: [item.key] });

    // Assert
    expect(patches).toHaveLength(1);
    expect(within(section).queryByText(CHANGED)).not.toBeInTheDocument();
  });
});
