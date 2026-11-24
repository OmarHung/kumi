import { describe, expect, it } from 'vitest';
import type { FieldDefinition } from '../api/blueprint';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import {
  anyTypeHasParents,
  fieldDefinition,
  fieldLabel,
  formatFieldValue,
  safeHttpUrl,
  typeAllowsParent,
  typeHasField,
} from './fields';

const members = [{ personId: 'p-1', displayName: '範例使用者', role: 'admin' }];

function definition(key: string): FieldDefinition {
  const found = fieldDefinition(simpleTasksBlueprint, key);
  if (found === undefined) {
    throw new Error(`測試資料少了欄位 ${key}`);
  }
  return found;
}

describe('欄位適不適用', () => {
  it('依類型的 fields 判斷', () => {
    // Arrange / Act / Assert
    expect(typeHasField(agencyBlueprint, 'change_request', 'impact')).toBe(true);
    expect(typeHasField(agencyBlueprint, 'requirement', 'impact')).toBe(false);
    expect(typeHasField(agencyBlueprint, 'nope', 'impact')).toBe(false);
  });

  it('只有設了 parents 的類型可以有上層項目', () => {
    // Arrange / Act / Assert
    expect(typeAllowsParent(agencyBlueprint, 'task')).toBe(true);
    expect(typeAllowsParent(agencyBlueprint, 'requirement')).toBe(false);
    expect(anyTypeHasParents(agencyBlueprint)).toBe(true);
    expect(anyTypeHasParents(simpleTasksBlueprint)).toBe(false);
  });
});

describe('fieldLabel', () => {
  it('核心欄位用中文、自訂欄位用藍圖的名稱、都不是時用原本的 key', () => {
    // Arrange / Act / Assert
    expect(fieldLabel(agencyBlueprint, 'due_date')).toBe('到期日');
    expect(fieldLabel(agencyBlueprint, 'client_signoff_date')).toBe('客戶驗收日');
    expect(fieldLabel(agencyBlueprint, 'mystery')).toBe('mystery');
  });
});

describe('formatFieldValue', () => {
  it('每種型別轉成看得懂的文字', () => {
    // Arrange / Act / Assert
    expect(formatFieldValue(definition('category'), 'bug', members)).toBe('錯誤');
    expect(formatFieldValue(definition('tags'), ['frontend', 'docs'], members)).toBe('前端、文件');
    expect(formatFieldValue(definition('review_date'), '2026-11-30', members)).toBe('2026/11/30');
    expect(formatFieldValue(definition('story_points'), 1234.5, members)).toBe('1,234.5');
    expect(formatFieldValue(definition('budget'), 1500, members)).toMatch(/1,500/);
    expect(formatFieldValue(definition('blocked'), true, members)).toBe('是');
    expect(formatFieldValue(definition('reviewer'), 'p-1', members)).toBe('範例使用者');
    expect(formatFieldValue(definition('summary'), '摘要文字', members)).toBe('摘要文字');
  });

  it('沒有值、選項或成員找不到時也有說法', () => {
    // Arrange / Act / Assert
    expect(formatFieldValue(definition('summary'), undefined, members)).toBe('');
    expect(formatFieldValue(definition('blocked'), undefined, members)).toBe('否');
    expect(formatFieldValue(definition('category'), 'gone', members)).toBe('gone');
    expect(formatFieldValue(definition('reviewer'), 'p-404', members)).toBe('不是成員的人');
  });

  it('幣別代碼不合法時退回數字加代碼', () => {
    // Arrange
    const odd: FieldDefinition = { key: 'x', name: 'x', type: 'currency', currency: 'NOT_A_CODE' };

    // Act / Assert
    expect(formatFieldValue(odd, 12, members)).toBe('12 NOT_A_CODE');
  });
});

describe('safeHttpUrl', () => {
  it('只接受 http 與 https 的完整網址', () => {
    // Arrange / Act / Assert
    expect(safeHttpUrl('https://example.com/a')).toBe('https://example.com/a');
    expect(safeHttpUrl('javascript:alert(1)')).toBeUndefined();
    expect(safeHttpUrl('not a url')).toBeUndefined();
  });
});
