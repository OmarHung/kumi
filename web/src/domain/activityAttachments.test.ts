import { describe, expect, it } from 'vitest';
import { attachmentAdded, attachmentRemoved, commented, memberRef } from '../test/activity';
import { smkDetail } from '../test/fixtures';
import {
  activityAnnouncement,
  attachmentDescription,
  attachmentGroupAnnouncement,
  attachmentGroupDescription,
  changeText,
  groupAttachmentRuns,
} from './activity';

const context = { blueprint: smkDetail().blueprint, members: [], typeKey: 'task' };

describe('活動：附件', () => {
  it('附加了檔案：「附加了檔案「規格.pdf」」，檔名另外標成值', () => {
    // Arrange
    const activity = attachmentAdded('規格.pdf');

    // Act
    const description = attachmentDescription(activity);

    // Assert
    expect(changeText(description)).toBe('附加了檔案「規格.pdf」');
    expect(description.segments.filter((segment) => segment.kind === 'value').map((segment) => segment.text)).toEqual(['規格.pdf']);
  });

  it('刪除了附件：檔名取自 from', () => {
    // Arrange & Act & Assert
    expect(changeText(attachmentDescription(attachmentRemoved('規格.pdf')))).toBe('刪除了附件「規格.pdf」');
  });

  it('紀錄沒帶檔名時只說前半句，不編名稱', () => {
    // Arrange
    const added = { ...attachmentAdded('x'), changes: [] };
    const removed = { ...attachmentRemoved('x'), changes: [] };

    // Act & Assert
    expect(changeText(attachmentDescription(added))).toBe('附加了檔案');
    expect(changeText(attachmentDescription(removed))).toBe('刪除了附件');
  });

  it('報讀的摘要是人加上同一句話；太長的檔名截短', () => {
    // Arrange
    const long = `${'長'.repeat(100)}.pdf`;

    // Act & Assert
    expect(activityAnnouncement(attachmentAdded('規格.pdf'), context)).toBe('範例使用者 附加了檔案「規格.pdf」');
    expect(activityAnnouncement(attachmentRemoved(long), context)).toMatch(/…$/);
  });
});

const at = (minutes: number) => ({ at: new Date(Date.UTC(2026, 10, 13, 8, minutes)).toISOString() });

describe('活動：連續的附件合併成一行', () => {
  it('同一個人、連續、相隔 5 分鐘內的 attachment_added 合成一組；超過 5 分鐘、換人、中間夾了別的活動就分開', () => {
    // Arrange
    const a = attachmentAdded('a.png', at(0));
    const b = attachmentAdded('b.png', at(5));
    const c = attachmentAdded('c.png', at(11));
    const other = attachmentAdded('d.png', { ...at(12), actor: memberRef });
    const e = attachmentAdded('e.png', at(13));
    const comment = commented('中間的留言', at(14));
    const f = attachmentAdded('f.png', at(15));

    // Act
    const groups = groupAttachmentRuns([a, b, c, other, e, comment, f]);

    // Assert
    expect(groups).toEqual([[a, b], [c], [other], [e], [comment], [f]]);
  });

  it('刪除的附件不合併，照舊一筆一行', () => {
    // Arrange
    const removed = [attachmentRemoved('a.png', at(0)), attachmentRemoved('b.png', at(1))];

    // Act & Assert
    expect(groupAttachmentRuns(removed)).toEqual([[removed[0]], [removed[1]]]);
  });

  it('一組的說法：「附加了 3 個檔案：a.png、b.png、c.png」，檔名另外標成值', () => {
    // Arrange
    const group = ['a.png', 'b.png', 'c.png'].map((name, index) => attachmentAdded(name, at(index)));

    // Act
    const description = attachmentGroupDescription(group);

    // Assert
    expect(changeText(description)).toBe('附加了 3 個檔案：a.png、b.png、c.png');
    expect(description.segments.filter((segment) => segment.kind === 'value').map((segment) => segment.text)).toEqual(['a.png', 'b.png', 'c.png']);
  });

  it('太多時只列前 3 個，後面接「和其他 N 個」', () => {
    // Arrange
    const group = ['a', 'b', 'c', 'd', 'e'].map((name, index) => attachmentAdded(`${name}.png`, at(index)));

    // Act & Assert
    expect(changeText(attachmentGroupDescription(group))).toBe('附加了 5 個檔案：a.png、b.png、c.png 和其他 2 個');
  });

  it('報讀的摘要是人加上同一句話', () => {
    // Arrange
    const group = [attachmentAdded('a.png', at(0)), attachmentAdded('b.png', at(1))];

    // Act & Assert
    expect(attachmentGroupAnnouncement(group)).toBe('範例使用者 附加了 2 個檔案：a.png、b.png');
  });
});
