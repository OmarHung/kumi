import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EditorHarness } from '../../test/richTextHarness';

// 永遠下載不完：看下載好之前的樣子。
vi.mock('./RichTextEditorContent', () => new Promise<never>(() => undefined));

describe('RichTextEditor：下載好之前', () => {
  it('顯示同樣的外框與原文的純文字（唯讀），還沒有可以編輯的元素', () => {
    render(<EditorHarness initial={'原文\n第二行'} hint="快速鍵說明" />);
    expect(screen.getByText('說明')).toBeInTheDocument();
    expect(screen.getByText('快速鍵說明')).toBeInTheDocument();
    expect(screen.getByText(/原文/).textContent).toBe('原文\n第二行');
    expect(screen.queryByRole('textbox')).toBeNull();
    expect(screen.queryByRole('toolbar')).toBeNull();
  });
});
