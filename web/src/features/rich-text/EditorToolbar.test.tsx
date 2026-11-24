import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { findEditor, typeInEditor } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

const BUTTON_NAMES = ['粗體', '斜體', '刪除線', '行內程式碼', '標題', '項目清單', '編號清單', '待辦清單', '引用', '程式碼區塊', '連結'];

async function setup(initial = '') {
  const user = userEvent.setup();
  const onValue = vi.fn();
  render(<EditorHarness initial={initial} onValue={onValue} />);
  const editor = await findEditor('說明');
  return { user, onValue, editor, toolbar: screen.getByRole('toolbar', { name: '格式' }) };
}

describe('編輯器的工具列', () => {
  it('依序有格式、區塊、連結的按鈕，只有一個停駐點', async () => {
    const { toolbar } = await setup();
    const buttons = within(toolbar).getAllByRole('button');
    expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual(BUTTON_NAMES);
    expect(buttons.filter((button) => button.tabIndex === 0)).toHaveLength(1);
  });

  it('左右鍵、Home、End 在按鈕之間移動，停駐點跟著走', async () => {
    const { user, toolbar } = await setup();
    const buttons = within(toolbar).getAllByRole('button');
    buttons[0]?.focus();
    await user.keyboard('{ArrowRight}');
    expect(buttons[1]).toHaveFocus();
    expect(buttons[1]?.tabIndex).toBe(0);
    expect(buttons[0]?.tabIndex).toBe(-1);
    await user.keyboard('{End}');
    expect(buttons.at(-1)).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(buttons[0]).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(buttons.at(-1)).toHaveFocus();
    await user.keyboard('{Home}');
    expect(buttons[0]).toHaveFocus();
  });

  it('粗體：選了字按下去變 **粗體**，按鈕是按下的狀態，編輯器的焦點與選取留著', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '重點');
    await user.keyboard('{Control>}a{/Control}');
    await user.click(screen.getByRole('button', { name: '粗體' }));
    expect(onValue).toHaveBeenLastCalledWith('**重點**');
    expect(screen.getByRole('button', { name: '粗體' })).toHaveAttribute('aria-pressed', 'true');
    expect(editor).toHaveFocus();
  });

  it.each([
    ['斜體', '*字*'],
    ['刪除線', '~~字~~'],
    ['行內程式碼', '`字`'],
  ])('%s：選了字按下去存成 %s', async (name, markdown) => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '字');
    await user.keyboard('{Control>}a{/Control}');
    await user.click(screen.getByRole('button', { name }));
    expect(onValue).toHaveBeenLastCalledWith(markdown);
  });

  it.each([
    ['標題', '## 字'],
    ['項目清單', '- 字'],
    ['編號清單', '1. 字'],
    ['待辦清單', '- [ ] 字'],
    ['引用', '> 字'],
    ['程式碼區塊', '```\n字\n```'],
  ])('%s：游標所在的段落變成 %s', async (name, markdown) => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '字');
    await user.click(screen.getByRole('button', { name }));
    expect(onValue).toHaveBeenLastCalledWith(markdown);
  });

  it('標題按兩次回到段落', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '字');
    await user.click(screen.getByRole('button', { name: '標題' }));
    expect(screen.getByRole('button', { name: '標題' })).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByRole('button', { name: '標題' }));
    expect(onValue).toHaveBeenLastCalledWith('字');
  });

  it('底線關掉：Ctrl+U 不產生底線', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '字');
    await user.keyboard('{Control>}a{/Control}{Control>}u{/Control}');
    expect(editor.querySelector('u')).toBeNull();
    expect(onValue).toHaveBeenLastCalledWith('字');
  });
});

describe('編輯器的連結', () => {
  async function openLinkBox(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: '連結' }));
    return within(await screen.findByRole('dialog', { name: '連結' }));
  }

  it('選了字按「連結」，填網址按「套用」：選取的字變成連結，焦點回編輯器', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '看這裡');
    await user.keyboard('{Control>}a{/Control}');
    const box = await openLinkBox(user);
    await user.type(box.getByRole('textbox', { name: '網址' }), 'https://example.com/a');
    await user.click(box.getByRole('button', { name: '套用' }));
    expect(onValue).toHaveBeenLastCalledWith('[看這裡](https://example.com/a)');
    expect(screen.queryByRole('dialog', { name: '連結' })).toBeNull();
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
  });

  it('沒有選取文字時插入網址本身當連結文字', async () => {
    const { user, onValue, editor } = await setup();
    await user.click(editor);
    const box = await openLinkBox(user);
    await user.type(box.getByRole('textbox', { name: '網址' }), 'https://example.com/');
    await user.keyboard('{Enter}');
    // 連結文字和網址一樣時 mdast 寫成 <網址>（CommonMark 的自動連結，顯示時一樣是連結）。
    expect(onValue).toHaveBeenLastCalledWith('<https://example.com/>');
  });

  it('只收 http、https、mailto：javascript: 不收，欄位下面說原因，文件不變', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '字');
    await user.keyboard('{Control>}a{/Control}');
    const box = await openLinkBox(user);
    await user.type(box.getByRole('textbox', { name: '網址' }), 'javascript:alert(1)');
    await user.click(box.getByRole('button', { name: '套用' }));
    expect(box.getByText('只能連到 http、https 或 mailto 的網址')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '連結' })).toBeInTheDocument();
    expect(onValue).toHaveBeenLastCalledWith('字');
  });

  it('mailto 可以', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '寫信');
    await user.keyboard('{Control>}a{/Control}');
    const box = await openLinkBox(user);
    await user.type(box.getByRole('textbox', { name: '網址' }), 'mailto:someone@example.com');
    await user.click(box.getByRole('button', { name: '套用' }));
    expect(onValue).toHaveBeenLastCalledWith('[寫信](mailto:someone@example.com)');
  });

  it('網址是空的時說「請輸入網址」', async () => {
    const { user } = await setup();
    const box = await openLinkBox(user);
    await user.click(box.getByRole('button', { name: '套用' }));
    expect(box.getByText('請輸入網址')).toBeInTheDocument();
  });

  it('游標在連結裡：小框帶出現在的網址，可以改，也可以「移除連結」', async () => {
    const { user, onValue, editor } = await setup('[舊連結](https://example.com/old)');
    await user.click(editor);
    const link = editor.querySelector('a');
    expect(link).not.toBeNull();
    placeCaretIn(link as HTMLElement);
    const box = await openLinkBox(user);
    const field = box.getByRole('textbox', { name: '網址' });
    expect(field).toHaveValue('https://example.com/old');
    await user.clear(field);
    await user.type(field, 'https://example.com/new');
    await user.click(box.getByRole('button', { name: '套用' }));
    expect(onValue).toHaveBeenLastCalledWith('[舊連結](https://example.com/new)');
    placeCaretIn(editor.querySelector('a') as HTMLElement);
    const again = await openLinkBox(user);
    await user.click(again.getByRole('button', { name: '移除連結' }));
    expect(onValue).toHaveBeenLastCalledWith('舊連結');
  });

  it('Ctrl+K 打開小框，Esc 關上並把焦點放回編輯器', async () => {
    const { user, editor } = await setup();
    await user.click(editor);
    await user.keyboard('{Control>}k{/Control}');
    const box = within(await screen.findByRole('dialog', { name: '連結' }));
    expect(box.getByRole('textbox', { name: '網址' })).toHaveFocus();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: '連結' })).toBeNull();
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
  });

  it('小框開著按 Esc 只關小框，不取消編輯', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    render(<EditorHarness onCancel={onCancel} />);
    await findEditor('說明');
    await user.click(screen.getByRole('button', { name: '連結' }));
    await screen.findByRole('dialog', { name: '連結' });
    await user.keyboard('{Escape}');
    expect(onCancel).not.toHaveBeenCalled();
  });
});

describe('編輯器的連結：包在外層的 <form> 裡', () => {
  async function setupInForm() {
    const user = userEvent.setup();
    const onOuterSubmit = vi.fn((event: { preventDefault: () => void }) => {
      event.preventDefault();
    });
    const onValue = vi.fn();
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    render(
      <form onSubmit={onOuterSubmit}>
        <EditorHarness onValue={onValue} />
      </form>,
    );
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '看這裡');
    await user.keyboard('{Control>}a{/Control}');
    await user.keyboard('{Control>}k{/Control}');
    const field = await screen.findByRole('textbox', { name: '網址' });
    await user.type(field, 'https://example.com/a');
    return { user, onOuterSubmit, onValue, consoleError };
  }

  it('網址欄按 Enter：套上連結，外層的表單不送出，也沒有巢狀 <form> 的警告', async () => {
    const { user, onOuterSubmit, onValue, consoleError } = await setupInForm();
    await user.keyboard('{Enter}');
    expect(onValue).toHaveBeenLastCalledWith('[看這裡](https://example.com/a)');
    expect(onOuterSubmit).not.toHaveBeenCalled();
    expect(consoleError.mock.calls.map((call) => String(call[0]) + String(call[1] ?? '')).join('\n')).not.toMatch(/cannot be a descendant|cannot contain a nested/);
  });

  it('按「套用」：套上連結，外層的表單不送出', async () => {
    const { user, onOuterSubmit, onValue } = await setupInForm();
    await user.click(screen.getByRole('button', { name: '套用' }));
    expect(onValue).toHaveBeenLastCalledWith('[看這裡](https://example.com/a)');
    expect(onOuterSubmit).not.toHaveBeenCalled();
  });

  it('輸入法選字中的 Enter 不算套用', async () => {
    const { onValue } = await setupInForm();
    const field = screen.getByRole('textbox', { name: '網址' });
    fireEvent.keyDown(field, { key: 'Enter', isComposing: true });
    expect(onValue).toHaveBeenLastCalledWith('看這裡');
    expect(screen.getByRole('dialog', { name: '連結' })).toBeInTheDocument();
  });

  it('小框不是 <form>（名稱照舊是「連結」的對話框）', async () => {
    await setupInForm();
    expect(screen.getByRole('dialog', { name: '連結' }).querySelector('form')).toBeNull();
  });
});

/** 把游標放進某個元素的文字裡。 */
function placeCaretIn(element: HTMLElement): void {
  const text = element.firstChild;
  if (text === null) return;
  const range = document.createRange();
  range.setStart(text, 1);
  range.collapse(true);
  const selection = window.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(range);
  document.dispatchEvent(new Event('selectionchange'));
}
