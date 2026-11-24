import { describe, expect, it } from 'vitest';
import { hexToHsv, hsvToHex, normalizeHex } from './hexColor';

describe('normalizeHex', () => {
  it.each([
    ['#1a2b3c', '#1a2b3c'],
    ['1A2B3C', '#1a2b3c'],
    ['#abc', '#aabbcc'],
    ['ABC', '#aabbcc'],
    ['  #00FF7f ', '#00ff7f'],
  ])('%s 整理成 %s', (input, expected) => {
    // Arrange & Act
    const result = normalizeHex(input);

    // Assert
    expect(result).toBe(expected);
  });

  it.each(['', '#', '#12345', '#1234567', 'red', '#ggg000', 'rgb(0,0,0)', '#12 345', '"/><script>'])(
    '看不懂的 %s 是 null',
    (input) => {
      // Arrange & Act
      const result = normalizeHex(input);

      // Assert
      expect(result).toBeNull();
    },
  );
});

describe('hexToHsv 與 hsvToHex', () => {
  it.each([
    ['#ff0000', { h: 0, s: 1, v: 1 }],
    ['#00ff00', { h: 120, s: 1, v: 1 }],
    ['#0000ff', { h: 240, s: 1, v: 1 }],
    ['#ffffff', { h: 0, s: 0, v: 1 }],
    ['#000000', { h: 0, s: 0, v: 0 }],
  ])('%s 是 %o', (hex, hsv) => {
    // Arrange & Act
    const result = hexToHsv(hex);

    // Assert
    expect(result.h).toBeCloseTo(hsv.h, 5);
    expect(result.s).toBeCloseTo(hsv.s, 5);
    expect(result.v).toBeCloseTo(hsv.v, 5);
  });

  it('換過去再換回來是同一個色碼', () => {
    // Arrange
    const colors = ['#e5322b', '#1f9d55', '#2f7ff5', '#808080', '#123456', '#fedcba', '#f5b800'];

    // Act
    const roundTrip = colors.map((hex) => hsvToHex(hexToHsv(hex)));

    // Assert
    expect(roundTrip).toEqual(colors);
  });

  it('超出範圍的值夾在範圍裡（色相 360 就是 0）', () => {
    // Arrange & Act
    const wrapped = hsvToHex({ h: 360, s: 1, v: 1 });
    const clamped = hsvToHex({ h: 120, s: 1.5, v: -0.2 });

    // Assert
    expect(wrapped).toBe('#ff0000');
    expect(clamped).toBe('#000000');
  });
});
