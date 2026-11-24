import { describe, expect, it } from 'vitest';
import { initialsOf } from './initials';

describe('頭像的字', () => {
  it('中文取第一個字', () => {
    // Arrange
    const name = '範例使用者';

    // Act
    const initials = initialsOf(name);

    // Assert
    expect(initials).toBe('範');
  });

  it('英文取前兩個單字的字首，轉成大寫', () => {
    // Arrange
    const names = ['Example User', 'example', '  mary   jane watson '];

    // Act
    const initials = names.map(initialsOf);

    // Assert
    expect(initials).toEqual(['EU', 'E', 'MJ']);
  });

  it('英文後面接中文時只取英文的字首', () => {
    // Arrange
    const name = 'QA 主管';

    // Act
    const initials = initialsOf(name);

    // Assert
    expect(initials).toBe('Q');
  });

  it('不會把罕用字（兩個 UTF-16 單位）切成一半', () => {
    // Arrange
    const name = '𠮷野家';

    // Act
    const initials = initialsOf(name);

    // Assert
    expect(initials).toBe('𠮷');
  });

  it('名稱是空白時用問號', () => {
    // Arrange
    const name = '   ';

    // Act
    const initials = initialsOf(name);

    // Assert
    expect(initials).toBe('?');
  });
});
