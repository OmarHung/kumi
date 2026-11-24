import { describe, expect, it } from 'vitest';
import { created, migrated, transitioned, typeChanged, updated } from '../test/activity';
import { agencyBlueprint } from '../test/blueprints';
import { memberAsEditor, ownerAsAdmin } from '../test/fixtures';
import { activityAnnouncement, changeText, typeChangeDescription, typesAtEntries } from './activity';

// 項目現在的類型是任務：狀態的名稱要看前後兩個類型各自的流程，不是看現在的類型。
const context = { blueprint: agencyBlueprint, members: [ownerAsAdmin, memberAsEditor], typeKey: 'task' };

function sentence(activity: ReturnType<typeof typeChanged>): string {
  return changeText(typeChangeDescription(activity, context));
}

describe('時間軸：改類型', () => {
  it('說出前後的類型、狀態怎麼搬（各自用自己流程的名稱）與封存的欄位值', () => {
    // Arrange
    const activity = typeChanged(
      'requirement',
      'task',
      [
        { field: 'quote_amount', from: 1200, to: null },
        { field: 'client_signoff_date', from: '2026-11-20', to: null },
      ],
      { fromState: 'developing', toState: 'doing' },
    );

    // Act & Assert
    expect(sentence(activity)).toBe(
      '把類型從「需求」改成「任務」，狀態從「開發」搬到「進行中」；「報價金額」（原本是 $1,200）、「客戶驗收日」（原本是 2026/11/20）的值封存起來',
    );
  });

  it('狀態沒搬、也沒有封存的欄位時只說類型', () => {
    // Arrange & Act & Assert
    expect(sentence(typeChanged('requirement', 'change_request'))).toBe('把類型從「需求」改成「需求變更單」');
  });

  it('長文字的值不記內容，只說它封存了', () => {
    // Arrange
    const activity = typeChanged('change_request', 'requirement', [{ field: 'change_reason', from: null, to: null }]);

    // Act & Assert
    expect(sentence(activity)).toBe('把類型從「需求變更單」改成「需求」；「變更原因」的值封存起來');
  });

  it('被刪除的類型與它流程裡的狀態只剩 key', () => {
    // Arrange
    const activity = typeChanged('legacy', 'task', [], { fromState: 'review', toState: 'todo' });

    // Act & Assert
    expect(sentence(activity)).toBe('把類型從「legacy」改成「任務」，狀態從「review」搬到「待辦」');
  });

  it('值另外標出來，畫面上比較好掃讀', () => {
    // Arrange
    const activity = typeChanged('requirement', 'task', [{ field: 'quote_amount', from: 1200, to: null }]);

    // Act
    const { segments } = typeChangeDescription(activity, context);

    // Assert
    expect(segments.filter((segment) => segment.kind === 'value').map((segment) => segment.text)).toEqual(['$1,200']);
  });

  it('給螢幕報讀器的摘要是同一句話', () => {
    // Arrange
    const activity = typeChanged('requirement', 'task', [], { fromState: 'developing', toState: 'doing' });

    // Act & Assert
    expect(activityAnnouncement(activity, context)).toBe('範例使用者 把類型從「需求」改成「任務」，狀態從「開發」搬到「進行中」');
  });

  it('刪除的紀錄說刪除了這個項目', () => {
    // Arrange
    const deleted = { ...typeChanged('task', 'task'), kind: 'deleted' as const, changes: [] };

    // Act & Assert
    expect(activityAnnouncement(deleted, context)).toBe('範例使用者 刪除了這個項目');
  });
});

describe('時間軸：每一筆當時的類型', () => {
  it('從已載入的改類型往回推：在某筆改類型之前用它的 from，之後用它的 to', () => {
    // Arrange
    const entries = [
      created(),
      transitioned('confirming', 'quoting'),
      typeChanged('requirement', 'task', [], { fromState: 'quoting', toState: 'todo' }),
      transitioned('todo', 'doing'),
      typeChanged('task', 'change_request', [], { fromState: 'doing', toState: 'confirming' }),
      updated([{ field: 'title', from: '舊', to: '新' }]),
    ];

    // Act
    const types = typesAtEntries(entries, 'change_request');

    // Assert
    expect(entries.map((entry) => types.get(entry.id))).toEqual([
      'requirement',
      'requirement',
      'requirement',
      'task',
      'task',
      'change_request',
    ]);
  });

  it('還沒載入後面的頁時，最後一筆改類型之後的用它的 to，不用項目現在的類型', () => {
    // Arrange：之後還有改類型（在還沒載入的頁），項目現在已經是需求變更單。
    const entries = [created(), typeChanged('requirement', 'task'), migrated(4, { fromState: 'review', toState: 'doing' })];

    // Act
    const types = typesAtEntries(entries, 'change_request');

    // Assert
    expect(entries.map((entry) => types.get(entry.id))).toEqual(['requirement', 'requirement', 'task']);
  });

  it('沒有載入任何改類型時，每一筆都用項目現在的類型', () => {
    // Arrange
    const entries = [created(), transitioned('todo', 'doing')];

    // Act
    const types = typesAtEntries(entries, 'task');

    // Assert
    expect(entries.map((entry) => types.get(entry.id))).toEqual(['task', 'task']);
  });
});
