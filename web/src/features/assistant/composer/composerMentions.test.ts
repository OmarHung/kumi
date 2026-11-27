import { describe, expect, it } from 'vitest';
import { applyMentions, plainMentionInsert } from './composerMentions';

const XIAO_MING = { personId: 'p-1', displayName: '小明' };
const JOHN = { personId: 'p-2', displayName: 'John Smith' };

describe('輸入框的提及', () => {
  it('選人時寫成 @名稱 加一個空白，游標在後面', () => {
    // Act
    const inserted = plainMentionInsert('請 @小', { start: 2, query: '小' }, { ...XIAO_MING, role: 'x' });

    // Assert
    expect(inserted).toEqual({ value: '請 @小明 ', caret: 6 });
  });

  it('送出時把還在的 @名稱 換成提及語法', () => {
    expect(applyMentions('請 @小明 看一下', [XIAO_MING])).toBe('請 @[小明](mention:p-1) 看一下');
  });

  it('使用者刪掉的不換', () => {
    expect(applyMentions('請小明看一下', [XIAO_MING])).toBe('請小明看一下');
  });

  it('名稱有空白也對得上', () => {
    expect(applyMentions('@John Smith 好', [JOHN])).toBe('@[John Smith](mention:p-2) 好');
  });

  it('同名的兩個人照選的順序對應', () => {
    // Act
    const result = applyMentions('@小明 和 @小明', [XIAO_MING, { personId: 'p-9', displayName: '小明' }]);

    // Assert
    expect(result).toBe('@[小明](mention:p-1) 和 @[小明](mention:p-9)');
  });

  it('@ 前面是英數字的是 Email，不換', () => {
    expect(applyMentions('寄到 a@小明', [XIAO_MING])).toBe('寄到 a@小明');
  });

  it('名稱裡會弄壞連結的字拿掉', () => {
    expect(applyMentions('@a[b] x', [{ personId: 'p-3', displayName: 'a[b]' }])).toBe('@[ab](mention:p-3) x');
  });

  it('名稱是另一個名稱的前綴時不換錯：@Ivani 不是 @Ivan', () => {
    // Arrange
    const ivan = { personId: 'p-1', displayName: 'Ivan' };
    const ivani = { personId: 'p-2', displayName: 'Ivani' };

    // Act
    const result = applyMentions('@Ivani 和 @Ivan', [ivan, ivani]);

    // Assert
    expect(result).toBe('@[Ivani](mention:p-2) 和 @[Ivan](mention:p-1)');
  });

  it('只選了短名稱時，文字裡較長的名稱延伸不算', () => {
    expect(applyMentions('@Ivani 好', [{ personId: 'p-1', displayName: 'Ivan' }])).toBe('@Ivani 好');
    expect(applyMentions('@Ivan，好', [{ personId: 'p-1', displayName: 'Ivan' }])).toBe('@[Ivan](mention:p-1)，好');
  });

  it('選人的順序和文字的順序不同也都換得到', () => {
    // Act
    const result = applyMentions('@小華 先，再 @小明', [XIAO_MING, { personId: 'p-5', displayName: '小華' }]);

    // Assert
    expect(result).toBe('@[小華](mention:p-5) 先，再 @[小明](mention:p-1)');
  });

  it('同一個人提及兩次，兩處都換', () => {
    expect(applyMentions('@小明 你好 @小明', [XIAO_MING, XIAO_MING])).toBe('@[小明](mention:p-1) 你好 @[小明](mention:p-1)');
    expect(applyMentions('@小明 你好 @小明', [XIAO_MING])).toBe('@[小明](mention:p-1) 你好 @[小明](mention:p-1)');
  });
});
