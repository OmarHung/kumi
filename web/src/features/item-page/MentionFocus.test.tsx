import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { Member } from '../../api/types';
import { findEditor } from '../../test/richText';
import { RichTextEditor } from '../rich-text/RichTextEditor';

const members: readonly Member[] = [{ personId: 'p-1', displayName: 'Alice Wang', role: 'commenter' }];

function Harness({ autoFocus = false }: { readonly autoFocus?: boolean }) {
  const [value, setValue] = useState('');
  return (
    <RichTextEditor
      label="留言"
      value={value}
      onChange={setValue}
      maxLength={1000}
      rows={3}
      hint="提示"
      onSubmit={() => undefined}
      isBusy={false}
      error={null}
      mentionMembers={members}
      autoFocus={autoFocus}
    />
  );
}

/** 把 focusin 擋在 React 的根容器外面：模擬 onFocus 沒來（程式化的 focus、重新掛上時焦點已經在、自動化工具）。 */
function swallowFocusEvents(): () => void {
  const stop = (event: Event) => {
    event.stopPropagation();
  };
  document.addEventListener('focusin', stop, true);
  return () => {
    document.removeEventListener('focusin', stop, true);
  };
}

let restore: (() => void) | undefined;

afterEach(() => {
  restore?.();
  restore = undefined;
});

describe('提及的清單：焦點狀態不只靠 onFocus', () => {
  it('焦點已經在編輯器上、但 React 沒收到 focus 事件：打 @ 照樣打開清單', async () => {
    // Arrange
    restore = swallowFocusEvents();
    const user = userEvent.setup();
    render(<Harness />);
    const textarea = await findEditor('留言');
    textarea.focus();
    expect(textarea).toHaveFocus();

    // Act
    await user.keyboard('@');

    // Assert
    expect(await screen.findByRole('listbox', { name: '提及成員' })).toBeInTheDocument();
  });

  it('autoFocus 掛上就有焦點（草稿還原、重新掛上）：打 @ 照樣打開清單', async () => {
    // Arrange
    restore = swallowFocusEvents();
    const user = userEvent.setup();
    render(<Harness autoFocus />);
    const editor = await findEditor('留言');
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });

    // Act
    await user.keyboard('@Ali');

    // Assert
    expect(await screen.findByRole('option', { name: 'Alice Wang' })).toBeInTheDocument();
  });

  it('沒有焦點（值是程式改的、滑鼠不在上面）不打開清單', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Harness />);
    const textarea = await findEditor('留言');
    await user.click(textarea);
    await user.keyboard('@');
    expect(await screen.findByRole('listbox')).toBeInTheDocument();

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});
