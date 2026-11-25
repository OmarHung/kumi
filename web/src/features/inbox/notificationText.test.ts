import { describe, expect, it } from 'vitest';
import { notification } from '../../test/notifications';
import { notificationExcerpt, notificationSentence, plainExcerpt } from './notificationText';

describe('notificationSentence', () => {
  it.each([
    [{ kind: 'assigned' as const }, '指派給你'],
    [{ kind: 'mentioned' as const }, '提及你'],
    [{ kind: 'commented' as const }, '留言了'],
    [{ kind: 'replied' as const }, '回覆了你的留言'],
    [{ kind: 'state_changed' as const, fromStateName: '待辦', toStateName: '審查中' }, '把狀態從「待辦」改成「審查中」'],
  ])('%j', (overrides, sentence) => {
    expect(notificationSentence(notification(overrides))).toBe(sentence);
  });

  it('改狀態但狀態的名稱不見了（合約允許是 null）：只說改了狀態', () => {
    expect(notificationSentence(notification({ kind: 'state_changed', fromStateName: null, toStateName: null }))).toBe('改了狀態');
  });

  it('只知道改成什麼時：說改成什麼', () => {
    expect(notificationSentence(notification({ kind: 'state_changed', fromStateName: null, toStateName: '完成' }))).toBe('把狀態改成「完成」');
  });
});

describe('plainExcerpt', () => {
  it('提及的寫法換成「@名稱」，其他連結只留文字', () => {
    expect(plainExcerpt('請 @[王小明](mention:p-1) 看 [設計稿](https://example.com/a)')).toBe('請 @王小明 看 設計稿');
  });

  it('換行與連續的空白縮成一個空白，頭尾的空白拿掉', () => {
    expect(plainExcerpt('  第一行\n\n  第二行  ')).toBe('第一行 第二行');
  });

  it.each([
    ['提及寫到一半', '請 @[王小', '請'],
    ['提及的網址寫到一半', '請 @[王小明](mention:p-', '請'],
    ['連結的網址寫到一半', '看 [設計稿](https://exa', '看'],
    ['只有 @[', '@[', null],
  ])('摘要截斷在連結中間（%s）：沒寫完的部分去掉', (_name, excerpt, expected) => {
    expect(plainExcerpt(excerpt)).toBe(expected);
  });

  it('寫完的連結後面接著的字不受影響', () => {
    expect(plainExcerpt('[a](https://example.com) 後面 [b]')).toBe('a 後面 [b]');
  });

  it('沒有摘要是 null', () => {
    expect(plainExcerpt(null)).toBeNull();
    expect(plainExcerpt('   ')).toBeNull();
  });
});

describe('notificationExcerpt', () => {
  it('編輯過的留言在摘要後面加「（已編輯）」，沒編輯過的不加', () => {
    expect(notificationExcerpt(notification({ kind: 'commented', excerpt: '收到', edited: true }))).toBe('收到（已編輯）');
    expect(notificationExcerpt(notification({ kind: 'commented', excerpt: '收到', edited: false }))).toBe('收到');
  });

  it('沒有摘要時沒有內容，也不單獨出現「（已編輯）」', () => {
    expect(notificationExcerpt(notification({ excerpt: null, edited: true }))).toBeNull();
  });
});
