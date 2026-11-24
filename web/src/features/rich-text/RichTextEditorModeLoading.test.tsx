import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { setEditorMode } from '../../lib/editorMode';
import { EditorHarness } from '../../test/richTextHarness';
import { findEditor } from '../../test/richText';

const loaded = vi.hoisted(() => ({ count: 0 }));

// 數 Tiptap 那一包被載入幾次：模組的工廠函式只在真的 import 時才跑。
vi.mock('./RichTextEditorContent', async (importOriginal) => {
  loaded.count += 1;
  return importOriginal();
});

describe('編輯方式與延後載入', () => {
  it('偏好是 Markdown 時不下載編輯器那一包；偏好是所見即所得時才載入', async () => {
    // Arrange
    setEditorMode('markdown');

    // Act
    render(<EditorHarness initial="原文" />);
    await screen.findByRole('textbox', { name: '說明' });
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Assert
    expect(loaded.count).toBe(0);
  });

  it('切回所見即所得才下載', async () => {
    // Arrange
    setEditorMode('rich');

    // Act
    render(<EditorHarness initial="原文" />);
    await findEditor('說明');

    // Assert
    expect(loaded.count).toBe(1);
  });
});
