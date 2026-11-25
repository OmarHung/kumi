import { describe, expect, it } from 'vitest';
import { confirmationCodeOf } from '../../test/cliLogin';
import { CONFIRMATION_LENGTH, digitsOf, formatConfirmationCode } from './cliLoginText';

describe('確認碼的輸入', () => {
  it.each([
    ['E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM', '951123'],
    ['AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '941901'],
    ['abcdefghijklmnopqrstuvwxyz0123456789-_ABCDEFG', '342122'],
  ])('假後端的算法和設計的測試向量一致（%s）', (challenge, expected) => {
    // Arrange
    // Act
    const code = confirmationCodeOf(challenge);

    // Assert
    expect(code).toBe(expected);
  });

  it.each([
    ['951 123', '951123'],
    ['951-123', '951123'],
    ['  951123  ', '951123'],
    ['9a5b1c', '951'],
    ['1234567890', '123456'],
    ['９５１ １２３', '951123'],
    ['', ''],
  ])('「%s」只留前 6 個數字（%s）', (text, expected) => {
    // Arrange
    // Act
    const digits = digitsOf(text);

    // Assert
    expect(digits).toBe(expected);
    expect(digits.length).toBeLessThanOrEqual(CONFIRMATION_LENGTH);
  });

  it.each([
    ['', ''],
    ['95', '95'],
    ['951', '951'],
    ['9511', '951 1'],
    ['951123', '951 123'],
  ])('顯示成 ddd ddd：「%s」→「%s」', (digits, shown) => {
    // Arrange
    // Act
    const text = formatConfirmationCode(digits);

    // Assert
    expect(text).toBe(shown);
  });
});
