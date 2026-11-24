import { describe, expect, it } from 'vitest';
import { ownerRef } from '../../test/activity';
import { notification } from '../../test/notifications';
import { layoutToasts, toastAnnouncement, toastExcerpt, toastHeadline } from './toastText';

const mention = () =>
  notification({
    kind: 'mentioned',
    itemKey: 'MNT-12',
    itemTitle: '修正登入頁',
    excerpt: '請看 @[王小明](mention:abc) 的[設計稿](https://example.com/a)',
  });

describe('提示的文字', () => {
  it('第一行是誰、做了什麼', () => {
    expect(toastHeadline(mention())).toBe(`${ownerRef.displayName} 提及你`);
  });

  it('完整時報讀軟體唸誰、做了什麼、哪個項目與內容；精簡不唸內容', () => {
    // Arrange
    const entry = mention();

    // Act & Assert
    expect(toastAnnouncement(entry, 'full')).toBe(`新通知：${ownerRef.displayName} 提及你，MNT-12 修正登入頁：請看 @王小明 的設計稿`);
    expect(toastAnnouncement(entry, 'compact')).toBe(`新通知：${ownerRef.displayName} 提及你，MNT-12 修正登入頁`);
  });

  it('沒有內容的通知（指派）完整時也沒有第三段', () => {
    expect(toastExcerpt(notification({ kind: 'assigned', excerpt: null }), 'full')).toBeNull();
    expect(toastExcerpt(mention(), 'compact')).toBeNull();
  });
});

describe('最多三個提示', () => {
  const some = (count: number) => Array.from({ length: count }, () => notification());

  it('三則以內全部畫出來', () => {
    // Arrange
    const entries = some(3);

    // Act
    const { visible, folded } = layoutToasts(entries);

    // Assert
    expect(visible).toEqual(entries);
    expect(folded).toEqual([]);
  });

  it('超過就留最新的兩則，其餘併成一則「還有 N 則」', () => {
    // Arrange
    const entries = some(5);

    // Act
    const { visible, folded } = layoutToasts(entries);

    // Assert
    expect(visible).toEqual(entries.slice(3));
    expect(folded).toEqual(entries.slice(0, 3));
  });
});
