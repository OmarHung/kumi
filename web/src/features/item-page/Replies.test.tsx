import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import type { Activity } from '../../api/types';
import { commented, created, failComment, manyComments, memberRef, serveActivity, serveComment, showAllActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { clearEditor, editorText, findEditor, typeInEditor } from '../../test/richText';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const COMMENTER: readonly Permission[] = ['item.read', 'comment.create', 'blueprint.read'];
const REPLY_BUTTON = '回覆 範例使用者 的留言';

async function openThread(history: readonly Activity[], permissions: readonly Permission[] = COMMENTER) {
  signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: permissions })] });
  serveItem(task);
  const activity = serveActivity(task.key, history);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  await within(section).findByRole('list', { name: '活動與留言' });
  return { ...view, section, activity };
}

function repliesOf(section: HTMLElement, text: string): HTMLElement {
  const entry = within(section).getByText(text).closest('li');
  if (entry === null) throw new Error(`找不到含「${text}」的那一筆`);
  return within(entry).getByRole('list', { name: '回覆 範例使用者 的留言的回覆' });
}

describe('留言的回覆：回覆框', () => {
  it('回覆框的 Tab 順序和畫面一致：文字框、取消、送出回覆（插入圖片或檔案在上方的工具列裡）', async () => {
    // Arrange
    const { section, user } = await openThread([created(), commented('最上層的留言')], smkDetail().myPermissions);
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));
    await typeInEditor(user, await findEditor(REPLY_BUTTON, section), '想回');

    // Act
    const order: (string | null)[] = [];
    for (let step = 0; step < 2; step += 1) {
      await user.tab();
      order.push(document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent ?? null);
    }

    // Assert
    expect(order).toEqual(['取消', '送出回覆']);
  });
});

describe('留言的回覆：排法', () => {
  it('回覆的清單名稱是「回覆 作者 的留言的回覆」，和同一串的回覆框（「回覆 作者 的留言」）分得開', async () => {
    // Arrange
    const top = commented('最上層的留言');
    const reply = commented('一則回覆', { replyToId: top.id, actor: memberRef });

    // Act
    const { section, user } = await openThread([created(), top, reply]);
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));

    // Assert
    expect(within(section).getAllByRole('list', { name: /回覆 範例使用者 的留言/ })).toHaveLength(1);
    expect(within(section).getByRole('list', { name: '回覆 範例使用者 的留言的回覆' })).toBeInTheDocument();
    expect(within(section).getByRole('form', { name: '回覆 範例使用者 的留言' })).toBeInTheDocument();
  });

  it('回覆收在那一則底下（依時間），不另外出現在最上層的時間軸', async () => {
    // Arrange
    const top = commented('最上層的留言');
    const later = commented('後來的留言');
    const first = commented('先回的', { replyToId: top.id, actor: memberRef, at: '2026-11-13T09:00:00Z' });
    const second = commented('後回的', { replyToId: top.id, at: '2026-11-13T10:00:00Z' });

    // Act
    const { section } = await openThread([created(), top, first, second, later]);

    // Assert
    const timeline = within(section).getByRole('list', { name: '活動與留言' });
    expect(Array.from(timeline.children).map((child) => child.getAttribute('data-activity-id'))).toEqual([
      expect.any(String),
      top.id,
      later.id,
    ]);
    const replies = repliesOf(section, '最上層的留言');
    expect(within(replies).getAllByRole('listitem').map((item) => item.getAttribute('data-activity-id'))).toEqual([first.id, second.id]);
  });

  it('沒有回覆的留言不多一個空的清單', async () => {
    // Arrange & Act
    const { section } = await openThread([created(), commented('沒人回')]);

    // Assert
    expect(within(section).queryByRole('list', { name: /的留言的回覆$/ })).not.toBeInTheDocument();
  });

  it('載入更多時，新的一頁裡的回覆歸到已經載入的那一則底下', async () => {
    // Arrange
    const top = commented('第一頁的留言');
    const reply = commented('第二頁的回覆', { replyToId: top.id, at: '2026-11-14T00:00:00Z' });
    const { user, section } = await openThread([top, ...manyComments(55), reply]);
    expect(within(section).queryByText('第二頁的回覆')).not.toBeInTheDocument();

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await within(repliesOf(section, '第一頁的留言')).findByText('第二頁的回覆')).toBeInTheDocument();
  });

  it('找不到它回覆的那一則時，當成一般留言放著，前面寫「回覆一則較早的留言」', async () => {
    // Arrange
    const lost = commented('不知道回誰', { replyToId: '0190a000-0000-7000-8000-ffffffffffff' });

    // Act
    const { section } = await openThread([created(), lost]);

    // Assert
    const entry = within(section).getByText('不知道回誰').closest('li');
    expect(entry).toHaveAttribute('data-activity-id', lost.id);
    expect(within(entry as HTMLElement).getByText('回覆一則較早的留言')).toBeInTheDocument();
    expect(within(section).getAllByRole('list', { name: '活動與留言' })[0]?.lastElementChild).toBe(entry);
  });
});

describe('留言的回覆：回覆的按鈕與回覆框', () => {
  it('每一則最上層留言底下有「回覆」（名稱帶作者）；回覆本身、其他活動沒有', async () => {
    // Arrange
    const top = commented('一則留言');
    const reply = commented('一則回覆', { replyToId: top.id, actor: memberRef });

    // Act
    const { section } = await openThread([created(), top, reply]);

    // Assert
    expect(within(section).getAllByRole('button', { name: /^回覆 / })).toHaveLength(1);
    expect(within(section).getByRole('button', { name: REPLY_BUTTON })).toBeInTheDocument();
  });

  it('沒有 comment.create 的人看不到「回覆」', async () => {
    // Arrange & Act
    const { section } = await openThread([created(), commented('一則留言')], ['item.read', 'blueprint.read']);

    // Assert
    expect(within(section).queryByRole('button', { name: /^回覆 / })).not.toBeInTheDocument();
  });

  it('按「回覆」在那一串的最下面打開回覆框，焦點進去；送出帶 replyTo，成功後出現在那一串，框清空、留著，報讀「已送出回覆」', async () => {
    // Arrange
    const top = commented('一則留言');
    const old = commented('已經有的回覆', { replyToId: top.id, actor: memberRef });
    const { user, section, activity } = await openThread([created(), top, old]);
    const requests = serveComment(task.key, { timeline: activity });

    // Act
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));
    const textarea = await findEditor(REPLY_BUTTON, section);
    await waitFor(() => {
      expect(textarea).toHaveFocus();
    });
    await typeInEditor(user, textarea, '收到');
    await user.click(within(section).getByRole('button', { name: '送出回覆' }));

    // Assert
    const replies = repliesOf(section, '一則留言');
    expect(await within(replies).findByText('收到')).toBeInTheDocument();
    expect(within(replies).getAllByRole('listitem')).toHaveLength(2);
    expect(requests.map((request) => request.body)).toEqual([{ body: '收到', replyTo: top.id }]);
    expect(editorText(textarea)).toBe('');
    expect(textarea).toHaveFocus();
    expect(await within(section).findByText('已送出回覆', { selector: '[role="status"]' })).toBeInTheDocument();
    // 回覆框在那一串的最下面：回覆清單之後
    const form = within(section).getByRole('form', { name: REPLY_BUTTON });
    expect(replies.compareDocumentPosition(form) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('「取消」收起回覆框、焦點回到「回覆」；框裡的字一起不要了', async () => {
    // Arrange
    const top = commented('一則留言');
    const { user, section } = await openThread([created(), top]);
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));
    await typeInEditor(user, await findEditor(REPLY_BUTTON, section), '不送了');

    // Act
    await user.click(within(section).getByRole('button', { name: '取消' }));

    // Assert
    expect(within(section).queryByRole('textbox', { name: REPLY_BUTTON })).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: REPLY_BUTTON })).toHaveFocus();
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));
    expect(editorText(await findEditor(REPLY_BUTTON, section))).toBe('');
  });

  it('框是空的時 Esc 收起來、焦點回到「回覆」；有字時 Esc 不收', async () => {
    // Arrange
    const top = commented('一則留言');
    const { user, section } = await openThread([created(), top]);
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));
    const textarea = await findEditor(REPLY_BUTTON, section);
    await typeInEditor(user, textarea, '字');

    // Act & Assert
    await user.keyboard('{Escape}');
    expect(textarea).toBeInTheDocument();
    await clearEditor(user, textarea);
    await user.keyboard('{Escape}');
    expect(within(section).queryByRole('textbox', { name: REPLY_BUTTON })).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: REPLY_BUTTON })).toHaveFocus();
  });

  it('回覆的草稿依「項目＋那一則」分開：不和最上層的留言框、別串的回覆框混在一起，換頁回來時回覆框還開著', async () => {
    // Arrange
    const one = commented('第一則');
    const two = commented('第二則');
    const { user, section } = await openThread([created(), one, two]);
    await typeInEditor(user, await findEditor('留言', section), '最上層的草稿');
    const [firstReply] = within(section).getAllByRole('button', { name: REPLY_BUTTON });

    // Act
    await user.click(firstReply as HTMLElement);
    await typeInEditor(user, await findEditor(REPLY_BUTTON, section), '回第一則');

    // Assert
    expect(editorText(await findEditor('留言', section))).toBe('最上層的草稿');
    expect(sessionStorage.getItem(`draft.comment.SMK-7.reply.${one.id}`)).toBe('回第一則');
    expect(sessionStorage.getItem(`draft.comment.SMK-7.reply.${two.id}`)).toBeNull();
    expect(sessionStorage.getItem('draft.comment.SMK-7')).toBe('最上層的草稿');
  });

  it('有沒送出的回覆草稿時，回覆框一開始就是開的並說明已還原，不搶焦點', async () => {
    // Arrange
    const top = commented('一則留言');
    sessionStorage.setItem(`draft.comment.SMK-7.reply.${top.id}`, '寫到一半');

    // Act
    const { section } = await openThread([created(), top]);

    // Assert
    const textarea = await findEditor(REPLY_BUTTON, section);
    expect(editorText(textarea)).toBe('寫到一半');
    expect(textarea).not.toHaveFocus();
    const form = within(section).getByRole('form', { name: REPLY_BUTTON });
    expect(await within(form).findByText('已還原上次沒送出的內容。')).toBeInTheDocument();
  });

  it('回覆的那一則找不到（404 comment.not_found）時，在框下面說明，內容留著', async () => {
    // Arrange
    const top = commented('一則留言');
    const { user, section } = await openThread([created(), top]);
    failComment(task.key, 404, 'comment.not_found', '找不到這則留言。');
    await user.click(within(section).getByRole('button', { name: REPLY_BUTTON }));
    const textarea = await findEditor(REPLY_BUTTON, section);

    // Act
    await typeInEditor(user, textarea, '想回覆');
    await user.click(within(section).getByRole('button', { name: '送出回覆' }));

    // Assert
    expect(await within(section).findByText('找不到你要回覆的那則留言，它可能已經不在這個項目裡。')).toBeInTheDocument();
    expect(editorText(textarea)).toBe('想回覆');
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '送出回覆' })).toBeEnabled();
    });
  });
});
