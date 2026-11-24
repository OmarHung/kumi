import { describe, expect, it } from 'vitest';
import { agencyBlueprint } from '../test/blueprints';
import {
  convertCheckbox,
  convertDate,
  convertItemKey,
  convertMember,
  convertMultiLine,
  convertMultiOptions,
  convertNamed,
  convertNumber,
  convertOption,
  convertPriority,
  convertSingleLine,
  convertState,
  convertType,
  type ImportMember,
} from './importValues';

const OPTIONS = [
  { key: 'frontend', name: '前端' },
  { key: 'backend', name: '後端' },
];

const MEMBERS: readonly ImportMember[] = [
  { personId: 'p1', displayName: '王小明', email: 'ming@example.com' },
  { personId: 'p2', displayName: '李小華', email: null },
];

function valueOf<T>(result: { ok: boolean; value?: T; message?: string }): T | string | undefined {
  return result.ok ? result.value : result.message;
}

describe('convertSingleLine 與 convertMultiLine', () => {
  it('標題前後的空白去掉', () => {
    expect(valueOf(convertSingleLine('  登入頁  '))).toBe('登入頁');
  });

  it('一行的文字不能有換行或 NUL', () => {
    expect(convertSingleLine('甲\n乙').ok).toBe(false);
    expect(convertSingleLine('甲\u0000乙').ok).toBe(false);
  });

  it('說明可以有換行，CRLF 統一成 LF，NUL 不收', () => {
    expect(valueOf(convertMultiLine('第一行\r\n第二行'))).toBe('第一行\n第二行');
    expect(convertMultiLine('壞\u0000字').ok).toBe(false);
  });
});

describe('convertNumber', () => {
  it.each([
    ['1,234', 1234],
    ['NT$ 1,500.5', 1500.5],
    ['$20', 20],
    ['3000 元', 3000],
    ['-7', -7],
    ['１２', 12],
    ['1,234.5', 1234.5],
    ['-1,000', -1000],
  ])('「%s」轉成 %d', (raw, expected) => {
    expect(valueOf(convertNumber(raw))).toBe(expected);
  });

  it('不是數字時說明哪一個值有問題', () => {
    expect(valueOf(convertNumber('很多'))).toBe('「很多」不是數字');
    expect(convertNumber('1e5').ok).toBe(false);
  });

  it('逗號只當千分位：3,5 與 12,34 不是數字，不會悄悄變成 35', () => {
    expect(convertNumber('3,5').ok).toBe(false);
    expect(convertNumber('12,34').ok).toBe(false);
    expect(convertNumber('1,2345').ok).toBe(false);
  });
});

describe('convertDate', () => {
  it.each([
    ['2026-11-05', '2026-11-05'],
    ['2026/11/05', '2026-11-05'],
    ['2026/1/5', '2026-01-05'],
  ])('「%s」轉成 %s', (raw, expected) => {
    expect(valueOf(convertDate(raw))).toBe(expected);
  });

  it('沒有這一天的日期與其他寫法都不收', () => {
    expect(valueOf(convertDate('2026/02/30'))).toBe('「2026/02/30」沒有這一天');
    expect(valueOf(convertDate('11/5/2026'))).toContain('不是日期');
  });
});

describe('convertPriority', () => {
  it.each([
    ['未設定', 'none'],
    ['低', 'low'],
    ['中', 'medium'],
    ['高', 'high'],
    ['緊急', 'urgent'],
    ['URGENT', 'urgent'],
    [' Medium ', 'medium'],
  ])('「%s」是 %s', (raw, expected) => {
    expect(valueOf(convertPriority(raw))).toBe(expected);
  });

  it('不認得的優先度列出可以用的', () => {
    expect(valueOf(convertPriority('超高'))).toContain('未設定、低、中、高、緊急');
  });
});

describe('convertCheckbox', () => {
  it.each(['true', 'TRUE', '是', '1', 'v', '✓'])('「%s」是勾選', (raw) => {
    expect(valueOf(convertCheckbox(raw))).toBe(true);
  });

  it.each(['false', '否', '0'])('「%s」是沒勾', (raw) => {
    expect(valueOf(convertCheckbox(raw))).toBe(false);
  });

  it('其他的值不收', () => {
    expect(convertCheckbox('也許').ok).toBe(false);
  });
});

describe('convertOption 與 convertMultiOptions', () => {
  it('單選可以寫選項的名稱或 key', () => {
    expect(valueOf(convertOption('前端', OPTIONS))).toBe('frontend');
    expect(valueOf(convertOption('Backend', OPTIONS))).toBe('backend');
  });

  it('找不到的選項說明是哪一個', () => {
    expect(valueOf(convertOption('設計', OPTIONS))).toBe('「設計」不是這個欄位的選項');
  });

  it('多選用逗號、頓號、分號分開，重複的只算一次', () => {
    expect(valueOf(convertMultiOptions('前端、backend; 前端，', OPTIONS))).toEqual(['frontend', 'backend']);
  });

  it('多選裡有一項找不到就整格有問題', () => {
    expect(valueOf(convertMultiOptions('前端,設計', OPTIONS))).toBe('「設計」不是這個欄位的選項');
  });
});

describe('convertMember', () => {
  it('用名稱或 Email（不分大小寫）找成員', () => {
    expect(valueOf(convertMember('王小明', MEMBERS))).toBe('p1');
    expect(valueOf(convertMember('Ming@Example.com', MEMBERS))).toBe('p1');
  });

  it('找不到時說明是誰', () => {
    expect(valueOf(convertMember('張三', MEMBERS))).toBe('找不到「張三」這個成員');
  });

  it('同名的兩個人要改用 Email，用 Email 就找得到', () => {
    // Arrange
    const twins: readonly ImportMember[] = [
      { personId: 'a', displayName: '王小明', email: 'a@example.com' },
      { personId: 'b', displayName: '王小明', email: 'b@example.com' },
    ];

    // Act & Assert
    expect(valueOf(convertMember('王小明', twins))).toBe('有 2 位成員叫「王小明」，請改用 Email');
    expect(valueOf(convertMember('b@example.com', twins))).toBe('b');
  });
});

describe('convertNamed、convertItemKey', () => {
  it('週期用名稱找編號', () => {
    expect(valueOf(convertNamed('衝刺 1', '週期', [{ id: 'c1', name: '衝刺 1' }]))).toBe('c1');
    expect(valueOf(convertNamed('衝刺 9', '週期', []))).toBe('找不到「衝刺 9」這個週期');
  });

  it('項目編號統一成大寫，寫法不對的不收', () => {
    expect(valueOf(convertItemKey(' agy-12 '))).toBe('AGY-12');
    expect(convertItemKey('12').ok).toBe(false);
    expect(convertItemKey('AGY-0').ok).toBe(false);
  });
});

describe('convertType 與 convertState', () => {
  it('類型用名稱或 key', () => {
    expect(valueOf(convertType('需求', agencyBlueprint))).toBe('requirement');
    expect(valueOf(convertType('Task', agencyBlueprint))).toBe('task');
    expect(valueOf(convertType('史詩', agencyBlueprint))).toBe('找不到「史詩」這個類型');
  });

  it('狀態要在那個類型的流程裡', () => {
    expect(valueOf(convertState('開發', agencyBlueprint, 'requirement'))).toBe('developing');
    expect(valueOf(convertState('進行中', agencyBlueprint, 'requirement'))).toBe('「進行中」不是「需求」流程裡的狀態');
  });
});
