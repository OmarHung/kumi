import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Member } from '../../api/types';
import { created, serveActivity, serveComment, showAllActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { clearEditor, editorText, findEditor, tiptapOf, typeInEditor } from '../../test/richText';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '第一段說明');
const alice: Member = { personId: '0190a000-0000-7000-8000-000000000003', displayName: 'Alice Wang', role: 'commenter' };

async function openItem() {
  signedInAs(exampleOwner, { details: [smkDetail({ members: [ownerAsAdmin, memberAsEditor, alice] })] });
  serveItem(task);
  const activity = serveActivity(task.key, [created()]);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  await within(section).findByRole('list', { name: '活動與留言' });
  return { ...view, section, activity, comment: await findEditor('留言', section) };
}

/** 留言框存在分頁裡的草稿（Markdown）：編輯器的內容用它斷言，不看編輯器裡的畫面。 */
async function expectDraft(markdown: string): Promise<void> {
  await waitFor(() => {
    expect(sessionStorage.getItem('draft.comment.SMK-7')).toBe(markdown);
  });
}

/** 清單裡的選項照順序是這些人（頭像是裝飾，不算在名稱裡）。 */
function expectOptions(names: readonly string[]): void {
  const options = screen.getAllByRole('option');
  expect(options).toHaveLength(names.length);
  names.forEach((name, index) => {
    expect(options[index]).toHaveAccessibleName(name);
  });
}

describe('提及：打 @ 打開成員的清單', () => {
  it('打 @ 列出專案的成員（不含自己）；編輯器一直是 textbox（不是 combobox、沒有 aria-expanded），用 aria-controls 與 aria-activedescendant 指著清單與反白的那一個', async () => {
    // Arrange
    const { user, comment } = await openItem();
    expect(comment).not.toHaveAttribute('aria-controls');

    // Act
    await typeInEditor(user, comment, '@');

    // Assert
    const input = comment;
    const list = screen.getByRole('listbox', { name: '提及成員' });
    expectOptions(['範例成員', 'Alice Wang']);
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(input).toHaveAttribute('role', 'textbox');
    expect(input).not.toHaveAttribute('aria-expanded');
    expect(input).toHaveAttribute('aria-controls', list.id);
    expect(input).toHaveAttribute('aria-autocomplete', 'list');
    const [first] = within(list).getAllByRole('option');
    expect(input).toHaveAttribute('aria-activedescendant', first?.id);
    expect(first).toHaveAttribute('aria-selected', 'true');
    expect(input).toHaveFocus();
  });

  it('照打的字篩，不分大小寫；打到沒有符合的就只剩一列說明', async () => {
    // Arrange
    const { user, comment } = await openItem();

    // Act
    await typeInEditor(user, comment, '@ali');

    // Assert
    expectOptions(['Alice Wang']);
    await typeInEditor(user, comment, 'zz');
    expect(screen.getAllByRole('option')).toHaveLength(1);
    expect(screen.getByRole('option')).toHaveTextContent('沒有符合的成員');
    expect(screen.getByRole('option')).toHaveAttribute('aria-disabled', 'true');
    expect(comment).toHaveAttribute('aria-activedescendant', screen.getByRole('option').id);
  });

  it('報讀軟體聽得到有幾位成員可以選', async () => {
    // Arrange
    const { user, comment, section } = await openItem();

    // Act
    await typeInEditor(user, comment, '@');

    // Assert
    await waitFor(() => {
      expect(section).toHaveTextContent('2 位成員可以提及，用上下鍵選擇，Enter 確定');
    });
  });

  it.each([
    ['Email 的 @', 'user@example'],
    ['@ 後面馬上是空白', '@ 範'],
    ['@ 後面已經空了兩格', '@範 例 使'],
    ['沒有 @', '範例'],
  ])('%s 不打開清單', async (_name, text) => {
    // Arrange
    const { user, comment } = await openItem();

    // Act
    await typeInEditor(user, comment, text);

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(comment).not.toHaveAttribute('aria-controls');
  });

  it('編輯器失去焦點就關上', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@');
    expect(await screen.findByRole('listbox')).toBeInTheDocument();

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});

describe('提及：誰在清單裡', () => {
  it('目前登入的人不在清單裡（提及自己不會通知），打自己的名字也找不到', async () => {
    // Arrange
    const { user, comment } = await openItem();

    // Act
    await typeInEditor(user, comment, '@範例使');

    // Assert
    expect(screen.queryByRole('option', { name: '範例使用者' })).not.toBeInTheDocument();
    expect(screen.getByRole('option')).toHaveTextContent('沒有符合的成員');
  });

  it('搜尋字可以含一個空白：「Alice W」找得到 Alice Wang，再多一個空白就不是在找人了', async () => {
    // Arrange
    const { user, comment } = await openItem();

    // Act
    await typeInEditor(user, comment, '@Alice W');

    // Assert
    expectOptions(['Alice Wang']);
    await typeInEditor(user, comment, 'a ');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('沒有符合時 Esc 只關清單一次，文字不動；再按 Esc 才輪到外面（說明的取消編輯）', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@Nobody Here');
    expect(await screen.findByRole('option')).toHaveTextContent('沒有符合的成員');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(editorText(comment)).toBe('@Nobody Here');
  });
});

describe('提及：鍵盤', () => {
  it('↑↓ 移動反白（繞回另一端），反白的選項是 aria-selected，編輯器的 aria-activedescendant 跟著', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@');
    const options = await screen.findAllByRole('option');

    // Act & Assert
    await user.keyboard('{ArrowDown}');
    expect(options[1]).toHaveAttribute('aria-selected', 'true');
    expect(comment).toHaveAttribute('aria-activedescendant', options[1]?.id);
    await user.keyboard('{ArrowDown}');
    expect(options[0]).toHaveAttribute('aria-selected', 'true');
    await user.keyboard('{ArrowUp}');
    expect(options[1]).toHaveAttribute('aria-selected', 'true');
    expect(editorText(comment)).toBe('@');
  });

  it('Enter 插入 @[名稱](mention:編號) 加一個空白，不換行，清單關上，焦點與游標留在後面', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '請 @');
    await screen.findByRole('listbox');

    // Act
    await user.keyboard('{Enter}');

    // Assert
    const mention = `@[範例成員](mention:${exampleMember.personId}) `;
    await expectDraft(`請 ${mention}`.trimEnd());
    expect(comment).toHaveFocus();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    await typeInEditor(user, comment, '看一下');
    await expectDraft(`請 ${mention}看一下`);
  });

  it('Tab 也是選取，焦點不離開編輯器', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@Ali');
    await screen.findByRole('listbox');

    // Act
    await user.keyboard('{Tab}');

    // Assert
    await expectDraft(`@[Alice Wang](mention:${alice.personId})`);
    expect(comment).toHaveFocus();
  });

  it('Esc 只關上清單：文字照舊；同一個 @ 繼續打字不會再打開，刪掉重打 @ 才再開', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@A');
    await screen.findByRole('listbox');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(editorText(comment)).toBe('@A');
    await user.keyboard('l');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    await clearEditor(user, comment);
    await typeInEditor(user, comment, '@');
    expect(await screen.findByRole('listbox')).toBeInTheDocument();
  });

  it('沒有符合的成員時 Enter 照舊是換行，Tab 照舊離開編輯器', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@zzz');
    await screen.findByRole('listbox');

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(comment.querySelectorAll('p')).toHaveLength(2);
    expect(editorText(comment)).toBe('@zzz');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('Ctrl+Enter 還是送出：留言送出的是插入好的寫法', async () => {
    // Arrange
    const { user, comment, activity } = await openItem();
    const requests = serveComment(task.key, { timeline: activity });
    await typeInEditor(user, comment, '@Ali');
    await screen.findByRole('listbox');
    await user.keyboard('{Enter}');

    // Act
    await typeInEditor(user, comment, '麻煩看一下');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ body: `@[Alice Wang](mention:${alice.personId}) 麻煩看一下` });
  });
});

describe('提及：滑鼠', () => {
  it('點選項插入，焦點不離開編輯器', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@');
    const option = await screen.findByRole('option', { name: 'Alice Wang' });

    // Act
    await user.click(option);

    // Assert
    await expectDraft(`@[Alice Wang](mention:${alice.personId})`);
    expect(comment).toHaveFocus();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});

describe('提及：輸入法', () => {
  it('選字中不打開；選字結束時游標前面是 @ 加字才打開', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await user.click(comment);

    // Act：注音輸入「範」的過程（選字中，編輯器的內容是組字用的暫存字）
    fireEvent.compositionStart(comment);
    tiptapOf(comment).commands.insertContent('@');

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    tiptapOf(comment).commands.insertContent('範');
    fireEvent.compositionEnd(comment);
    expect(await screen.findByRole('listbox')).toBeInTheDocument();
    expectOptions(['範例成員']);
  });

  it('輸入法的按鍵不是清單的：選字中的方向鍵與 Enter 都不移動反白、不插入、不換段落', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@');
    const options = await screen.findAllByRole('option');

    // Act：選字中。Safari 在選字結束之後才送出 Enter（keyCode 229）那一段交給 ProseMirror 自己處理，只有真的 Safari 才有，jsdom 測不到。
    fireEvent.compositionStart(comment);
    fireEvent.keyDown(comment, { key: 'ArrowDown', isComposing: true });
    fireEvent.keyDown(comment, { key: 'Enter', isComposing: true });

    // Assert
    expect(editorText(comment)).toBe('@');
    expect(comment.querySelectorAll('p')).toHaveLength(1);
    expect(options[0]).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('listbox')).toBeInTheDocument();
  });

  it('選字中按 Enter 不會插入提及（編輯器裡不會冒出提及的節點）', async () => {
    // Arrange
    const { user, comment } = await openItem();
    await typeInEditor(user, comment, '@');
    await screen.findByRole('listbox');

    // Act
    fireEvent.compositionStart(comment);
    tiptapOf(comment).commands.insertContent('ㄒ');
    fireEvent.keyDown(comment, { key: 'Enter', isComposing: true });

    // Assert
    expect(comment.querySelector('.mention')).toBeNull();
    expect(editorText(comment)).toBe('@ㄒ');
  });
});

describe('提及：說明的編輯器', () => {
  async function openDescriptionEditor() {
    signedInAs(exampleOwner, { details: [smkDetail({ members: [ownerAsAdmin, memberAsEditor, alice] })] });
    serveItem(task);
    serveActivity(task.key, [created()]);
    const view = renderApp('/projects/SMK/items/SMK-7');
    const region = await screen.findByRole('region', { name: '說明' });
    await view.user.click(within(region).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', region);
    await clearEditor(view.user, editor);
    return { ...view, region, editor };
  }

  /** 分頁裡存的草稿（Markdown）。 */
  function storedDraftText(): string | null {
    const raw = sessionStorage.getItem('draft.description.SMK-7');
    if (raw === null) return null;
    const parsed: unknown = JSON.parse(raw);
    return typeof parsed === 'object' && parsed !== null && 'text' in parsed && typeof parsed.text === 'string' ? parsed.text : raw;
  }

  it('同樣的清單與插入', async () => {
    // Arrange
    const { user, editor } = await openDescriptionEditor();

    // Act
    await typeInEditor(user, editor, '負責人 @範例成');
    await screen.findByRole('listbox');
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(storedDraftText()?.trimEnd()).toBe(`負責人 @[範例成員](mention:${exampleMember.personId})`);
    });
  });

  it('Esc 先關上清單，不會取消編輯；再按一次才取消', async () => {
    // Arrange
    const { user, region, editor } = await openDescriptionEditor();
    await typeInEditor(user, editor, '@');
    await screen.findByRole('listbox');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(within(region).getByRole('textbox', { name: '說明' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(within(region).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
  });

  it('編輯器下面有一行「支援 Markdown；輸入 @ 提及成員」，連在編輯器的說明上', async () => {
    // Arrange & Act
    const { editor } = await openDescriptionEditor();

    // Assert
    expect(editor).toHaveAccessibleDescription(/支援 Markdown；輸入 @ 提及成員/);
  });
});

describe('提及：留言框', () => {
  it('收起來的一行也有同一行提示（連在編輯器的說明上）', async () => {
    // Arrange & Act
    const { comment } = await openItem();
    await flushPending();

    // Assert
    expect(comment).toHaveAccessibleDescription(/支援 Markdown；輸入 @ 提及成員/);
  });
});
