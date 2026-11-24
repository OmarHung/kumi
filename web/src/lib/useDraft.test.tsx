import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { draftKey, readDraft, writeDraft } from './draftStorage';
import { useDraft } from './useDraft';

const KEY = draftKey('comment', 'AGY-3');
const notBlank = { isWorthKeeping: (value: string) => value.trim() !== '' };

describe('useDraft', () => {
  it('掛上時還原存著的內容，並標成「已還原」', () => {
    // Arrange
    writeDraft(KEY, '上次打的');

    // Act
    const { result } = renderHook(() => useDraft(KEY, notBlank));

    // Assert
    expect(result.current.restored).toBe('上次打的');
    expect(result.current.isRestored).toBe(true);
  });

  it('沒有草稿，或存著的不值得留下來，都當成沒有', () => {
    // Arrange
    writeDraft(KEY, '   ');

    // Act
    const { result } = renderHook(() => useDraft(KEY, notBlank));

    // Assert
    expect(result.current.restored).toBeNull();
    expect(result.current.isRestored).toBe(false);
  });

  it('save：值得留下來就存；不值得就拿掉，也不再說「已還原」', () => {
    // Arrange
    writeDraft(KEY, '上次打的');
    const { result } = renderHook(() => useDraft(KEY, notBlank));

    // Act
    act(() => {
      result.current.save('改過的');
    });

    // Assert
    expect(readDraft(KEY)).toBe('改過的');
    expect(result.current.isRestored).toBe(true);

    // Act
    act(() => {
      result.current.save('  ');
    });

    // Assert
    expect(readDraft(KEY)).toBeNull();
    expect(result.current.isRestored).toBe(false);
  });

  it('clear：拿掉草稿，不再說「已還原」；還原的內容照樣記得（呼叫端自己決定輸入框要放什麼）', () => {
    // Arrange
    writeDraft(KEY, '上次打的');
    const { result } = renderHook(() => useDraft(KEY, notBlank));

    // Act
    act(() => {
      result.current.clear();
    });

    // Assert
    expect(readDraft(KEY)).toBeNull();
    expect(result.current.isRestored).toBe(false);
    expect(result.current.restored).toBe('上次打的');
  });

  it('withBase：連同打草稿時的內容一起存，還原時拿得到', () => {
    // Arrange
    const key = draftKey('description', 'AGY-3');
    const { result } = renderHook(() => useDraft(key, { ...notBlank, withBase: true }));

    // Act
    act(() => {
      result.current.save('我的版本', '當時的說明');
    });
    const { result: again } = renderHook(() => useDraft(key, { ...notBlank, withBase: true }));

    // Assert
    expect(again.current.restored).toBe('我的版本');
    expect(again.current.restoredBase).toBe('當時的說明');
  });

  it('withBase：讀到舊的純文字草稿（沒有記下當時的內容）照樣還原，base 是 undefined', () => {
    // Arrange
    const key = draftKey('description', 'AGY-3');
    writeDraft(key, '{不是 JSON 的舊草稿');

    // Act
    const { result } = renderHook(() => useDraft(key, { ...notBlank, withBase: true }));

    // Assert
    expect(result.current.restored).toBe('{不是 JSON 的舊草稿');
    expect(result.current.restoredBase).toBeUndefined();
  });

  it('沒有 withBase 時照字面存（留言打的就算像 JSON 也不會被拆開）', () => {
    // Arrange
    writeDraft(KEY, '{"text":"像 JSON 的留言"}');

    // Act
    const { result } = renderHook(() => useDraft(KEY, notBlank));

    // Assert
    expect(result.current.restored).toBe('{"text":"像 JSON 的留言"}');
    expect(result.current.restoredBase).toBeUndefined();
  });
});
