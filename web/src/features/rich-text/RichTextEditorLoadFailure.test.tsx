import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { EditorHarness } from '../../test/richTextHarness';

// 重新部署之後，舊分頁要的那一包檔案已經不在了：動態載入會失敗。
vi.mock('./RichTextEditorContent', () => {
  throw new Error('Failed to fetch dynamically imported module');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('RichTextEditor：延後載入失敗', () => {
  it('顯示「編輯器載入失敗」與「重新整理」，外框、標籤、提示、錯誤都還在，頁面不掉進錯誤頁', async () => {
    render(<EditorHarness initial="草稿的字" hint="快速鍵說明" error="寫不下" />);
    expect(await screen.findByText('編輯器載入失敗，重新整理頁面再試一次。')).toBeInTheDocument();
    expect(screen.getByText('說明')).toBeInTheDocument();
    expect(screen.getByText('快速鍵說明')).toBeInTheDocument();
    expect(screen.getAllByRole('alert')).toHaveLength(2);
    expect(screen.getByText('寫不下')).toBeInTheDocument();
  });

  it('「重新整理」重新載入頁面', async () => {
    const user = userEvent.setup();
    const reload = vi.fn();
    vi.stubGlobal('location', { reload });
    render(<EditorHarness />);
    await user.click(await screen.findByRole('button', { name: '重新整理' }));
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
