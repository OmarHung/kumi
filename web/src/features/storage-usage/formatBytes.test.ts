import { describe, expect, it } from 'vitest';
import { formatBytes, formatLimit } from './formatBytes';

describe('formatBytes', () => {
  it.each([
    [0, '0 B'],
    [512, '512 B'],
    [1536, '1.5 KiB'],
    [5 * 1024 * 1024, '5.0 MiB'],
    [1_288_490_188, '1.2 GiB'],
    [20 * 1024 ** 3, '20.0 GiB'],
    [3 * 1024 ** 4, '3.0 TiB'],
  ])('%d 位元組寫成 %s', (bytes, expected) => {
    // Arrange / Act / Assert
    expect(formatBytes(bytes)).toBe(expected);
  });
});

describe('formatLimit', () => {
  it.each([
    [20 * 1024 ** 3, '20 GiB'],
    [2 * 1024 ** 3, '2 GiB'],
    [512 * 1024 ** 2, '512 MiB'],
    [1.5 * 1024 ** 3, '1.5 GiB'],
    [0, '0 B'],
  ])('上限 %d 位元組寫成 %s', (bytes, expected) => {
    // Arrange / Act / Assert
    expect(formatLimit(bytes)).toBe(expected);
  });
});
