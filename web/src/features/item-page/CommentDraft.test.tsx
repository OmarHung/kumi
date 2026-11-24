import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it } from 'vitest';
import { commented, created, serveActivity, serveComment, showAllActivity } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { clearEditor, editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const other = detailOf(smkItem({ number: 8, title: '另一個項目' }));

async function openItem(path = '/projects/SMK/items/SMK-7') {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  serveItem(other);
  const activity = serveActivity(task.key, [created()]);
  serveActivity(other.key, [created()]);
  const view = renderApp(path);
  const section = await findComments();
  return { ...view, section, activity };
}

async function findComments(): Promise<HTMLElement> {
  const section = await screen.findByRole('region', { name: '活動與留言' });
  await within(section).findByRole('list', { name: '活動與留言' });
  return section;
}

/** 從側欄換到「我的工作」，再用瀏覽器的上一頁回來。 */
async function leaveAndComeBack(view: Awaited<ReturnType<typeof openItem>>): Promise<HTMLElement> {
  await view.user.click(within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' }));
  await screen.findByRole('heading', { level: 1, name: '我的工作' });
  await view.router.navigate(-1);
  return findComments();
}

describe('留言的草稿', () => {
  it('寫到一半換頁再回來：內容還在、留言框展開，並說明已還原，有「捨棄」', async () => {
    // Arrange
    const view = await openItem();
    await typeInEditor(view.user, await findEditor('留言', view.section), '寫到一半{Enter}第二行');

    // Act
    const section = await leaveAndComeBack(view);

    // Assert
    const textarea = await findEditor('留言', section);
    expect(editorText(textarea)).toBe('寫到一半\n第二行');
    expect(within(section).getByRole('button', { name: '送出留言' })).toBeEnabled();
    const form = within(section).getByRole('form', { name: '新增留言' });
    expect(await within(form).findByRole('status')).toHaveTextContent('已還原上次沒送出的內容。');
    expect(within(form).getByRole('button', { name: '捨棄沒送出的留言' })).toBeInTheDocument();
    expect(textarea).not.toHaveFocus();
  });

  it('「捨棄」清空留言框、焦點回到留言框；再回來就不會還原', async () => {
    // Arrange
    const view = await openItem();
    await typeInEditor(view.user, await findEditor('留言', view.section), '不要了');
    const section = await leaveAndComeBack(view);
    const form = within(section).getByRole('form', { name: '新增留言' });

    // Act
    await view.user.click(await within(form).findByRole('button', { name: '捨棄沒送出的留言' }));

    // Assert
    const textarea = await findEditor('留言', section);
    expect(editorText(textarea)).toBe('');
    expect(textarea).toHaveFocus();
    expect(within(form).queryByRole('status')).not.toBeInTheDocument();

    // Act
    const again = await leaveAndComeBack(view);

    // Assert
    expect(editorText(await findEditor('留言', again))).toBe('');
    expect(within(again).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
  });

  it('送出成功就清掉草稿，再回來是空的', async () => {
    // Arrange
    const view = await openItem();
    serveComment(task.key, { timeline: view.activity });
    await typeInEditor(view.user, await findEditor('留言', view.section), '已經送出的留言');

    // Act
    await view.user.keyboard('{Control>}{Enter}{/Control}');
    await within(view.section).findByText('已經送出的留言');
    const section = await leaveAndComeBack(view);

    // Assert
    expect(editorText(await findEditor('留言', section))).toBe('');
    expect(within(section).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
  });

  it('只有空白不算草稿；刪光了也不再還原', async () => {
    // Arrange
    const view = await openItem();
    const editor = await findEditor('留言', view.section);
    await typeInEditor(view.user, editor, 'abc');
    await clearEditor(view.user, editor);
    await typeInEditor(view.user, editor, '   ');

    // Act
    const section = await leaveAndComeBack(view);

    // Assert
    expect(editorText(await findEditor('留言', section))).toBe('');
    expect(within(section).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
  });

  it('草稿跟著項目：另一個項目看不到這一則', async () => {
    // Arrange
    const view = await openItem();
    await typeInEditor(view.user, await findEditor('留言', view.section), '給 SMK-7 的');

    // Act
    await view.router.navigate('/projects/SMK/items/SMK-8');
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('另一個項目');
    });
    const section = await findComments();

    // Assert
    expect(editorText(await findEditor('留言', section))).toBe('');
    await waitFor(() => {
      expect(within(section).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
    });
  });

  it('送出中換了頁又回來：還原的是同一段字，送出完成前不能再送；送出成功後清掉，不會留著讓人再送一次', async () => {
    // Arrange
    const view = await openItem();
    const posts: unknown[] = [];
    let release: () => void = () => undefined;
    server.use(
      http.post('/v1/items/SMK-7/comments', async ({ request }) => {
        posts.push(await request.json());
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        const posted = commented('換頁前送出的留言');
        view.activity.add(posted);
        return ok(posted, 201);
      }),
    );
    await typeInEditor(view.user, await findEditor('留言', view.section), '換頁前送出的留言');
    await view.user.keyboard('{Control>}{Enter}{/Control}');
    await within(view.section).findByRole('button', { name: '送出中…' });

    // Act
    const section = await leaveAndComeBack(view);

    // Assert
    const textarea = await findEditor('留言', section);
    expect(editorText(textarea)).toBe('換頁前送出的留言');
    expect(within(section).getByRole('button', { name: '送出中…' })).toBeDisabled();

    // Act
    release();

    // Assert
    await waitFor(() => {
      expect(editorText(textarea)).toBe('');
    });
    expect(within(section).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
    expect(posts).toHaveLength(1);
    expect(sessionStorage.getItem('draft.comment.SMK-7')).toBeNull();
  });

  it('送出成功時留言框裡已經是別的字（例如回來之後又改過）就留著', async () => {
    // Arrange
    const view = await openItem();
    let release: () => void = () => undefined;
    server.use(
      http.post('/v1/items/SMK-7/comments', async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        const posted = commented('第一則');
        view.activity.add(posted);
        return ok(posted, 201);
      }),
    );
    await typeInEditor(view.user, await findEditor('留言', view.section), '第一則');
    await view.user.keyboard('{Control>}{Enter}{/Control}');
    await within(view.section).findByRole('button', { name: '送出中…' });
    sessionStorage.setItem('draft.comment.SMK-7', '另外一段');

    // Act
    const section = await leaveAndComeBack(view);
    release();

    // Assert
    await within(section).findByText('第一則');
    expect(editorText(await findEditor('留言', section))).toBe('另外一段');
    expect(sessionStorage.getItem('draft.comment.SMK-7')).toBe('另外一段');
  });
});
