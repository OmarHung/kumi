import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { created, serveActivity, serveComment, showAllActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { descriptionRevisionOf, detailOf, serveItem, serveItems, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { findEditor, typeInEditor } from '../../test/richText';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

/** Safari 在 compositionend 之後才送出選字用的 Enter：isComposing 已經是 false，keyCode 是 229。 */
const SAFARI_CANDIDATE_ENTER = { key: 'Enter', code: 'Enter', keyCode: 229 } as const;
/** Chrome、Firefox 選字時的 Enter：isComposing 是 true。 */
const COMPOSING_ENTER = { key: 'Enter', code: 'Enter', keyCode: 229, isComposing: true } as const;

const task = detailOf(
  smkItem({ number: 7, title: '首頁設計稿', fields: { tags: ['frontend'], notes: '舊的備註' } }),
  '舊的說明',
);

async function openTask() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  const activity = serveActivity(task.key, [created()]);
  const patches = servePatch(task);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const heading = await screen.findByRole('heading', { level: 1 });
  const section = screen.getByRole('region', { name: '活動與留言' });
  await within(section).findByText(/建立了這個項目/);
  return { ...view, heading, section, patches, activity };
}

describe('注音選字的 Enter 不會送出或儲存', () => {
  it.each([
    ['Safari（isComposing 已經是 false、keyCode 229）', SAFARI_CANDIDATE_ENTER],
    ['其他瀏覽器（isComposing 是 true）', COMPOSING_ENTER],
  ])('頁首的標題：%s', async (_name, candidateEnter) => {
    // Arrange
    const { user, heading, patches } = await openTask();
    await user.click(within(heading).getByRole('button', { name: '標題：首頁設計稿' }));
    const input = within(heading).getByRole('textbox', { name: '標題' });
    await user.type(input, '改');

    // Act
    fireEvent.keyDown(input, candidateEnter);

    // Assert
    expect(within(heading).getByRole('textbox', { name: '標題' })).toHaveValue('首頁設計稿改');
    expect(patches).toHaveLength(0);
    await user.keyboard('{Enter}');
    expect(await within(heading).findByRole('button', { name: '標題：首頁設計稿改' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ title: '首頁設計稿改' }]);
  });

  it('長文字欄位的 Ctrl+Enter', async () => {
    // Arrange
    const { user, patches } = await openTask();
    const fields = screen.getByRole('region', { name: '欄位' });
    await user.click(within(fields).getByRole('button', { name: '備註：舊的備註' }));
    const textarea = within(fields).getByRole('textbox', { name: '備註' });

    // Act
    fireEvent.keyDown(textarea, { ...SAFARI_CANDIDATE_ENTER, ctrlKey: true });

    // Assert
    await user.type(within(fields).getByRole('textbox', { name: '備註' }), '，補充');
    await user.keyboard('{Control>}{Enter}{/Control}');
    expect(await within(fields).findByRole('button', { name: '備註：舊的備註，補充' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { notes: '舊的備註，補充' } }]);
  });

  it('多選的勾選框', async () => {
    // Arrange
    const { user, patches } = await openTask();
    const fields = screen.getByRole('region', { name: '欄位' });
    await user.click(within(fields).getByRole('button', { name: '標籤：前端' }));
    const backend = within(fields).getByRole('checkbox', { name: '後端' });
    await user.click(backend);

    // Act
    fireEvent.keyDown(backend, SAFARI_CANDIDATE_ENTER);

    // Assert
    await user.click(within(fields).getByRole('checkbox', { name: '文件' }));
    await user.keyboard('{Enter}');
    expect(await within(fields).findByRole('button', { name: '標籤：前端、後端、文件' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ fields: { tags: ['frontend', 'backend', 'docs'] } }]);
  });

  it('說明的 Ctrl+Enter', async () => {
    // Arrange
    const { user, patches } = await openTask();
    const region = screen.getByRole('region', { name: '說明' });
    await user.click(within(region).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', region);
    await typeInEditor(user, editor, '，補充');

    // Act
    fireEvent.keyDown(editor, { ...SAFARI_CANDIDATE_ENTER, ctrlKey: true });

    // Assert
    await typeInEditor(user, editor, '，再補充');
    await user.keyboard('{Control>}{Enter}{/Control}');
    expect(await within(region).findByText('舊的說明，補充，再補充')).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ description: '舊的說明，補充，再補充', expectedDescriptionRevision: descriptionRevisionOf('舊的說明') }]);
  });

  it('留言的 Ctrl+Enter', async () => {
    // Arrange
    const { user, section, activity } = await openTask();
    const comments = serveComment(task.key, { timeline: activity });
    const textarea = await findEditor('留言', section);
    await typeInEditor(user, textarea, '看起來不錯');

    // Act
    fireEvent.keyDown(textarea, { ...SAFARI_CANDIDATE_ENTER, metaKey: true });

    // Assert
    expect(comments).toHaveLength(0);
    await typeInEditor(user, textarea, '，明天見');
    await user.keyboard('{Meta>}{Enter}{/Meta}');
    expect(await within(section).findByText('看起來不錯，明天見')).toBeInTheDocument();
    expect(comments.map((request) => request.body)).toEqual([{ body: '看起來不錯，明天見' }]);
  });

  it('下層項目的快速新增：選字的 Enter 不送出表單，真的按 Enter 才新增', async () => {
    // Arrange
    const requirement = detailOf(agyItem({ number: 1, title: '官網需求' }));
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItem(requirement);
    serveActivity(requirement.key, [created()]);
    serveItems('AGY', []);
    const { user } = renderApp('/projects/AGY/items/AGY-1');
    const region = await screen.findByRole('region', { name: '下層項目' });
    const input = within(region).getByRole('textbox', { name: '標題' });
    await user.type(input, '新的下層');

    // Act
    const isSubmitAllowed = fireEvent.keyDown(input, SAFARI_CANDIDATE_ENTER);

    // Assert
    expect(isSubmitAllowed).toBe(false);
    expect(fireEvent.keyDown(input, { key: 'Enter', code: 'Enter', keyCode: 13 })).toBe(true);
    await waitFor(() => {
      expect(input).toHaveValue('新的下層');
    });
  });
});
