import { describe, expect, it } from 'vitest';
import { guessKind, rejectionReason, shortenFileName } from './attachmentRules';

const file = (name: string, size: number, type = '') => ({ name, size, type });
const MIB = 1024 * 1024;

describe('附件的前端檢查', () => {
  it.each([
    ['a.PNG', '', 'image'],
    ['a.bin', 'image/webp', 'image'],
    ['a.pdf', '', 'pdf'],
    ['a.md', '', 'text'],
    ['a.docx', '', null],
  ])('%s 猜成 %s', (name, type, kind) => {
    expect(guessKind(file(name, 1, type))).toBe(kind);
  });

  it('超過數量、太大、合計太大才擋；猜不出類型照 PDF 的上限', () => {
    expect(rejectionReason(file('a.md', 10), 5, 0)).toBe('一則訊息最多 5 個附件。');
    expect(rejectionReason(file('a.png', 5 * MIB + 1), 0, 0)).toBe('檔案超過 5.0 MB。');
    expect(rejectionReason(file('a.md', 256 * 1024 + 1), 0, 0)).toBe('檔案超過 256 KB。');
    expect(rejectionReason(file('a.xyz', 10 * MIB), 0, 0)).toBeNull();
    expect(rejectionReason(file('a.xyz', 10 * MIB + 1), 0, 0)).toBe('檔案超過 10.0 MB。');
    expect(rejectionReason(file('a.pdf', 10 * MIB), 1, 8 * MIB)).toBe('附件合計超過 15.0 MB。');
  });

  it('檔名太長中間省略、保留副檔名；短的照原樣', () => {
    expect(shortenFileName('短.png')).toBe('短.png');
    const shortened = shortenFileName('這是一個非常非常非常長的檔案名稱用來測試省略.png');
    expect(shortened).toContain('…');
    expect(shortened.endsWith('省略.png')).toBe(true);
  });
});
