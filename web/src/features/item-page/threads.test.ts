import { describe, expect, it } from 'vitest';
import { attachmentAdded, commented, created } from '../../test/activity';
import { buildThreads } from './threads';

describe('buildThreads', () => {
  it('回覆收在最上層留言底下，依時間排；回覆不另外成為一筆', () => {
    // Arrange
    const first = commented('第一則');
    const replyA = commented('回覆一', { replyToId: first.id });
    const second = commented('第二則');
    const replyB = commented('回覆二', { replyToId: first.id });

    // Act
    const threads = buildThreads([first, replyA, second, replyB]);

    // Assert
    expect(threads.map((thread) => thread.activity)).toEqual([first, second]);
    expect(threads[0]?.replies).toEqual([replyA, replyB]);
    expect(threads[1]?.replies).toEqual([]);
  });

  it('找不到它回覆的那一則時，當成一般留言放在時間軸上並標記', () => {
    // Arrange
    const start = created();
    const lost = commented('孤兒', { replyToId: '0190a000-0000-7000-8000-ffffffffffff' });

    // Act
    const threads = buildThreads([start, lost]);

    // Assert
    expect(threads.map((thread) => thread.activity)).toEqual([start, lost]);
    expect(threads.map((thread) => thread.isDetached)).toEqual([false, true]);
  });

  it('回覆的對象本身是回覆時（不應該發生），也當成找不到', () => {
    // Arrange
    const top = commented('最上層');
    const reply = commented('回覆', { replyToId: top.id });
    const nested = commented('回覆的回覆', { replyToId: reply.id });

    // Act
    const threads = buildThreads([top, reply, nested]);

    // Assert
    expect(threads.map((thread) => thread.activity)).toEqual([top, nested]);
    expect(threads[0]?.replies).toEqual([reply]);
    expect(threads[1]?.isDetached).toBe(true);
  });

  it('連續附加的檔案併成一筆（group 帶著每一筆），其他的活動各自一筆', () => {
    // Arrange
    const a = attachmentAdded('a.png', { at: '2026-11-13T08:00:00Z' });
    const b = attachmentAdded('b.png', { at: '2026-11-13T08:03:00Z' });
    const late = attachmentAdded('c.png', { at: '2026-11-13T09:00:00Z' });
    const note = commented('留言');

    // Act
    const threads = buildThreads([a, b, late, note]);

    // Assert
    expect(threads.map((thread) => thread.activity)).toEqual([a, late, note]);
    expect(threads.map((thread) => thread.group)).toEqual([[a, b], [late], [note]]);
  });
});
