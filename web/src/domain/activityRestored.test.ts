import { describe, expect, it } from 'vitest';
import { deleted, restored } from '../test/activity';
import { agencyBlueprint } from '../test/blueprints';
import { memberAsEditor, ownerAsAdmin } from '../test/fixtures';
import { activityAnnouncement, changeText, restoreDescription } from './activity';

// 項目現在的類型是需求（交付流程）。
const context = { blueprint: agencyBlueprint, members: [ownerAsAdmin, memberAsEditor], typeKey: 'requirement' };

function sentence(activity: ReturnType<typeof restored>): string {
  return changeText(restoreDescription(activity, context));
}

describe('時間軸：復原刪除的項目', () => {
  it('沒有搬移時只說復原了', () => {
    // Arrange & Act & Assert
    expect(sentence(restored())).toBe('復原了這個項目');
  });

  it('狀態不在流程裡被搬到第一個狀態時，接著說前後的狀態', () => {
    // Arrange
    const activity = restored([], { fromState: 'review', toState: 'confirming' });

    // Act & Assert
    expect(sentence(activity)).toBe('復原了這個項目，狀態從「review」搬到「需求確認」');
  });

  it('封存的欄位說出名稱與原本的值，長文字只說封存了', () => {
    // Arrange
    const activity = restored([
      { field: 'quote_amount', from: 1200, to: null },
      { field: 'change_reason', from: null, to: null },
    ]);

    // Act & Assert
    expect(sentence(activity)).toBe('復原了這個項目；「報價金額」（原本是 $1,200）、「變更原因」的值封存起來');
  });

  it('上層不能再掛上去時說拿掉了哪一個上層', () => {
    // Arrange
    const activity = restored([{ field: 'parent', from: 'AGY-3', to: null }]);

    // Act & Assert
    expect(sentence(activity)).toBe('復原了這個項目；拿掉上層 AGY-3');
  });

  it('搬移、封存與拿掉上層同時發生時依序說完', () => {
    // Arrange
    const activity = restored(
      [
        { field: 'quote_amount', from: 1200, to: null },
        { field: 'parent', from: 'AGY-3', to: null },
      ],
      { fromState: 'review', toState: 'confirming' },
    );

    // Act & Assert
    expect(sentence(activity)).toBe(
      '復原了這個項目，狀態從「review」搬到「需求確認」；「報價金額」（原本是 $1,200）的值封存起來；拿掉上層 AGY-3',
    );
  });

  it('值另外標出來，畫面上比較好掃讀', () => {
    // Arrange
    const activity = restored([
      { field: 'quote_amount', from: 1200, to: null },
      { field: 'parent', from: 'AGY-3', to: null },
    ]);

    // Act
    const { segments } = restoreDescription(activity, context);

    // Assert
    expect(segments.filter((segment) => segment.kind === 'value').map((segment) => segment.text)).toEqual(['$1,200', 'AGY-3']);
  });

  it('給螢幕報讀器的摘要是同一句話', () => {
    // Arrange
    const activity = restored([{ field: 'parent', from: 'AGY-3', to: null }], { fromState: 'review', toState: 'confirming' });

    // Act & Assert
    expect(activityAnnouncement(activity, context)).toBe(
      '範例使用者 復原了這個項目，狀態從「review」搬到「需求確認」；拿掉上層 AGY-3',
    );
  });

  it('刪除的紀錄的摘要說刪除了這個項目', () => {
    // Arrange & Act & Assert
    expect(activityAnnouncement(deleted(), context)).toBe('範例使用者 刪除了這個項目');
  });
});
