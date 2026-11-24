import { describe, expect, it } from 'vitest';
import { normalizeProjectSearchText, PROJECT_SEARCH_MAX_LENGTH } from './projectSearchText';

describe('normalizeProjectSearchText', () => {
  it('去掉頭尾空白，中間的空白照留', () => {
    expect(normalizeProjectSearchText('  web01 example  ')).toBe('web01 example');
  });

  it('拿掉控制字元（換行、NUL、Tab）', () => {
    expect(normalizeProjectSearchText('we\nb0\u00001\t')).toBe('web01');
  });

  it('只有空白或控制字元當作沒有輸入', () => {
    expect(normalizeProjectSearchText(' \n\t ')).toBe('');
  });

  it(`超過 ${String(PROJECT_SEARCH_MAX_LENGTH)} 字的截掉，截斷處的空白也去掉`, () => {
    // Arrange
    const raw = `${'a'.repeat(PROJECT_SEARCH_MAX_LENGTH - 1)} ${'b'.repeat(30)}`;

    // Act
    const text = normalizeProjectSearchText(raw);

    // Assert
    expect(text).toBe('a'.repeat(PROJECT_SEARCH_MAX_LENGTH - 1));
  });
});
