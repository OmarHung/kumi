import { afterEach, describe, expect, it, vi } from 'vitest';
import { clearAllDrafts, draftKey, readDraft, removeDraft, writeDraft } from './draftStorage';

afterEach(() => {
  sessionStorage.clear();
});

describe('草稿的存放', () => {
  it('鍵名是 draft.<種類>.<項目編號>', () => {
    // Arrange & Act & Assert
    expect(draftKey('comment', 'AGY-3')).toBe('draft.comment.AGY-3');
    expect(draftKey('description', 'AGY-3')).toBe('draft.description.AGY-3');
    expect(draftKey('comment', 'AGY-3', 'c-1')).toBe('draft.comment.AGY-3.reply.c-1');
    expect(draftKey('comment-edit', 'AGY-3', 'c-1')).toBe('draft.comment-edit.AGY-3.reply.c-1');
  });

  it('存在這個分頁（sessionStorage），不放 localStorage；讀得回來，拿掉之後是 null', () => {
    // Arrange
    const key = draftKey('comment', 'AGY-3');

    // Act
    writeDraft(key, '寫到一半');

    // Assert
    expect(readDraft(key)).toBe('寫到一半');
    expect(sessionStorage.getItem('draft.comment.AGY-3')).toBe('寫到一半');
    expect(localStorage.getItem('draft.comment.AGY-3')).toBeNull();

    // Act
    removeDraft(key);

    // Assert
    expect(readDraft(key)).toBeNull();
  });

  it('沒有存過是 null', () => {
    // Arrange & Act & Assert
    expect(readDraft(draftKey('description', 'AGY-9'))).toBeNull();
  });

  it('clearAllDrafts 只清 draft. 開頭的鍵，別的照樣留著', () => {
    // Arrange
    writeDraft(draftKey('comment', 'AGY-3'), '留言');
    writeDraft(draftKey('description', 'SMK-7'), '說明');
    sessionStorage.setItem('other.key', '別的');

    // Act
    clearAllDrafts();

    // Assert
    expect(readDraft(draftKey('comment', 'AGY-3'))).toBeNull();
    expect(readDraft(draftKey('description', 'SMK-7'))).toBeNull();
    expect(sessionStorage.getItem('other.key')).toBe('別的');
  });

  it('讀寫失敗（不能用、容量滿了）當成沒有草稿，不丟例外', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'key').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError');
    });
    const key = draftKey('comment', 'AGY-3');

    // Act & Assert
    expect(() => {
      writeDraft(key, '寫不進去');
    }).not.toThrow();
    expect(readDraft(key)).toBeNull();
    expect(() => {
      removeDraft(key);
    }).not.toThrow();
    expect(() => {
      clearAllDrafts();
    }).not.toThrow();
  });

  it('沒有 sessionStorage 的環境也當成沒有草稿', () => {
    // Arrange
    vi.stubGlobal('sessionStorage', undefined);
    const key = draftKey('comment', 'AGY-3');

    // Act & Assert
    expect(() => {
      writeDraft(key, '寫不進去');
      clearAllDrafts();
    }).not.toThrow();
    expect(readDraft(key)).toBeNull();
    vi.unstubAllGlobals();
  });
});
