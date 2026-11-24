import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { findEditor, placeCaretAtEnd } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

/** 按一下 Shift+Tab，回傳瀏覽器的預設動作有沒有被擋下。 */
function pressShiftTab(editor: HTMLElement): boolean {
  return !fireEvent.keyDown(editor, { key: 'Tab', shiftKey: true });
}

async function setupWithCaretAtEnd(initial: string, props: Partial<Parameters<typeof EditorHarness>[0]> = {}) {
  const user = userEvent.setup();
  const onValue = vi.fn();
  render(<EditorHarness initial={initial} onValue={onValue} {...props} />);
  const editor = await findEditor('說明');
  await user.click(editor);
  placeCaretAtEnd(editor);
  return { user, onValue, editor };
}

const removers: (() => void)[] = [];

afterEach(() => {
  vi.restoreAllMocks();
  removers.splice(0).forEach((remove) => {
    remove();
  });
});

/** 全域的快捷鍵（useShortcut）聽在 document 上、看 defaultPrevented 決定要不要處理：這裡記下它看到的樣子。 */
function watchDocumentKeys() {
  const seen = vi.fn<(isPrevented: boolean) => void>();
  const listener = (event: KeyboardEvent) => {
    seen(event.defaultPrevented);
  };
  document.addEventListener('keydown', listener);
  removers.push(() => {
    document.removeEventListener('keydown', listener);
  });
  return seen;
}

describe('清單的 Shift+Tab', () => {
  it.each([
    ['項目清單', '- 甲\n- 乙'],
    ['編號清單', '1. 甲\n2. 乙'],
    ['待辦清單', '- [ ] 甲\n- [ ] 乙'],
  ])('%s的第一層項目：不處理，讓瀏覽器把焦點移到上一個控制項（不變回段落）', async (_name, markdown) => {
    const { editor, onValue } = await setupWithCaretAtEnd(markdown);
    const isPrevented = pressShiftTab(editor);
    expect(isPrevented).toBe(false);
    expect(onValue).not.toHaveBeenCalled();
    expect(editor.querySelector('li')).not.toBeNull();
  });

  it.each([
    ['項目清單', '- 甲\n  - 乙', '- 甲\n- 乙'],
    ['待辦清單', '- [ ] 甲\n  - [ ] 乙', '- [ ] 甲\n- [ ] 乙'],
  ])('%s的巢狀項目：往外提一層', async (_name, markdown, lifted) => {
    const { editor, onValue } = await setupWithCaretAtEnd(markdown);
    expect(pressShiftTab(editor)).toBe(true);
    expect(onValue).toHaveBeenLastCalledWith(lifted);
  });

  it('Tab 在不能再縮排的第一個項目：放行', async () => {
    const { editor } = await setupWithCaretAtEnd('- 甲');
    expect(!fireEvent.keyDown(editor, { key: 'Tab' })).toBe(false);
  });
});

describe('送出中的待辦勾選框', () => {
  it('點了不改變：畫面上的勾選狀態和文件一致', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness isBusy initial={'- [ ] 甲\n- [x] 乙'} onValue={onValue} />);
    const editor = await findEditor('說明');
    const [first, second] = Array.from(editor.querySelectorAll<HTMLInputElement>('input[type=checkbox]'));
    await user.click(first as HTMLInputElement);
    await user.click(second as HTMLInputElement);
    expect(first).not.toBeChecked();
    expect(second).toBeChecked();
    expect(onValue).not.toHaveBeenCalled();
    expect(editor.querySelectorAll('li[data-checked="true"]')).toHaveLength(1);
  });

  it('沒有送出中時照常可以勾', async () => {
    const user = userEvent.setup();
    const { editor, onValue } = await setupWithCaretAtEnd('- [ ] 甲');
    await user.click(editor.querySelector('input[type=checkbox]') as HTMLInputElement);
    expect(onValue).toHaveBeenLastCalledWith('- [x] 甲');
  });
});

describe('焦點在工具列的按鈕上', () => {
  async function setupToolbar() {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const onSubmit = vi.fn();
    const seenByDocument = watchDocumentKeys();
    render(<EditorHarness onCancel={onCancel} onSubmit={onSubmit} />);
    await findEditor('說明');
    screen.getByRole('button', { name: '粗體' }).focus();
    return { user, onCancel, onSubmit, seenByDocument };
  }

  it('Esc 取消；到全域的快捷鍵時已經標成處理過（不會關掉面板）', async () => {
    const { user, onCancel, seenByDocument } = await setupToolbar();
    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(seenByDocument).toHaveBeenLastCalledWith(true);
  });

  it('Ctrl+Enter 與 ⌘+Enter 送出', async () => {
    const { user, onSubmit } = await setupToolbar();
    await user.keyboard('{Control>}{Enter}{/Control}');
    await user.keyboard('{Meta>}{Enter}{/Meta}');
    expect(onSubmit).toHaveBeenCalledTimes(2);
  });

  it('小框開著時 Esc 只關小框，不取消', async () => {
    const { user, onCancel } = await setupToolbar();
    await user.click(screen.getByRole('button', { name: '連結' }));
    await screen.findByRole('dialog', { name: '連結' });
    screen.getByRole('button', { name: '連結' }).focus();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: '連結' })).toBeNull();
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('沒有給 onCancel 時 Esc 也標成處理過（和在內容裡一樣不會關掉面板）', async () => {
    const user = userEvent.setup();
    const seenByDocument = watchDocumentKeys();
    render(<EditorHarness />);
    await findEditor('說明');
    screen.getByRole('button', { name: '斜體' }).focus();
    await user.keyboard('{Escape}');
    expect(seenByDocument).toHaveBeenLastCalledWith(true);
  });

  it('已經處理過的按鍵（defaultPrevented）不再處理', async () => {
    const onCancel = vi.fn();
    render(
      <div
        onKeyDownCapture={(event) => {
          event.preventDefault();
        }}
      >
        <EditorHarness onCancel={onCancel} />
      </div>,
    );
    await findEditor('說明');
    fireEvent.keyDown(screen.getByRole('button', { name: '粗體' }), { key: 'Escape' });
    expect(onCancel).not.toHaveBeenCalled();
  });
});

describe('連結的快速鍵與按鈕', () => {
  it('Ctrl+K 打開小框前，先把連結鈕捲進看得到的範圍（窄螢幕時它在工具列的捲動範圍外）', async () => {
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    const { user } = await setupWithCaretAtEnd('字');
    await user.keyboard('{Control>}k{/Control}');
    await screen.findByRole('dialog', { name: '連結' });
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' });
    expect(scrollIntoView.mock.contexts[0]).toBe(screen.getByRole('button', { name: '連結' }));
    // @ts-expect-error 還原成 jsdom 原本沒有這個方法的樣子
    delete Element.prototype.scrollIntoView;
  });

  it('送出中按 Ctrl+K：不開小框，也不讓全域的快速跳轉搶走', async () => {
    const { editor } = await setupWithCaretAtEnd('字', { isBusy: true });
    const isPrevented = !fireEvent.keyDown(editor, { key: 'k', ctrlKey: true });
    expect(isPrevented).toBe(true);
    expect(screen.queryByRole('dialog', { name: '連結' })).toBeNull();
  });

  it('游標在程式碼區塊裡：連結鈕 aria-disabled，點了與 Ctrl+K 都不開小框', async () => {
    const user = userEvent.setup();
    const { editor } = await setupWithCaretAtEnd('```\ncode\n```');
    const link = screen.getByRole('button', { name: '連結' });
    await waitFor(() => {
      expect(link).toHaveAttribute('aria-disabled', 'true');
    });
    await user.click(link);
    fireEvent.keyDown(editor, { key: 'k', ctrlKey: true });
    expect(screen.queryByRole('dialog', { name: '連結' })).toBeNull();
  });

  it('游標在連結上：連結鈕是按下的狀態（aria-pressed）', async () => {
    const user = userEvent.setup();
    render(<EditorHarness initial="[字字](https://example.com/)" />);
    const editor = await findEditor('說明');
    const link = screen.getByRole('button', { name: '連結' });
    expect(link).toHaveAttribute('aria-pressed', 'false');
    await user.click(editor);
    const anchor = editor.querySelector('a');
    const range = document.createRange();
    range.setStart(anchor?.firstChild as Node, 1);
    range.collapse(true);
    window.getSelection()?.removeAllRanges();
    window.getSelection()?.addRange(range);
    document.dispatchEvent(new Event('selectionchange'));
    await waitFor(() => {
      expect(link).toHaveAttribute('aria-pressed', 'true');
    });
  });
});
