export interface ContinuedList {
  readonly value: string;
  readonly caret: number;
}

// 編號的 . 與 ) 後面一定要有空白（不然「3.14」「2)」會被當成清單）；頓號是中文的寫法，常常不空格。
const NUMBERED = /^(\s*)(\d{1,9})([.)、])(\s*)(.*)$/;
const BULLET = /^(\s*)([-*•])(\s+)(.*)$/;

interface Marker {
  /** 新的一行開頭要寫什麼（縮排加記號加空白）。 */
  readonly next: string;
  /** 記號之後到游標之間有沒有文字。 */
  readonly hasText: boolean;
}

function markerOf(lineBeforeCaret: string): Marker | null {
  const numbered = NUMBERED.exec(lineBeforeCaret);
  if (numbered !== null) {
    const [, indent = '', digits = '', separator = '', gap = '', text = ''] = numbered;
    if (separator !== '、' && gap === '') return null;
    return { next: `${indent}${String(Number(digits) + 1)}${separator}${gap}`, hasText: text.trim() !== '' };
  }
  const bullet = BULLET.exec(lineBeforeCaret);
  if (bullet === null) return null;
  const [, indent = '', symbol = '', gap = '', text = ''] = bullet;
  return { next: `${indent}${symbol}${gap}`, hasText: text.trim() !== '' };
}

/**
 * 在清單的那一行按「換行」：接上下一個編號或同一個符號；只有記號的空項目就拿掉記號、結束清單。
 * 不是清單的行、有選取一段字時回 null（照瀏覽器原本的換行）。游標不在行尾時，後面的字帶到新的一行。
 */
export function continueList(value: string, selectionStart: number, selectionEnd: number): ContinuedList | null {
  if (selectionStart !== selectionEnd) return null;
  const lineStart = value.lastIndexOf('\n', selectionStart - 1) + 1;
  const marker = markerOf(value.slice(lineStart, selectionStart));
  if (marker === null) return null;
  const restOfLine = value.slice(selectionStart).split('\n', 1)[0] ?? '';
  if (!marker.hasText && restOfLine.trim() === '') {
    return { value: `${value.slice(0, lineStart)}${value.slice(selectionStart)}`, caret: lineStart };
  }
  const inserted = `\n${marker.next}`;
  return { value: `${value.slice(0, selectionStart)}${inserted}${value.slice(selectionStart)}`, caret: selectionStart + inserted.length };
}
