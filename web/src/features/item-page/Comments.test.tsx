import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import { commented, created, failComment, manyComments, serveActivity, serveComment } from '../../test/activity';
import type { Activity } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const COMMENTER: readonly Permission[] = ['item.read', 'comment.create', 'blueprint.read'];

async function openComments(permissions: readonly Permission[] = COMMENTER, history: readonly Activity[] = [created(), commented('先前的留言')]) {
  signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: permissions })] });
  serveItem(task);
  const activity = serveActivity(task.key, history);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  // 預設看的是「留言」：這個檔案測的是留言，不選「全部」。
  await within(section).findByRole('list', { name: '留言' });
  return { ...view, section, activity };
}

describe('留言', () => {
  it('留言者可以留言，但不能改欄位', async () => {
    // Arrange & Act
    const { section } = await openComments();

    // Assert
    expect(await findEditor('留言', section)).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: '欄位' })).queryByRole('button')).not.toBeInTheDocument();
  });

  it('沒有 comment.create 的人看不到留言框', async () => {
    // Arrange & Act
    const { section } = await openComments(['item.read', 'blueprint.read']);

    // Assert
    expect(within(section).queryByRole('textbox', { name: '留言' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '送出留言' })).not.toBeInTheDocument();
  });

  it('空白時不能送出', async () => {
    // Arrange
    const { user, section } = await openComments();
    const requests = serveComment(task.key);
    const textarea = await findEditor('留言', section);
    const send = within(section).getByRole('button', { name: '送出留言' });
    expect(send).toBeDisabled();

    // Act
    await typeInEditor(user, textarea, '   {Enter}  ');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(send).toBeDisabled();
    expect(requests).toHaveLength(0);
  });

  it('Ctrl+Enter 送出；成功後清空、接在時間軸最後，報讀「已送出留言」；之後重抓也還在', async () => {
    // Arrange
    const { user, section, activity } = await openComments();
    const requests = serveComment(task.key, { timeline: activity });
    const textarea = await findEditor('留言', section);

    // Act
    await typeInEditor(user, textarea, '  看起來不錯{Enter}明天再確認  ');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    const list = within(section).getByRole('list', { name: '留言' });
    expect(await within(list).findByText('明天再確認')).toBeInTheDocument();
    expect(list.lastElementChild).toHaveTextContent('範例使用者 留言');
    expect(editorText(textarea)).toBe('');
    expect(textarea).toHaveFocus();
    expect(requests.map((request) => request.body)).toEqual([{ body: '看起來不錯\n\n明天再確認' }]);
    expect(within(section).getAllByRole('status').at(-1)).toHaveTextContent(/^已送出留言$/);
    await waitFor(() => {
      expect(activity.requests).toHaveLength(2);
    });
    expect(within(list).getAllByText('明天再確認')).toHaveLength(1);
    expect(list.lastElementChild).toHaveTextContent('看起來不錯');
  });

  it('送出時剛好有活動在重抓，舊的結果不會把剛送出的留言蓋掉', async () => {
    // Arrange
    const history = [created(), commented('先前的留言')];
    const { user, section, activity, queryClient } = await openComments(COMMENTER, history);
    const comments = serveComment(task.key, { timeline: activity });
    let release: () => void = () => undefined;
    server.use(
      http.get(
        '/v1/items/SMK-7/activity',
        async () => {
          await new Promise<void>((resolve) => {
            release = resolve;
          });
          return ok({ items: history, nextCursor: null });
        },
        { once: true },
      ),
    );
    void queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Act
    await typeInEditor(user, await findEditor('留言', section), '剛寫的留言');
    await user.keyboard('{Control>}{Enter}{/Control}');
    await within(section).findByText('剛寫的留言');
    release();

    // Assert
    await waitFor(() => {
      expect(queryClient.isFetching()).toBe(0);
    });
    expect(comments).toHaveLength(1);
    expect(within(section).getByText('剛寫的留言')).toBeInTheDocument();
  });

  it('剛好載滿一頁時留言，重抓跨過頁界後還是看得到，不必按「載入更多」', async () => {
    // Arrange
    const { user, section, activity } = await openComments(COMMENTER, [created(), ...manyComments(50)]);
    serveComment(task.key, { timeline: activity });
    await within(section).findByText('留言 50');

    // Act
    await typeInEditor(user, await findEditor('留言', section), '第五十一筆');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    const list = within(section).getByRole('list', { name: '留言' });
    await waitFor(() => {
      expect(activity.requests).toHaveLength(3);
    });
    expect(list.lastElementChild).toHaveTextContent('第五十一筆');
    expect(list.children).toHaveLength(51);
    expect(within(section).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
  });

  it('按「送出留言」也可以送出，送出中不能再按', async () => {
    // Arrange
    const { user, section, activity } = await openComments();
    let release: () => void = () => undefined;
    server.use(
      http.post('/v1/items/SMK-7/comments', async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        const posted = commented('收到');
        activity.add(posted);
        return ok(posted, 201);
      }),
    );

    // Act
    await typeInEditor(user, await findEditor('留言', section), '收到');
    await user.click(within(section).getByRole('button', { name: '送出留言' }));

    // Assert
    const pending = await within(section).findByRole('button', { name: '送出中…' });
    expect(pending).toBeDisabled();
    release();
    expect(await within(section).findByRole('button', { name: '送出留言' })).toBeDisabled();
    expect(within(section).getByText('收到')).toBeInTheDocument();
  });

  it('失敗時顯示訊息並留著內容', async () => {
    // Arrange
    const { user, section } = await openComments();
    failComment(task.key, 422, 'comment.body_invalid', '留言裡有不能用的字元。', [
      { code: 'comment.body_invalid', path: 'body', message: '留言裡有不能用的控制字元。' },
    ]);
    const textarea = await findEditor('留言', section);

    // Act
    await typeInEditor(user, textarea, '有問題的留言');
    await user.click(within(section).getByRole('button', { name: '送出留言' }));

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('留言裡有不能用的控制字元。');
    expect(editorText(textarea)).toBe('有問題的留言');
    expect(textarea).toHaveAccessibleDescription(/留言裡有不能用的控制字元。/);
    expect(within(section).getByRole('button', { name: '送出留言' })).toBeEnabled();
  });

  it('還有沒載入的活動時留言，時間軸一路讀到這則留言，中間的也接上，不會重複', async () => {
    // Arrange
    const { user, section, activity } = await openComments(COMMENTER, [
      created(),
      ...manyComments(50),
      commented('較新的留言'),
    ]);
    serveComment(task.key, { timeline: activity });
    await within(section).findByText('留言 50');
    expect(within(section).getByRole('button', { name: '載入更多' })).toBeInTheDocument();

    // Act
    await typeInEditor(user, await findEditor('留言', section), '剛寫的留言');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(await within(section).findByText('較新的留言')).toBeInTheDocument();
    const list = within(section).getByRole('list', { name: '留言' });
    expect(within(list).getAllByText('剛寫的留言')).toHaveLength(1);
    expect(list.lastElementChild).toHaveTextContent('剛寫的留言');
    expect(list.children).toHaveLength(52);
    expect(within(section).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
  });
});
