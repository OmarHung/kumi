import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Member } from '../../api/types';
import { setEditorMode } from '../../lib/editorMode';
import { EditorHarness } from '../../test/richTextHarness';

function member(id: string, displayName: string): Member {
  return { personId: id, displayName } as Member;
}

const MEMBERS: readonly Member[] = [member('11111111-aaaa', '王小明'), member('22222222-bbbb', '李大華')];

beforeEach(() => {
  setEditorMode('markdown');
});

describe('Markdown 原文的文字框', () => {
  it("autoFocus 給 'end'：焦點進去、游標在最後面；給 true 時在最前面", async () => {
    // Arrange / Act
    const { unmount } = render(<EditorHarness autoFocus="end" initial={'一二三'} />);
    const atEnd = screen.getByRole<HTMLTextAreaElement>('textbox', { name: '說明' });

    // Assert
    await waitFor(() => {
      expect(atEnd).toHaveFocus();
    });
    expect(atEnd.selectionStart).toBe(3);
    unmount();
    render(<EditorHarness autoFocus initial={'一二三'} />);
    const atStart = screen.getByRole<HTMLTextAreaElement>('textbox', { name: '說明' });
    await waitFor(() => {
      expect(atStart).toHaveFocus();
    });
    expect(atStart.selectionStart).toBe(0);
  });

  it('值就是 Markdown，不經過轉換：`* 一` 不會變成 `- 一`，打字拿到原樣的字', async () => {
    // Arrange
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial={'* 一\n\n\n二  '} onValue={onValue} />);
    const box = screen.getByRole('textbox', { name: '說明' });

    // Act
    await user.click(box);
    await user.keyboard('{Control>}{End}{/Control}{Enter}* 二');

    // Assert
    expect(box).toHaveValue('* 一\n\n\n二  \n* 二');
    expect(onValue).toHaveBeenLastCalledWith('* 一\n\n\n二  \n* 二');
  });

  it('同步畫出來，不下載編輯器：沒有 contenteditable，也沒有格式工具列', () => {
    // Arrange / Act
    const { container } = render(<EditorHarness initial="原文" />);

    // Assert
    expect(screen.getByRole('textbox', { name: '說明' })).toHaveValue('原文');
    expect(container.querySelector('[contenteditable]')).toBeNull();
    expect(screen.queryByRole('toolbar', { name: '格式' })).toBeNull();
    expect(screen.getByRole('group', { name: '編輯方式' })).toBeInTheDocument();
  });

  it('標籤、提示、格式提示連著文字框；格式提示以「Markdown 原文」開頭', () => {
    // Arrange / Act
    render(<EditorHarness hint="Ctrl+Enter 送出" mentionMembers={MEMBERS} />);
    const box = screen.getByRole('textbox', { name: '說明' });

    // Assert
    expect(box).toHaveAccessibleDescription('Ctrl+Enter 送出 Markdown 原文；輸入 @ 提及成員');
  });

  it('錯誤與剩餘字數也連著，錯誤時 aria-invalid', () => {
    // Arrange / Act
    render(<EditorHarness initial={'x'.repeat(95)} maxLength={100} error="寫不下" />);
    const box = screen.getByRole('textbox', { name: '說明' });

    // Assert
    expect(box).toHaveAttribute('aria-invalid', 'true');
    expect(box).toHaveAccessibleDescription(/還可以輸入 5 個字.*寫不下/);
  });

  it('maxLength 擋住多打的字', () => {
    // Arrange / Act
    render(<EditorHarness maxLength={5} />);

    // Assert
    expect(screen.getByRole('textbox', { name: '說明' })).toHaveAttribute('maxlength', '5');
  });

  it('Ctrl+Enter 與 ⌘+Enter 送出，Esc 取消', async () => {
    // Arrange
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    const onCancel = vi.fn();
    render(<EditorHarness onSubmit={onSubmit} onCancel={onCancel} />);
    await user.click(screen.getByRole('textbox', { name: '說明' }));

    // Act
    await user.keyboard('{Control>}{Enter}{/Control}');
    await user.keyboard('{Meta>}{Enter}{/Meta}');
    await user.keyboard('{Escape}');

    // Assert
    expect(onSubmit).toHaveBeenCalledTimes(2);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('輸入法選字中的 Ctrl+Enter 不送出', () => {
    // Arrange
    const onSubmit = vi.fn();
    render(<EditorHarness onSubmit={onSubmit} />);
    const box = screen.getByRole('textbox', { name: '說明' });

    // Act
    fireEvent.keyDown(box, { key: 'Enter', ctrlKey: true, isComposing: true });
    fireEvent.keyDown(box, { key: 'Enter', ctrlKey: true, keyCode: 229 });

    // Assert
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('送出中唯讀，焦點留得住', async () => {
    // Arrange
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial="字" isBusy onValue={onValue} />);
    const box = screen.getByRole('textbox', { name: '說明' });

    // Act
    await user.click(box);
    await user.keyboard('不能打');

    // Assert
    expect(box).toHaveAttribute('readonly');
    expect(box).toHaveFocus();
    expect(onValue).not.toHaveBeenCalled();
  });

  it('控制項只有一份值：外面換內容，文字框跟著', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<EditorHarness initial="舊的" />);

    // Act
    await user.click(screen.getByRole('button', { name: '外面換內容' }));

    // Assert
    expect(screen.getByRole('textbox', { name: '說明' })).toHaveValue('外面換的\n\n- 一');
  });
});

describe('Markdown 原文的提及', () => {
  async function typeAt() {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness mentionMembers={MEMBERS} onValue={onValue} />);
    const box = screen.getByRole('textbox', { name: '說明' });
    await user.click(box);
    await user.keyboard('請 @');
    return { user, onValue, box };
  }

  it('打 @ 列出成員，文字框的 aria 指著清單', async () => {
    // Arrange / Act
    const { box } = await typeAt();
    const list = await screen.findByRole('listbox', { name: '提及成員' });

    // Assert
    expect(screen.getAllByRole('option')).toHaveLength(2);
    expect(box).toHaveAttribute('aria-controls', list.id);
    expect(box).toHaveAttribute('aria-autocomplete', 'list');
    expect(box.getAttribute('aria-activedescendant')).toBe(screen.getAllByRole('option')[0]?.id);
  });

  it('選了插入 @[名稱](mention:編號) 加一個空白，游標在後面，清單關上', async () => {
    // Arrange
    const { user, onValue, box } = await typeAt();
    await screen.findByRole('listbox', { name: '提及成員' });

    // Act
    await user.keyboard('{ArrowDown}{Enter}');

    // Assert
    await waitFor(() => {
      expect(onValue).toHaveBeenLastCalledWith('請 @[李大華](mention:22222222-bbbb) ');
    });
    expect(screen.queryByRole('listbox')).toBeNull();
    expect((box as HTMLTextAreaElement).selectionStart).toBe('請 @[李大華](mention:22222222-bbbb) '.length);
  });

  it('清單開著時 Esc 只關清單，不取消編輯', async () => {
    // Arrange
    const user = userEvent.setup();
    const onCancel = vi.fn();
    render(<EditorHarness mentionMembers={MEMBERS} onCancel={onCancel} />);
    await user.click(screen.getByRole('textbox', { name: '說明' }));
    await user.keyboard('@');
    await screen.findByRole('listbox', { name: '提及成員' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('送出中不列清單', () => {
    // Arrange
    render(<EditorHarness mentionMembers={MEMBERS} isBusy initial="@" />);
    const box = screen.getByRole('textbox', { name: '說明' });

    // Act
    box.focus();
    fireEvent.select(box);

    // Assert
    expect(screen.queryByRole('listbox')).toBeNull();
  });
});
