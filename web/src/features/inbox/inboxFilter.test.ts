import { describe, expect, it } from 'vitest';
import { NOTIFICATION_SEARCH_MAX_LENGTH } from '../../api/notificationTypes';
import { inboxFilterOf, inboxFilterSearch } from './inboxFilter';

describe('收件匣的條件（網址）', () => {
  it('什麼都沒有是預設：全部分類、沒有搜尋字、只看未讀', () => {
    // Arrange & Act
    const filter = inboxFilterOf(new URLSearchParams());

    // Assert
    expect(filter).toEqual({ category: 'all', q: '', unreadOnly: true });
  });

  it('讀 type、q、show；不認得的分類當成全部，搜尋字去掉頭尾空白', () => {
    // Arrange & Act
    const filter = inboxFilterOf(new URLSearchParams('type=assigned&q=%20登入%20&show=all'));
    const unknown = inboxFilterOf(new URLSearchParams('type=bogus&show=nope'));

    // Assert
    expect(filter).toEqual({ category: 'assigned', q: '登入', unreadOnly: false });
    expect(unknown).toEqual({ category: 'all', q: '', unreadOnly: true });
  });

  it('網址的搜尋字去掉控制字元、截到上限：分享來的壞網址不會讓清單一直載入失敗', () => {
    // Arrange
    const long = 'a'.repeat(NOTIFICATION_SEARCH_MAX_LENGTH + 20);

    // Act
    const control = inboxFilterOf(new URLSearchParams('q=%E7%99%BB%09%E5%85%A5%00'));
    const tooLong = inboxFilterOf(new URLSearchParams({ q: long }));

    // Assert
    expect(control.q).toBe('登入');
    expect(tooLong.q).toHaveLength(NOTIFICATION_SEARCH_MAX_LENGTH);
  });

  it('預設的條件不寫進網址，其他參數（開著的面板 item）留著', () => {
    // Arrange
    const params = new URLSearchParams('item=SMK-7&type=mentions&q=abc&show=all');

    // Act
    const search = inboxFilterSearch(params, { category: 'all', q: '', unreadOnly: true });

    // Assert
    expect(search).toBe('?item=SMK-7');
  });

  it('只換指定的條件，其他的照舊', () => {
    // Arrange
    const params = new URLSearchParams('type=mentions&q=abc');

    // Act & Assert
    expect(inboxFilterSearch(params, { unreadOnly: false })).toBe('?type=mentions&q=abc&show=all');
    expect(inboxFilterSearch(params, { category: 'assigned' })).toBe('?type=assigned&q=abc');
    expect(inboxFilterSearch(new URLSearchParams(), {})).toBe('');
  });
});
