import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { Member } from '../../api/types';
import { findEditor, typeInEditor } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

function member(id: string, displayName: string): Member {
  return { personId: id, displayName } as Member;
}

const MEMBERS: readonly Member[] = [member('11111111-aaaa', '王小明'), member('22222222-bbbb', '李大華'), member('33333333-cccc', 'Alice Wang')];

async function setup(props: { onCancel?: () => void; isBusy?: boolean } = {}) {
  const user = userEvent.setup();
  const onValue = vi.fn();
  render(<EditorHarness mentionMembers={MEMBERS} onValue={onValue} {...props} />);
  const editor = await findEditor('說明');
  return { user, onValue, editor };
}

describe('編輯器裡的提及', () => {
  it('打 @ 列出成員，編輯器的 aria 指著清單，格式提示說可以提及', async () => {
    const { user, editor } = await setup();
    await typeInEditor(user, editor, '請 @');
    const list = await screen.findByRole('listbox', { name: '提及成員' });
    expect(screen.getAllByRole('option')).toHaveLength(3);
    expect(editor).toHaveAttribute('aria-controls', list.id);
    expect(editor).toHaveAttribute('aria-autocomplete', 'list');
    expect(editor.getAttribute('aria-activedescendant')).toBe(screen.getAllByRole('option')[0]?.id);
    expect(screen.getByText(/輸入 @ 提及成員/)).toBeInTheDocument();
  });

  it('打字篩選（不分大小寫），開頭符合的排前面', async () => {
    const { user, editor } = await setup();
    await typeInEditor(user, editor, '@wang');
    await waitFor(() => {
      const options = screen.getAllByRole('option');
      expect(options).toHaveLength(1);
      expect(options[0]).toHaveTextContent('Alice Wang');
    });
  });

  it('上下鍵移動反白（繞回另一端），Enter 選了插入 mention 節點加一個空白，清單關上', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '請 @');
    await screen.findByRole('listbox', { name: '提及成員' });
    await user.keyboard('{ArrowDown}');
    await waitFor(() => {
      expect(screen.getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true');
    });
    await user.keyboard('{ArrowUp}{ArrowUp}');
    await waitFor(() => {
      expect(screen.getAllByRole('option')[2]).toHaveAttribute('aria-selected', 'true');
    });
    await user.keyboard('{ArrowUp}{Enter}');
    expect(onValue).toHaveBeenLastCalledWith('請 @[李大華](mention:22222222-bbbb)');
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(editor.querySelector('[data-type="mention"]')).toHaveTextContent('@李大華');
  });

  it('點選項也插入', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '@');
    await user.click(await screen.findByRole('option', { name: /王小明/ }));
    expect(onValue).toHaveBeenLastCalledWith('@[王小明](mention:11111111-aaaa)');
  });

  it('Email 不觸發', async () => {
    const { user, editor } = await setup();
    await typeInEditor(user, editor, 'a@');
    expect(screen.queryByRole('listbox')).toBeNull();
    await user.keyboard('b');
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('Esc 先關清單，不取消編輯；同一個 @ 不再打開，再按 Esc 才取消', async () => {
    const onCancel = vi.fn();
    const { user, editor } = await setup({ onCancel });
    await typeInEditor(user, editor, '@王');
    await screen.findByRole('listbox', { name: '提及成員' });
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('listbox')).toBeNull();
    });
    expect(onCancel).not.toHaveBeenCalled();
    await user.keyboard('小');
    expect(screen.queryByRole('listbox')).toBeNull();
    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('沒有符合的成員時是一列不能選的說明，Enter 照常換段落', async () => {
    const { user, onValue, editor } = await setup();
    await typeInEditor(user, editor, '@zzz');
    expect(await screen.findByText('沒有符合的成員', { selector: 'li' })).toBeInTheDocument();
    await user.keyboard('{Enter}');
    expect(onValue).toHaveBeenLastCalledWith('@zzz');
    expect(editor.querySelectorAll('p').length).toBeGreaterThan(1);
  });

  it('沒給成員時沒有提及，格式提示也不提', async () => {
    const user = userEvent.setup();
    render(<EditorHarness />);
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '@');
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(screen.queryByText(/輸入 @ 提及成員/)).toBeNull();
  });

  it('一直掛著的 aria-live 說清單開了與符合的人數', async () => {
    const { user, editor } = await setup();
    const live = document.querySelector('[aria-live="polite"]');
    expect(live).toHaveTextContent('');
    await typeInEditor(user, editor, '@');
    await waitFor(() => {
      expect(live).toHaveTextContent('3 位成員可以提及，用上下鍵選擇，Enter 確定');
    });
  });

  it('已存的提及載入時是 mention 節點，存回去不變', async () => {
    const original = '請 @[王小明](mention:11111111-aaaa) 看';
    const { user, onValue, editor } = await setup();
    expect(onValue).not.toHaveBeenCalled();
    expect(editor).toBeInTheDocument();
    render(<EditorHarness mentionMembers={MEMBERS} initial={original} onValue={onValue} label="另一個" />);
    const other = await findEditor('另一個');
    expect(other.querySelector('[data-type="mention"]')).toHaveTextContent('@王小明');
    await user.click(other);
    expect(onValue).not.toHaveBeenCalled();
  });
});
