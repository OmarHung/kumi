import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { readEditorMode, setEditorMode } from '../../lib/editorMode';
import { createFakeUploads, findEditor, tiptapOf } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

const modeGroup = () => screen.getByRole('group', { name: '編輯方式' });
const toRich = () => within(modeGroup()).getByRole('button', { name: '所見即所得' });
const toMarkdown = () => within(modeGroup()).getByRole('button', { name: 'Markdown' });

describe('編輯方式的切換', () => {
  it('預設是所見即所得，切換在工具列最右邊、有格式工具列；選項是「所見即所得」「Markdown」', async () => {
    // Arrange / Act
    render(<EditorHarness initial="原文" />);
    await findEditor('說明');

    // Assert
    const bar = screen.getByRole('toolbar', { name: '格式' }).parentElement;
    expect(bar?.lastElementChild).toContainElement(modeGroup());
    expect(toRich()).toHaveAttribute('aria-pressed', 'true');
    expect(toMarkdown()).toHaveAttribute('aria-pressed', 'false');
  });

  it('切到 Markdown：文字框的值就是原文，格式按鈕拿掉只留切換，偏好記起來，焦點到文字框、游標在最前面', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<EditorHarness initial={'* 一\n\n二'} />);
    await findEditor('說明');

    // Act
    await user.click(toMarkdown());

    // Assert
    const box = screen.getByRole<HTMLTextAreaElement>('textbox', { name: '說明' });
    expect(box).toHaveValue('* 一\n\n二');
    expect(box).toHaveFocus();
    expect(box.selectionStart).toBe(0);
    expect(box.selectionEnd).toBe(0);
    expect(screen.queryByRole('toolbar', { name: '格式' })).toBeNull();
    expect(toMarkdown()).toHaveAttribute('aria-pressed', 'true');
    expect(readEditorMode()).toBe('markdown');
  });

  it('切回來：編輯器顯示同樣的內容，焦點到編輯器；沒改就回傳切換前的原文（不正規化）', async () => {
    // Arrange
    const user = userEvent.setup();
    const onValue = vi.fn();
    setEditorMode('markdown');
    render(<EditorHarness initial={'* 一\n* 二'} onValue={onValue} />);

    // Act
    await user.click(toRich());

    // Assert
    const editor = await findEditor('說明');
    expect(editor.querySelectorAll('li')).toHaveLength(2);
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    const caret = tiptapOf(editor).state.selection.$from;
    expect(caret.parent.textContent).toBe('一');
    expect(caret.parentOffset).toBe(0);
    expect(onValue).not.toHaveBeenCalled();
    expect(readEditorMode()).toBe('rich');
  });

  it('在原文裡改了字再切回來，編輯器看得到改過的字', async () => {
    // Arrange
    const user = userEvent.setup();
    setEditorMode('markdown');
    render(<EditorHarness initial="前" />);
    await user.click(screen.getByRole('textbox', { name: '說明' }));
    await user.keyboard('{End}**粗**');

    // Act
    await user.click(toRich());

    // Assert
    const editor = await findEditor('說明');
    expect(editor.querySelector('strong')).toHaveTextContent('粗');
  });

  it('偏好是 Markdown 時不下載編輯器那一包；偏好存在 localStorage 的 editor.mode', () => {
    // Arrange
    setEditorMode('markdown');

    // Act
    render(<EditorHarness />);

    // Assert
    expect(localStorage.getItem('editor.mode')).toBe('markdown');
    expect(screen.getByRole('textbox', { name: '說明' }).tagName).toBe('TEXTAREA');
  });

  it('偏好是共用的：一個編輯器切換，頁面上另一個跟著換', async () => {
    // Arrange
    const user = userEvent.setup();
    render(
      <>
        <EditorHarness initial="甲" />
        <EditorHarness initial="乙" label="留言" />
      </>,
    );
    await findEditor('說明');

    // Act
    await user.click(within(screen.getAllByRole('group', { name: '編輯方式' })[0] as HTMLElement).getByRole('button', { name: 'Markdown' }));

    // Assert
    expect(screen.getAllByRole('textbox').map((box) => box.tagName)).toEqual(['TEXTAREA', 'TEXTAREA']);
  });

  it('送出中：切換用 aria-disabled 停住並寫出原因，按了不切換，焦點不掉', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<EditorHarness isBusy />);
    await findEditor('說明');

    // Act
    await user.click(toMarkdown());

    // Assert
    expect(toMarkdown()).toHaveAttribute('aria-disabled', 'true');
    expect(toMarkdown()).not.toBeDisabled();
    expect(toMarkdown()).toHaveAccessibleDescription('送出中，暫時不能切換');
    expect(screen.getByText('送出中，暫時不能切換')).toBeVisible();
    expect(toMarkdown()).toHaveFocus();
    expect(readEditorMode()).toBe('rich');
  });

  it('上傳中：同樣停住，原因是「還有檔案在上傳」（兩種模式都是）', async () => {
    // Arrange
    const { uploads } = createFakeUploads();
    const uploading = { ...uploads, isUploading: true };
    const { unmount } = render(<EditorHarness uploads={uploading} />);
    await findEditor('說明');

    // Assert
    expect(toMarkdown()).toHaveAttribute('aria-disabled', 'true');
    expect(toMarkdown()).toHaveAccessibleDescription('還有檔案在上傳');
    unmount();

    // Arrange
    setEditorMode('markdown');
    render(<EditorHarness uploads={uploading} />);

    // Assert
    expect(toRich()).toHaveAttribute('aria-disabled', 'true');
    expect(toRich()).toHaveAccessibleDescription('還有檔案在上傳');
  });

  it('切換上的 Esc 與 Ctrl+Enter 和在內容裡一樣：取消、送出', async () => {
    // Arrange
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    const onCancel = vi.fn();
    setEditorMode('markdown');
    render(<EditorHarness onSubmit={onSubmit} onCancel={onCancel} />);
    toMarkdown().focus();

    // Act
    await user.keyboard('{Escape}{Control>}{Enter}{/Control}');

    // Assert
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('收起來的留言框：切換跟著看不到（和工具列一樣）', () => {
    // Arrange / Act
    setEditorMode('markdown');
    render(<EditorHarness isCollapsed />);

    // Assert
    expect(modeGroup().closest('[data-collapsed="true"]')).not.toBeNull();
  });
});
