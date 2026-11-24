import { describe, expect, it } from 'vitest';
import { utf16Bytes } from '../test/itemImport';
import { decodeCsvBytes } from './csvDecode';

describe('decodeCsvBytes', () => {
  it('UTF-8 照原樣讀出來', () => {
    // Arrange
    const bytes = new TextEncoder().encode('標題,類型\n登入頁,任務');

    // Act
    const decoded = decodeCsvBytes(bytes);

    // Assert
    expect(decoded).toEqual({ text: '標題,類型\n登入頁,任務', encoding: 'utf-8' });
  });

  it('UTF-8 的 BOM 會去掉', () => {
    // Arrange
    const bytes = new Uint8Array([0xef, 0xbb, 0xbf, ...new TextEncoder().encode('標題')]);

    // Act
    const decoded = decodeCsvBytes(bytes);

    // Assert
    expect(decoded.text).toBe('標題');
    expect(decoded.encoding).toBe('utf-8');
  });

  it('不是合法的 UTF-8 時改用 Big5', () => {
    // Arrange：「中文」的 Big5 是 A4A4 A4E5，後面接逗號與兩個雙位元組字；單獨看不是合法的 UTF-8
    const bytes = new Uint8Array([0xa4, 0xa4, 0xa4, 0xe5, 0x2c, 0xbc, 0xd0, 0xc3, 0x44]);

    // Act
    const decoded = decodeCsvBytes(bytes);

    // Assert
    expect(decoded.encoding).toBe('big5');
    expect(decoded.text.startsWith('中文,')).toBe(true);
  });

  it('Excel 的 Unicode 文字（UTF-16 加 BOM）依 BOM 讀成 UTF-16', () => {
    // Arrange：「標題\t類型」的 UTF-16LE 與 UTF-16BE
    const text = '標題\t類型';
    const le = utf16Bytes(text, 'le');
    const be = utf16Bytes(text, 'be');

    // Act & Assert
    expect(decodeCsvBytes(le)).toEqual({ text, encoding: 'utf-16le' });
    expect(decodeCsvBytes(be)).toEqual({ text, encoding: 'utf-16be' });
  });

  it('純 ASCII 當成 UTF-8', () => {
    // Arrange & Act
    const decoded = decodeCsvBytes(new TextEncoder().encode('a,b'));

    // Assert
    expect(decoded.encoding).toBe('utf-8');
  });
});
