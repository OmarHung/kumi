import { describe, expect, it } from 'vitest';
import { aFile, attachment } from '../../test/attachments';
import { markdownToDoc } from '../rich-text/markdown/markdownDoc';
import {
  escapeFileName,
  growthOf,
  insertText,
  nextPlaceholder,
  pastedFileName,
  placeholderOf,
  referenceOf,
  replaceWithCaret,
  stripPlaceholders,
} from './editorUploadText';

describe('檔名與參照的寫法', () => {
  it('中括號與反斜線加反斜線跳脫，換行換成空白', () => {
    // Arrange & Act
    const escaped = escapeFileName('a[1]\\b\r\nc\nd.png');

    // Assert
    expect(escaped).toBe('a\\[1\\]\\\\b c d.png');
  });

  it('反引號與 < 也跳脫，免得被當成 code span 或自動連結吞掉參照', () => {
    // Arrange & Act
    const escaped = escapeFileName('a`b<c>.png');

    // Assert
    expect(escaped).toBe('a\\`b\\<c>.png');
  });

  it('星號與底線也跳脫：檔名 a*b*c.pdf 不會被讀成斜體', () => {
    // Arrange & Act
    const escaped = escapeFileName('a*b*c_d_.pdf');

    // Assert
    expect(escaped).toBe('a\\*b\\*c\\_d\\_.pdf');
  });

  it('傳完之後的參照讀回來，檔名還是原本的檔名', () => {
    // Arrange
    const reference = referenceOf(attachment(1, { fileName: 'a*b*c_d_.pdf', contentType: 'application/pdf' }));

    // Act
    const block = markdownToDoc(reference).content?.[0]?.content?.[0];

    // Assert
    expect(block?.type).toBe('attachmentFile');
    expect(block?.attrs?.name).toBe('a*b*c_d_.pdf');
  });

  it('圖片的記號是 ![…]()，其他是 […]()', () => {
    // Arrange & Act & Assert
    expect(placeholderOf(aFile('a.png', 10, 'image/png'))).toBe('![上傳中：a.png…]()');
    expect(placeholderOf(aFile('a.pdf'))).toBe('[上傳中：a.pdf…]()');
    expect(placeholderOf(aFile('a.svg', 10, 'image/svg+xml'))).toBe('[上傳中：a.svg…]()');
  });

  it('只有伺服器會直接顯示的四種圖片插成圖片', () => {
    // Arrange
    const png = attachment(1, { fileName: 'a.png', contentType: 'image/png' });
    const svg = attachment(2, { fileName: 'a.svg', contentType: 'image/svg+xml' });

    // Act & Assert
    expect(referenceOf(png)).toBe(`![a.png](attachment:${png.id})`);
    expect(referenceOf(svg)).toBe(`[a.svg](attachment:${svg.id})`);
  });

  it('同名的檔案：第二個記號加序號，兩個記號不會相同', () => {
    // Arrange
    const file = aFile('image.png', 10, 'image/png');
    const taken = new Set<string>();

    // Act
    const first = nextPlaceholder(file, (candidate) => taken.has(candidate));
    taken.add(first.placeholder);
    const second = nextPlaceholder(file, (candidate) => taken.has(candidate));

    // Assert
    expect(first).toEqual({ placeholder: '![上傳中：image.png…]()', label: 'image.png' });
    expect(second).toEqual({ placeholder: '![上傳中：image.png（2）…]()', label: 'image.png（2）' });
  });

  it('參照比記號長多少：圖片多 42 個字（](attachment: 加 36 個字的編號）', () => {
    // Arrange
    const file = aFile('a.png', 10, 'image/png');

    // Act & Assert
    expect(growthOf(file, placeholderOf(file))).toBe(42);
    expect(growthOf(aFile('a.pdf'), placeholderOf(aFile('a.pdf')))).toBe(42);
  });

  it('草稿裡殘留的「上傳中」記號整個拿掉（連同那一行的換行），其他的字不動', () => {
    // Arrange
    const draft = '前\n![上傳中：a.png…]()\n[上傳中：b\\]c.pdf（2）…]()後\n![正常](attachment:0190a100-0000-7000-8000-000000000001)';

    // Act
    const stripped = stripPlaceholders(draft);

    // Assert
    expect(stripped).toEqual({ value: '前\n後\n![正常](attachment:0190a100-0000-7000-8000-000000000001)', removed: 2 });
    expect(stripPlaceholders('沒有')).toEqual({ value: '沒有', removed: 0 });
  });

  it('貼上的截圖（image.png 這類沒有意義的名稱）改成「截圖 年-月-日 時.分」，同一分鐘加序號', () => {
    // Arrange
    const now = new Date(2026, 10, 19, 14, 5);
    const taken = new Set<string>();

    // Act
    const first = pastedFileName(aFile('image.png', 10, 'image/png'), now, taken);
    taken.add(first);
    const second = pastedFileName(aFile('image.png', 10, 'image/png'), now, taken);

    // Assert
    expect(first).toBe('截圖 2026-11-19 14.05.png');
    expect(second).toBe('截圖 2026-11-19 14.05 (2).png');
    expect(pastedFileName(aFile('報告.pdf'), now, taken)).toBe('報告.pdf');
    expect(pastedFileName(aFile('image.jpeg', 10, 'image/jpeg'), now, new Set())).toBe('截圖 2026-11-19 14.05.jpeg');
  });
});

describe('文字框的換字與插入', () => {
  it('換字時游標：在記號前不動、在記號後依長度差平移、在記號裡放到參照後面', () => {
    const value = 'AB[x]CD';

    expect(replaceWithCaret(value, '[x]', '[long]', 1, 1)).toEqual({ value: 'AB[long]CD', start: 1, end: 1 });
    expect(replaceWithCaret(value, '[x]', '[long]', 7, 7)).toEqual({ value: 'AB[long]CD', start: 10, end: 10 });
    expect(replaceWithCaret(value, '[x]', '[long]', 3, 5)).toEqual({ value: 'AB[long]CD', start: 8, end: 8 });
    expect(replaceWithCaret(value, '[x]', '', 7, 7)).toEqual({ value: 'ABCD', start: 4, end: 4 });
    expect(replaceWithCaret(value, '不存在', 'Y', 2, 2)).toBeNull();
  });

  it('換字用函式取代：檔名裡的 $ 不是特殊字元', () => {
    expect(replaceWithCaret('前 X 後', 'X', '$&$1', 0, 0)?.value).toBe('前 $&$1 後');
  });

  it('在選取的範圍換成插入的字，回傳游標的新位置', () => {
    expect(insertText('abcdef', 4, 2, 'XY')).toEqual({ value: 'abXYef', caret: 4 });
  });
});
