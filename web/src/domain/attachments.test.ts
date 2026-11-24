import { describe, expect, it } from 'vitest';
import { attachmentIcon, attachmentKind, formatAttachmentSize, isInlineImage } from './attachments';

describe('formatAttachmentSize', () => {
  it.each([
    [0, '0 B'],
    [512, '512 B'],
    [1536, '1.5 KB'],
    [340 * 1024, '340 KB'],
    [1.2 * 1024 * 1024, '1.2 MB'],
    [25 * 1024 * 1024, '25.0 MB'],
  ])('%d 位元組寫成 %s', (bytes, expected) => {
    expect(formatAttachmentSize(bytes)).toBe(expected);
  });
});

describe('isInlineImage', () => {
  it('只有 png、jpeg、gif、webp 在瀏覽器裡直接開，SVG 不算', () => {
    expect(['image/png', 'image/jpeg', 'image/gif', 'image/webp'].every(isInlineImage)).toBe(true);
    expect(isInlineImage('image/svg+xml')).toBe(false);
    expect(isInlineImage('application/pdf')).toBe(false);
  });
});

describe('attachmentKind', () => {
  it.each([
    ['image/svg+xml', 'logo.svg', 'image'],
    ['application/pdf', '規格.pdf', 'pdf'],
    ['application/octet-stream', '規格.PDF', 'pdf'],
    ['application/zip', 'a.zip', 'archive'],
    ['application/octet-stream', 'backup.tar', 'archive'],
    ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'a.xlsx', 'spreadsheet'],
    ['text/csv', 'a.csv', 'spreadsheet'],
    ['application/octet-stream', 'a.xlsx', 'spreadsheet'],
    ['application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'a.docx', 'document'],
    ['text/plain', 'a.txt', 'document'],
    ['application/octet-stream', 'noext', 'other'],
    ['application/octet-stream', '.gitignore', 'other'],
  ] as const)('%s／%s 是 %s', (contentType, fileName, expected) => {
    expect(attachmentKind(contentType, fileName)).toBe(expected);
  });

  it('每一類有自己的圖示', () => {
    expect(attachmentIcon('image/png', 'a.png')).toBe('fileImage');
    expect(attachmentIcon('application/octet-stream', 'x')).toBe('fileOther');
  });
});
