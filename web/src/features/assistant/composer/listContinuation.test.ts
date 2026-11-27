import { describe, expect, it } from 'vitest';
import { continueList } from './listContinuation';

function at(value: string, caret = value.length) {
  return continueList(value, caret, caret);
}

describe('清單接續', () => {
  it.each([
    ['1. 買牛奶', '1. 買牛奶\n2. '],
    ['9) 買牛奶', '9) 買牛奶\n10) '],
    ['1、買牛奶', '1、買牛奶\n2、'],
    ['- 買牛奶', '- 買牛奶\n- '],
    ['* 買牛奶', '* 買牛奶\n* '],
    ['• 買牛奶', '• 買牛奶\n• '],
    ['  - 買牛奶', '  - 買牛奶\n  - '],
    ['前言\n3. 買牛奶', '前言\n3. 買牛奶\n4. '],
  ])('%j 換行接續成 %j', (value, expected) => {
    // Act
    const result = at(value);

    // Assert
    expect(result?.value).toBe(expected);
    expect(result?.caret).toBe(expected.length);
  });

  it('游標在行中間：後面的字帶到新的一行', () => {
    // Act
    const result = at('- 買牛奶和雞蛋', 5);

    // Assert
    expect(result?.value).toBe('- 買牛奶\n- 和雞蛋');
    expect(result?.caret).toBe(8);
  });

  it('只有記號沒有文字：拿掉這一行的記號、結束清單', () => {
    // Act
    const result = at('- 買牛奶\n- ');

    // Assert
    expect(result?.value).toBe('- 買牛奶\n');
    expect(result?.caret).toBe(6);
  });

  it('編號的空項目也一樣結束清單', () => {
    expect(at('1. a\n2. ')?.value).toBe('1. a\n');
  });

  it.each(['一般的文字', '3.14 是圓周率', '-沒有空白', '2)沒有空白', '', '1.'])('%j 不是清單：照一般換行', (value) => {
    expect(at(value)).toBeNull();
  });

  it('有選取一段字時不處理（照一般換行取代選取）', () => {
    expect(continueList('- 買牛奶', 2, 4)).toBeNull();
  });
});
