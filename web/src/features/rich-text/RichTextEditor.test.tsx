import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { clearEditor, editorText, findEditor, placeCaretAtEnd, tiptapOf, typeInEditor } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

describe('RichTextEditor：名稱、說明、狀態', () => {
  it('編輯器是 textbox，名稱是標籤，aria-describedby 依序連到提示、格式提示、字數與錯誤', async () => {
    // Arrange
    render(<EditorHarness initial={'x'.repeat(95)} maxLength={100} error="寫不下" hint="快速鍵說明" />);
    // Act
    const editor = await findEditor('說明');
    // Assert
    expect(editor).toHaveAttribute('aria-multiline', 'true');
    const ids = (editor.getAttribute('aria-describedby') ?? '').split(' ');
    expect(ids.map((id) => document.getElementById(id)?.textContent)).toEqual(['快速鍵說明', '支援 Markdown', '還可以輸入 5 個字', '寫不下']);
    expect(editor).toHaveAttribute('aria-invalid', 'true');
  });

  it('沒有錯誤也沒有接近上限時，說明只有提示與格式提示', async () => {
    render(<EditorHarness />);
    const editor = await findEditor('說明');
    expect((editor.getAttribute('aria-describedby') ?? '').split(' ')).toHaveLength(2);
    expect(editor).not.toHaveAttribute('aria-invalid');
  });

  it('有 placeholder 時 aria-placeholder 與看得到的提示都在', async () => {
    render(<EditorHarness placeholder="寫點什麼" />);
    const editor = await findEditor('說明');
    expect(editor).toHaveAttribute('aria-placeholder', '寫點什麼');
    expect(editor.querySelector('[data-placeholder="寫點什麼"]')).not.toBeNull();
  });

  it('送出中 aria-readonly，內容不能改，焦點還在', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness isBusy initial="原本的字" onValue={onValue} />);
    const editor = await findEditor('說明');
    editor.focus();
    await user.keyboard('多打的');
    expect(editor).toHaveAttribute('aria-readonly', 'true');
    expect(editor).toHaveAttribute('contenteditable', 'true');
    expect(onValue).not.toHaveBeenCalled();
    expect(editorText(editor)).toBe('原本的字');
    expect(editor).toHaveFocus();
  });

  it('超過上限時改成「超過上限 N 個字」並標成無效', async () => {
    render(<EditorHarness initial={'x'.repeat(105)} maxLength={100} />);
    const editor = await findEditor('說明');
    expect(screen.getByText('超過上限 5 個字')).toBeInTheDocument();
    expect(editor).toHaveAttribute('aria-invalid', 'true');
  });

  it('收起來時工具列與提示看不到（樣式），提示仍連在編輯器上', async () => {
    render(<EditorHarness isCollapsed hint="快速鍵說明" />);
    const editor = await findEditor('說明');
    expect(editor.closest('[data-collapsed="true"]')).not.toBeNull();
    expect(document.getElementById((editor.getAttribute('aria-describedby') ?? '').split(' ')[0] ?? '')).toHaveTextContent('快速鍵說明');
  });
});

describe('RichTextEditor：值的同步', () => {
  it('打字之後 onChange 給的是 Markdown', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness onValue={onValue} />);
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '你好');
    expect(onValue).toHaveBeenLastCalledWith('你好');
  });

  it('Markdown 的輸入規則開著：「## 」變標題、「- 」變清單', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness onValue={onValue} />);
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '## 標題{Enter}- 一項');
    expect(onValue).toHaveBeenLastCalledWith('## 標題\n\n- 一項');
    expect(editor.querySelector('h4[data-level="2"]')).not.toBeNull();
  });

  it('沒改時（原文會被正規化）onChange 一次都不會被呼叫；改了再改回來時給的是原文', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    const original = '* 一\n* 二';
    render(<EditorHarness initial={original} onValue={onValue} />);
    const editor = await findEditor('說明');
    await user.click(editor);
    expect(onValue).not.toHaveBeenCalled();
    placeCaretAtEnd(editor);
    await user.keyboard('X');
    expect(onValue).toHaveBeenLastCalledWith('- 一\n- 二X');
    await user.keyboard('{Backspace}');
    expect(onValue).toHaveBeenLastCalledWith(original);
  });

  it('value 從外面換掉時內容跟著換，換成空字串就清空', async () => {
    const user = userEvent.setup();
    render(<EditorHarness initial="舊的" />);
    const editor = await findEditor('說明');
    await user.click(screen.getByRole('button', { name: '外面換內容' }));
    await waitFor(() => {
      expect(editorText(editor)).toBe('外面換的\n一');
    });
    await user.click(screen.getByRole('button', { name: '外面清空' }));
    await waitFor(() => {
      expect(editorText(editor)).toBe('');
    });
  });

  it('清空（全選加 Backspace）之後 onChange 給空字串', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial="一些字" onValue={onValue} />);
    const editor = await findEditor('說明');
    await clearEditor(user, editor);
    expect(onValue).toHaveBeenLastCalledWith('');
  });
});

describe('RichTextEditor：快速鍵', () => {
  it('Ctrl+Enter 送出，⌘+Enter 也是；不換行', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    const onValue = vi.fn();
    render(<EditorHarness onSubmit={onSubmit} onValue={onValue} />);
    const editor = await findEditor('說明');
    await user.click(editor);
    await user.keyboard('{Control>}{Enter}{/Control}');
    await user.keyboard('{Meta>}{Enter}{/Meta}');
    expect(onSubmit).toHaveBeenCalledTimes(2);
    expect(onValue).not.toHaveBeenCalled();
  });

  it('輸入法選字中的 Ctrl+Enter 不送出', async () => {
    const onSubmit = vi.fn();
    render(<EditorHarness onSubmit={onSubmit} />);
    const editor = await findEditor('說明');
    editor.focus();
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, isComposing: true, bubbles: true, cancelable: true }));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('Esc 取消；沒有 onCancel 時不處理', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const { unmount } = render(<EditorHarness onCancel={onCancel} />);
    const editor = await findEditor('說明');
    await user.click(editor);
    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
    unmount();
    render(<EditorHarness />);
    const another = await findEditor('說明');
    await user.click(another);
    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('輸入法選字中的 Esc 不取消', async () => {
    const onCancel = vi.fn();
    render(<EditorHarness onCancel={onCancel} />);
    const editor = await findEditor('說明');
    editor.focus();
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', isComposing: true, bubbles: true, cancelable: true }));
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('Shift+Enter 換行（單一換行），Enter 開新段落', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness onValue={onValue} />);
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '第一行{Shift>}{Enter}{/Shift}第二行{Enter}第二段');
    expect(onValue).toHaveBeenLastCalledWith('第一行\n第二行\n\n第二段');
  });
});

describe('RichTextEditor：焦點', () => {
  it('autoFocus 在編輯器出現後把焦點放進去，游標在最前面（長的說明一進編輯看到的還是開頭）', async () => {
    render(<EditorHarness autoFocus initial={'第一段\n\n第二段'} />);
    const editor = await findEditor('說明');
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    expect(tiptapOf(editor).state.selection.from).toBe(1);
  });

  it("autoFocus 給 'end'：焦點進去、游標在最後面（編輯既有的留言，接著寫）", async () => {
    render(<EditorHarness autoFocus="end" initial={'第一段\n\n第二段'} />);
    const editor = await findEditor('說明');
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    const { state } = tiptapOf(editor);
    expect(state.selection.from).toBe(state.doc.content.size - 1);
  });

  it('點標籤把焦點放進編輯器', async () => {
    const user = userEvent.setup();
    render(<EditorHarness />);
    const editor = await findEditor('說明');
    await user.click(screen.getByText('說明', { selector: 'span' }));
    // Tiptap 的 focus 指令在下一個畫面才真的聚焦。
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
  });
});
